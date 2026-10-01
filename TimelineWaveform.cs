using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace MusicBeePlugin
{
    // One bounded range, one worker and no disk cache. Results are polled by
    // the editor, so closing it never leaves an Invoke on a disposed handle.
    internal sealed class TimelineWaveform : IDisposable
    {
        internal sealed class Range
        {
            internal double Start, Length;
            internal float[] Peaks, Rms, Attacks;
            internal string Error;
            internal double Snap(double time)
            {
                if (Attacks == null) return time;
                double step = Length / Attacks.Length, bestDistance = .04, best = time;
                for (int i=1;i<Attacks.Length-1;i++)
                {
                    var candidate=Start+i*step; var distance=Math.Abs(candidate-time);
                    if(distance>bestDistance || Attacks[i]<.15f)continue;
                    if(Attacks[i]>=Attacks[i-1] && Attacks[i]>Attacks[i+1]){best=candidate;bestDistance=distance;}
                }
                return Math.Round(best,3);
            }
        }
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CancelFn();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl,CharSet=CharSet.Unicode)]
        private delegate int DecodeFn([MarshalAs(UnmanagedType.LPWStr)] string path,double start,double length,[Out] float[] peaks,[Out] float[] rms,[Out] float[] bands,int count,CancelFn cancel);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern IntPtr LoadLibraryEx(string path,IntPtr reserved,uint flags);
        [DllImport("kernel32.dll",CharSet=CharSet.Ansi,ExactSpelling=true)] private static extern IntPtr GetProcAddress(IntPtr library,string name);
        [DllImport("kernel32.dll")] private static extern bool FreeLibrary(IntPtr library);
        private static readonly SemaphoreSlim DecodeGate = new SemaphoreSlim(1,1);
        private Task<Range> _task;
        private CancellationTokenSource _cancel;
        private double _start=-1,_length;
        private DateTime _changed;
        private bool _disposed,_requested;
        internal Range Data {get;private set;}
        internal string Status {get;private set;}="Zoom to 60 seconds or less for waveform.";
        internal void Update(string path,double start,double length)
        {
            if(_disposed)return;
            if(start!=_start || length!=_length){_start=start;_length=length;_changed=DateTime.UtcNow;_requested=false;Data=null;_cancel?.Cancel();}
            if(_task!=null){
                if(!_task.IsCompleted)return;
                var result=_task.Result;_task=null;
                if(!_cancel.IsCancellationRequested){Data=result;Status=result.Error??"Waveform: audio peaks, not automatically identified beats.";}
                _cancel.Dispose();_cancel=null;
            }
            if(_requested || (DateTime.UtcNow-_changed).TotalMilliseconds<300)return;
            if(length<=0 || length>60){Status="Zoom to 60 seconds or less for waveform.";return;}
            _requested=true;
            if(string.IsNullOrEmpty(path)||path.StartsWith(@"\\")||!File.Exists(path)){Status="Waveform unavailable: local audio file required.";return;}
            Status="Loading waveform...";_cancel=new CancellationTokenSource();var token=_cancel.Token;
            _task=Task.Run(()=>{
                bool entered=false;
                try { DecodeGate.Wait(token);entered=true;return Read(path,start,length,token); }
                catch(OperationCanceledException){return new Range {Start=start,Length=length,Error="Waveform cancelled."};}
                finally{if(entered)DecodeGate.Release();}
            });
        }
        internal static Range Read(string path,double start,double length,CancellationToken token)
        {
            var result=new Range {Start=start,Length=length};IntPtr library=IntPtr.Zero;
            try{
                if(length<=0 || length>60 || start<0)throw new ArgumentException("Invalid waveform range.");
                var dll=Path.Combine(Path.GetDirectoryName(typeof(TimelineWaveform).Assembly.Location),"DesktopLyricsGpu."+(IntPtr.Size==4?"Win32":"x64")+".dll");
                library=LoadLibraryEx(dll,IntPtr.Zero,0x100|0x800);
                if(library==IntPtr.Zero)throw new InvalidOperationException("Audio helper unavailable.");
                var address=GetProcAddress(library,"DL_WaveformDetail");if(address==IntPtr.Zero)throw new InvalidOperationException("Audio helper unavailable.");
                var decode=(DecodeFn)Marshal.GetDelegateForFunctionPointer(address,typeof(DecodeFn));
                var peaks=new float[Math.Min(16384,Math.Max(1024,(int)Math.Ceiling(length/.002)))];
                CancelFn cancel=()=>token.IsCancellationRequested?1:0;
                var rms=new float[peaks.Length];var bands=new float[peaks.Length*3];
                var hr=decode(path,start,length,peaks,rms,bands,peaks.Length,cancel);GC.KeepAlive(cancel);
                if(hr<0)throw new InvalidOperationException("Windows could not decode this audio range ("+hr.ToString("X8")+").");
                result.Peaks=peaks;result.Rms=rms;result.Attacks=BuildAttacks(bands,length);
            }catch(Exception ex){result.Error="Waveform unavailable: "+ex.Message;}
            finally{if(library!=IntPtr.Zero)FreeLibrary(library);}
            return result;
        }
        // Positive energy changes in low/mid/high bands. A centred 8ms
        // window separates sustained loudness from attacks without estimating BPM.
        internal static float[] BuildAttacks(float[] bands,double length)
        {
            int count=bands.Length/3;var result=new float[count];
            if(count<3 || length<=0)return result;
            double step=length/count;int window=Math.Max(1,(int)Math.Round(.008/step));
            var sums=new double[count+1];
            for(int band=0;band<3;band++){
                sums[0]=0;for(int i=0;i<count;i++){double v=bands[band*count+i];sums[i+1]=sums[i]+v*v;}
                for(int i=window;i<count-window;i++){
                    double before=Math.Sqrt((sums[i]-sums[i-window])/window);
                    double after=Math.Sqrt((sums[i+window]-sums[i])/window);
                    // A floor prevents tiny background fluctuations dominating
                    // a loud passage; retain absolute change as well as contrast.
                    result[i]+=(float)(Math.Max(0,after-before)/Math.Sqrt(.01+before));
                }
            }
            float maximum=0;foreach(float v in result)maximum=Math.Max(maximum,v);
            if(maximum>.015f)for(int i=0;i<count;i++)result[i]/=maximum;
            else Array.Clear(result,0,result.Length);
            return result;
        }
        public void Dispose(){_disposed=true;_cancel?.Cancel();if(_task!=null){var c=_cancel;_task.ContinueWith(t=>c.Dispose());}else _cancel?.Dispose();}
    }
}
