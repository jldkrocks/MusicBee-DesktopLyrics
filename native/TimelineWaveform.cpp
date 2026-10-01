#define NOMINMAX
#include <windows.h>
#include <mfapi.h>
#include <mfidl.h>
#include <mfreadwrite.h>
#include <wrl/client.h>
#include <algorithm>
#include <cmath>
#include <vector>
using Microsoft::WRL::ComPtr;
// Optional Windows decoder APIs are resolved dynamically: missing Media
// Foundation must not prevent loading the renderer on Windows N editions.
struct WaveRuntime {
 HMODULE platform=nullptr, reader=nullptr; bool com=false, started=false;
 decltype(&MFStartup) startup=nullptr; decltype(&MFShutdown) shutdown=nullptr;
 decltype(&MFCreateMediaType) mediaType=nullptr;
 decltype(&MFCreateSourceReaderFromURL) source=nullptr;
 ~WaveRuntime(){if(started)shutdown();if(reader)FreeLibrary(reader);if(platform)FreeLibrary(platform);if(com)CoUninitialize();}
 HRESULT Init(){
  HRESULT hr=CoInitializeEx(nullptr,COINIT_MULTITHREADED);if(FAILED(hr))return hr;com=true;
  platform=LoadLibraryExW(L"mfplat.dll",nullptr,LOAD_LIBRARY_SEARCH_SYSTEM32);
  reader=LoadLibraryExW(L"mfreadwrite.dll",nullptr,LOAD_LIBRARY_SEARCH_SYSTEM32);
  if(!platform||!reader)return E_NOTIMPL;
  startup=(decltype(startup))GetProcAddress(platform,"MFStartup");shutdown=(decltype(shutdown))GetProcAddress(platform,"MFShutdown");
  mediaType=(decltype(mediaType))GetProcAddress(platform,"MFCreateMediaType");source=(decltype(source))GetProcAddress(reader,"MFCreateSourceReaderFromURL");
  if(!startup||!shutdown||!mediaType||!source)return E_NOTIMPL;
  hr=startup(MF_VERSION,MFSTARTUP_LITE);started=SUCCEEDED(hr);return hr;
 }
};
static HRESULT DecodeWaveform(const wchar_t* path,double start,double duration,float* peaks,float* rms,float* bands,int count,int (__cdecl *cancel)()) noexcept {
 if(!path||!peaks||!cancel||!std::isfinite(start)||!std::isfinite(duration)||start<0||duration<=0||duration>60||count<1||count>16384)return E_INVALIDARG;
 try {
  std::fill(peaks,peaks+count,0.f);
  if(rms)std::fill(rms,rms+count,0.f);if(bands)std::fill(bands,bands+count*3,0.f);
  std::vector<double> energy(count*4,0.);std::vector<unsigned> framesPerBin(count,0);
  auto finish=[&](){for(int i=0;i<count;++i)if(framesPerBin[i]){
   if(rms)rms[i]=(float)std::sqrt(energy[i]/framesPerBin[i]);
   if(bands)for(int b=0;b<3;++b)bands[b*count+i]=(float)std::sqrt(energy[(b+1)*count+i]/framesPerBin[i]);
  }return S_OK;};
  WaveRuntime runtime;HRESULT hr=runtime.Init();if(FAILED(hr))return hr;
  ComPtr<IMFSourceReader> reader;hr=runtime.source(path,nullptr,&reader);if(FAILED(hr))return hr;
  hr=reader->SetStreamSelection((DWORD)MF_SOURCE_READER_ALL_STREAMS,FALSE);if(FAILED(hr))return hr;
  hr=reader->SetStreamSelection((DWORD)MF_SOURCE_READER_FIRST_AUDIO_STREAM,TRUE);if(FAILED(hr))return hr;
  ComPtr<IMFMediaType> type;hr=runtime.mediaType(&type);if(FAILED(hr))return hr;
  type->SetGUID(MF_MT_MAJOR_TYPE,MFMediaType_Audio);type->SetGUID(MF_MT_SUBTYPE,MFAudioFormat_PCM);type->SetUINT32(MF_MT_AUDIO_BITS_PER_SAMPLE,16);
  hr=reader->SetCurrentMediaType((DWORD)MF_SOURCE_READER_FIRST_AUDIO_STREAM,nullptr,type.Get());if(FAILED(hr))return hr;
  type.Reset();hr=reader->GetCurrentMediaType((DWORD)MF_SOURCE_READER_FIRST_AUDIO_STREAM,&type);if(FAILED(hr))return hr;
  UINT32 rate=0,channels=0,bits=0;
  type->GetUINT32(MF_MT_AUDIO_SAMPLES_PER_SECOND,&rate);type->GetUINT32(MF_MT_AUDIO_NUM_CHANNELS,&channels);type->GetUINT32(MF_MT_AUDIO_BITS_PER_SAMPLE,&bits);
  if(rate<1||rate>768000||channels<1||channels>32||bits!=16)return E_NOTIMPL;
  PROPVARIANT pos={};pos.vt=VT_I8;pos.hVal.QuadPart=(LONGLONG)(std::max(0.,start-.05)*10000000);
  hr=reader->SetCurrentPosition(GUID_NULL,pos);if(FAILED(hr))return hr;
  double low[32]={},mid[32]={};
  const double lowAlpha=1-std::exp(-2*3.141592653589793*180/rate);
  const double midAlpha=1-std::exp(-2*3.141592653589793*2500/rate);
  auto deadline=GetTickCount64()+15000;
  for(int packets=0;packets<100000;++packets){
   if(cancel()||GetTickCount64()>deadline)return HRESULT_FROM_WIN32(ERROR_CANCELLED);
   DWORD flags=0;LONGLONG stamp=0;ComPtr<IMFSample> sample;
   hr=reader->ReadSample((DWORD)MF_SOURCE_READER_FIRST_AUDIO_STREAM,0,nullptr,&flags,&stamp,&sample);if(FAILED(hr))return hr;
   if(flags&MF_SOURCE_READERF_CURRENTMEDIATYPECHANGED)return E_NOTIMPL;
   if(flags&MF_SOURCE_READERF_ENDOFSTREAM)return finish();
   double time=stamp/10000000.;if(time>=start+duration)return finish();
   if(!sample)continue;
   ComPtr<IMFMediaBuffer> buffer;hr=sample->ConvertToContiguousBuffer(&buffer);if(FAILED(hr))return hr;
   BYTE* data=nullptr;DWORD bytes=0;hr=buffer->Lock(&data,nullptr,&bytes);if(FAILED(hr))return hr;
   const auto values=(const short*)data;const auto frames=bytes/(2*channels);
   for(DWORD f=0;f<frames;++f){
    double t=time+(double)f/rate;int bin=(int)std::floor((t-start)/duration*count);
    // Preserve filter history across buffers, and warm it before the view.
    // Average channel energies, never mix opposite-polarity channels to mono.
    double power=0,lowPower=0,midPower=0,highPower=0;float peak=0;
    for(UINT32 c=0;c<channels;++c){
     double x=values[f*channels+c]/32768.;low[c]+=lowAlpha*(x-low[c]);mid[c]+=midAlpha*(x-mid[c]);
     power+=x*x;lowPower+=low[c]*low[c];midPower+=(mid[c]-low[c])*(mid[c]-low[c]);highPower+=(x-mid[c])*(x-mid[c]);
     peak=std::max(peak,(float)std::abs(x));
    }
    if(bin<0||bin>=count)continue;
    peaks[bin]=std::max(peaks[bin],peak);++framesPerBin[bin];
    energy[bin]+=power/channels;energy[count+bin]+=lowPower/channels;
    energy[2*count+bin]+=midPower/channels;energy[3*count+bin]+=highPower/channels;
   }
   buffer->Unlock();
  }
  return E_FAIL;
 }catch(...){return E_FAIL;}
}

// Keep the original entry point for ABI compatibility. The new export is
// resolved explicitly, so mixed helper versions fail safely before decoding.
extern "C" HRESULT __cdecl DL_Waveform(const wchar_t* path,double start,double duration,float* peaks,int count,int (__cdecl *cancel)()) noexcept {
 return DecodeWaveform(path,start,duration,peaks,nullptr,nullptr,count,cancel);
}
extern "C" HRESULT __cdecl DL_WaveformDetail(const wchar_t* path,double start,double duration,float* peaks,float* rms,float* bands,int count,int (__cdecl *cancel)()) noexcept {
 if(!rms||!bands)return E_INVALIDARG;
 return DecodeWaveform(path,start,duration,peaks,rms,bands,count,cancel);
}
