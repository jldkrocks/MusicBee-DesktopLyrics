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
            internal float[] Peaks;
            internal string Error;
            internal double Snap(double time)
            {
                if (Peaks == null) return time;
                double step = Length / Peaks.Length, bestDistance = .04, best = time;
                float maximum = 0; foreach (var p in Peaks) maximum = Math.Max(maximum,p);
                for (int i=1;i<Peaks.Length-1;i++)
                {
                    var candidate=Start+i*step; var distance=Math.Abs(candidate-time);
                    if(distance>bestDistance || Peaks[i]<maximum*.15f)continue;
                    var rise=Peaks[i]-Peaks[i-1];
                    if(rise>maximum*.04f && rise>=Peaks[i+1]-Peaks[i]){best=candidate;bestDistance=distance;}
                }
                return Math.Round(best,3);
            }
        }
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CancelFn();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl,CharSet=CharSet.Unicode)]
        private delegate int DecodeFn([MarshalAs(UnmanagedType.LPWStr)] string path,double start,double length,[Out] float[] peaks,int count,CancelFn cancel);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern IntPtr LoadLibraryEx(string path,IntPtr reserved,uint flags);
        [DllImport("kernel32.dll",CharSet=CharSet.Ansi,ExactSpelling=true)] private static extern IntPtr GetProcAddress(IntPtr library,string name);
        [DllImport("kernel32.dll")] private static extern bool FreeLibrary(IntPtr library);
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
            _task=Task.Run(()=>Read(path,start,length,token));
        }
        internal static Range Read(string path,double start,double length,CancellationToken token)
        {
            var result=new Range {Start=start,Length=length};IntPtr library=IntPtr.Zero;
            try{
                if(length<=0 || length>60 || start<0)throw new ArgumentException("Invalid waveform range.");
                var dll=Path.Combine(Path.GetDirectoryName(typeof(TimelineWaveform).Assembly.Location),"DesktopLyricsGpu."+(IntPtr.Size==4?"Win32":"x64")+".dll");
                library=LoadLibraryEx(dll,IntPtr.Zero,0x100|0x800);
                if(library==IntPtr.Zero)throw new InvalidOperationException("Audio helper unavailable.");
                var address=GetProcAddress(library,"DL_Waveform");if(address==IntPtr.Zero)throw new InvalidOperationException("Audio helper unavailable.");
                var decode=(DecodeFn)Marshal.GetDelegateForFunctionPointer(address,typeof(DecodeFn));
                var peaks=new float[Math.Min(16384,Math.Max(1024,(int)Math.Ceiling(length/.002)))];
                CancelFn cancel=()=>token.IsCancellationRequested?1:0;
                var hr=decode(path,start,length,peaks,peaks.Length,cancel);GC.KeepAlive(cancel);
                if(hr<0)throw new InvalidOperationException("Windows could not decode this audio range ("+hr.ToString("X8")+").");
                result.Peaks=peaks;
            }catch(Exception ex){result.Error="Waveform unavailable: "+ex.Message;}
            finally{if(library!=IntPtr.Zero)FreeLibrary(library);}
            return result;
        }
        public void Dispose(){_disposed=true;_cancel?.Cancel();if(_task!=null){var c=_cancel;_task.ContinueWith(t=>c.Dispose());}else _cancel?.Dispose();}
    }
}
