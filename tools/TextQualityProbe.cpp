// Isolated quality experiment, never loaded by MusicBee. Uses Windows DirectWrite,
// Direct2D and WIC. Software raster output is for comparison, not a GPU FPS claim.
#include <windows.h>
#include <d2d1.h>
#include <dwrite.h>
#include <wincodec.h>
#include <wrl/client.h>
#include <filesystem>
#include <fstream>
#include <sstream>
#include <vector>
#include <string>
#include <chrono>
#include <cstdio>
#include <algorithm>
using Microsoft::WRL::ComPtr;
static void Check(HRESULT hr) { if (FAILED(hr)) throw hr; }
class Outlines final : public IDWriteTextRenderer {
    ULONG refs = 1;
public:
    ID2D1Factory* factory;
    std::vector<ComPtr<ID2D1TransformedGeometry>> paths;
    D2D1_RECT_F bounds = {};
    explicit Outlines(ID2D1Factory* f) : factory(f) {}
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** p) override {
        if (!p) return E_POINTER;
        if (iid == __uuidof(IUnknown) || iid == __uuidof(IDWritePixelSnapping) || iid == __uuidof(IDWriteTextRenderer)) {
            *p = static_cast<IDWriteTextRenderer*>(this); AddRef(); return S_OK;
        } *p = nullptr; return E_NOINTERFACE;
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return ++refs; }
    ULONG STDMETHODCALLTYPE Release() override { ULONG n = --refs; if (!n) delete this; return n; }
    HRESULT STDMETHODCALLTYPE IsPixelSnappingDisabled(void*, BOOL* value) override { *value = TRUE; return S_OK; }
    HRESULT STDMETHODCALLTYPE GetCurrentTransform(void*, DWRITE_MATRIX* m) override { *m = {1,0,0,1,0,0}; return S_OK; }
    HRESULT STDMETHODCALLTYPE GetPixelsPerDip(void*, FLOAT* v) override { *v = 1; return S_OK; }
    HRESULT STDMETHODCALLTYPE DrawGlyphRun(void*, FLOAT x, FLOAT y, DWRITE_MEASURING_MODE,
        DWRITE_GLYPH_RUN const* run, DWRITE_GLYPH_RUN_DESCRIPTION const*, IUnknown*) override {
        try {
            ComPtr<ID2D1PathGeometry> path; Check(factory->CreatePathGeometry(&path));
            ComPtr<ID2D1GeometrySink> sink; Check(path->Open(&sink));
            Check(run->fontFace->GetGlyphRunOutline(run->fontEmSize, run->glyphIndices, run->glyphAdvances,
                run->glyphOffsets, run->glyphCount, run->isSideways, run->bidiLevel % 2, sink.Get()));
            Check(sink->Close());
            ComPtr<ID2D1TransformedGeometry> transformed;
            Check(factory->CreateTransformedGeometry(path.Get(), D2D1::Matrix3x2F::Translation(x,y), &transformed));
            D2D1_RECT_F b; Check(transformed->GetBounds(nullptr, &b));
            if (b.right >= b.left && b.bottom >= b.top) {
                if (paths.empty()) bounds = b;
                else { bounds.left = (std::min)(bounds.left,b.left); bounds.top = (std::min)(bounds.top,b.top);
                    bounds.right = (std::max)(bounds.right,b.right); bounds.bottom = (std::max)(bounds.bottom,b.bottom); }
            }
            paths.push_back(transformed); return S_OK;
        } catch (HRESULT hr) { return hr; }
    }
    HRESULT STDMETHODCALLTYPE DrawUnderline(void*,FLOAT,FLOAT,DWRITE_UNDERLINE const*,IUnknown*) override { return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE DrawStrikethrough(void*,FLOAT,FLOAT,DWRITE_STRIKETHROUGH const*,IUnknown*) override { return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE DrawInlineObject(void*,FLOAT,FLOAT,IDWriteInlineObject*,BOOL,BOOL,IUnknown*) override { return E_NOTIMPL; }
};
int wmain(int argc, wchar_t** argv) {
    if (argc != 2) { std::puts("TextQualityProbe <fixture-folder>"); return 2; }
    HRESULT init = CoInitializeEx(nullptr,COINIT_APARTMENTTHREADED); if (FAILED(init)) return 1;
    int result = 0;
    try {
        ComPtr<ID2D1Factory> factory; Check(D2D1CreateFactory(D2D1_FACTORY_TYPE_SINGLE_THREADED,factory.GetAddressOf()));
        ComPtr<IDWriteFactory> dw; Check(DWriteCreateFactory(DWRITE_FACTORY_TYPE_SHARED,__uuidof(IDWriteFactory),reinterpret_cast<IUnknown**>(dw.GetAddressOf())));
        ComPtr<IWICImagingFactory> wic; Check(CoCreateInstance(CLSID_WICImagingFactory,nullptr,CLSCTX_INPROC_SERVER,IID_PPV_ARGS(&wic)));
        std::filesystem::path folder(argv[1]); std::ofstream metrics(folder / "directwrite-metrics.csv");
        metrics << "id,dwrite_width,dwrite_height,outline_prepare_ms,lines\n";
        int count = 0;
        for (auto const& entry : std::filesystem::directory_iterator(folder)) {
            if (entry.path().extension() != L".fixture") continue;
            std::ifstream input(entry.path(),std::ios::binary|std::ios::ate);
            auto length = static_cast<size_t>(input.tellg()); input.seekg(0);
            if (length < 4 || length % 2) throw E_INVALIDARG;
            std::wstring contents(length/2,L'\0'); input.read(reinterpret_cast<char*>(&contents[0]),length);
            if (contents[0] != 0xfeff) throw E_INVALIDARG;
            std::wistringstream lines(contents.substr(1)); std::wstring line;
            auto next = [&]() { std::getline(lines,line); if (!line.empty() && line.back()==L'\r')line.pop_back(); return line; };
            auto width = std::stoul(next()), height = std::stoul(next()); float points = std::stof(next());
            int style = std::stoi(next()); std::wstring font = next(); float scale = std::stof(next());
            std::wstring text((std::istreambuf_iterator<wchar_t>(lines)),{});
            ComPtr<IDWriteTextFormat> format;
            Check(dw->CreateTextFormat(font.c_str(),nullptr,style&1?DWRITE_FONT_WEIGHT_BOLD:DWRITE_FONT_WEIGHT_NORMAL,
                style&2?DWRITE_FONT_STYLE_ITALIC:DWRITE_FONT_STYLE_NORMAL,DWRITE_FONT_STRETCH_NORMAL,points*96.f/72.f,L"en-gb",&format));
            Check(format->SetWordWrapping(DWRITE_WORD_WRAPPING_NO_WRAP));
            Check(format->SetTextAlignment(DWRITE_TEXT_ALIGNMENT_CENTER));
            Check(format->SetParagraphAlignment(DWRITE_PARAGRAPH_ALIGNMENT_CENTER));
            auto start = std::chrono::steady_clock::now();
            ComPtr<IDWriteTextLayout> layout;
            Check(dw->CreateTextLayout(text.c_str(),static_cast<UINT32>(text.size()),format.Get(),(float)width,(float)height,&layout));
            ComPtr<Outlines> outlines; outlines.Attach(new Outlines(factory.Get()));
            Check(layout->Draw(nullptr,outlines.Get(),0,0));
            auto ms = std::chrono::duration<double,std::milli>(std::chrono::steady_clock::now()-start).count();
            DWRITE_TEXT_METRICS tm; Check(layout->GetMetrics(&tm));
            auto b = outlines->bounds; float bw=b.right-b.left,bh=b.bottom-b.top;
            metrics << entry.path().stem().string() << ',' << bw << ',' << bh << ',' << ms << ',' << tm.lineCount << '\n';
            ComPtr<IWICBitmap> bitmap;
            Check(wic->CreateBitmap(width,height,GUID_WICPixelFormat32bppPBGRA,WICBitmapCacheOnLoad,&bitmap));
            ComPtr<ID2D1RenderTarget> target;
            Check(factory->CreateWicBitmapRenderTarget(bitmap.Get(),D2D1::RenderTargetProperties(D2D1_RENDER_TARGET_TYPE_SOFTWARE,
                D2D1::PixelFormat(DXGI_FORMAT_B8G8R8A8_UNORM,D2D1_ALPHA_MODE_PREMULTIPLIED),96,96),&target));
            ComPtr<ID2D1SolidColorBrush> white,black,shadow;
            Check(target->CreateSolidColorBrush(D2D1::ColorF(1,1,1),&white));
            Check(target->CreateSolidColorBrush(D2D1::ColorF(0,0,0),&black));
            Check(target->CreateSolidColorBrush(D2D1::ColorF(0,0,0,170.f/255),&shadow));
            ComPtr<ID2D1StrokeStyle> stroke; auto props=D2D1::StrokeStyleProperties();props.lineJoin=D2D1_LINE_JOIN_ROUND;
            Check(factory->CreateStrokeStyle(props,nullptr,0,&stroke));
            auto transform = D2D1::Matrix3x2F::Translation(-b.left,-b.top)*D2D1::Matrix3x2F::Scale(scale,scale)*
                D2D1::Matrix3x2F::Translation((width-bw*scale)/2,(height-bh*scale)/2);
            target->BeginDraw();target->Clear(D2D1::ColorF(22.f/255,29.f/255,46.f/255));
            target->SetTransform(transform*D2D1::Matrix3x2F::Translation(scale,2*scale));
            for(auto const& path:outlines->paths)target->FillGeometry(path.Get(),shadow.Get());
            target->SetTransform(transform);
            for(auto const& path:outlines->paths) {
                target->DrawGeometry(path.Get(),black.Get(),(std::max)(1.5f,(std::min)(3.f,points/20)),stroke.Get());
                target->FillGeometry(path.Get(),white.Get());
            }
            Check(target->EndDraw());
            auto output = folder / (entry.path().stem().wstring()+L"-directwrite.png");
            ComPtr<IWICStream> stream;Check(wic->CreateStream(&stream));Check(stream->InitializeFromFilename(output.c_str(),GENERIC_WRITE));
            ComPtr<IWICBitmapEncoder> encoder;Check(wic->CreateEncoder(GUID_ContainerFormatPng,nullptr,&encoder));Check(encoder->Initialize(stream.Get(),WICBitmapEncoderNoCache));
            ComPtr<IWICBitmapFrameEncode> frame;Check(encoder->CreateNewFrame(&frame,nullptr));Check(frame->Initialize(nullptr));Check(frame->SetSize(width,height));
            auto pixelFormat=GUID_WICPixelFormat32bppBGRA;Check(frame->SetPixelFormat(&pixelFormat));Check(frame->WriteSource(bitmap.Get(),nullptr));Check(frame->Commit());Check(encoder->Commit());count++;
        }
        std::printf("Rendered %d comparison fixtures. Production text renderer unchanged.\n",count);
        if (!count) result=2;
    } catch (HRESULT hr) { std::printf("Text quality probe failed 0x%08lx\n",(unsigned long)hr);result=1; }
      catch (std::exception const& e) { std::printf("Text quality probe failed: %s\n",e.what());result=1; }
    CoUninitialize();return result;
}
