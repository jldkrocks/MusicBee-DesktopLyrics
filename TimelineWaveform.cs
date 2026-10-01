using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace MusicBeePlugin
{
    // A bounded nearby-chunk cache, one worker and no disk cache. Results are polled by
    // the editor, so closing it never leaves an Invoke on a disposed handle.
    internal sealed class TimelineWaveform : IDisposable
    {
        internal sealed class Range
        {
            internal double Start, Length;
            internal Range[] Tiles;
            internal bool HasSamples => Peaks != null || (Tiles != null && Tiles.Any(t=>t.Peaks!=null));
            internal bool Sample(double start,double end,out float peak,out float rms,out float attack)
            {
                peak=rms=attack=0;
                if(Tiles!=null){bool found=false;foreach(var tile in Tiles){float p,r,a;if(tile.Sample(start,end,out p,out r,out a)){found=true;peak=Math.Max(peak,p);rms=Math.Max(rms,r);attack=Math.Max(attack,a);}}return found;}
                int first,last;if(!PixelBins(start,end,out first,out last))return false;
                double power=0;
                for(int i=first;i<last;i++){peak=Math.Max(peak,Peaks[i]);if(Rms!=null)power+=Rms[i]*Rms[i];if(Attacks!=null)attack=Math.Max(attack,Attacks[i]/AttackDisplayMaximum);}
                rms=(float)Math.Sqrt(power/(last-first));return true;
            }
            internal float[] Peaks, Rms, Attacks;
            internal float AttackDisplayMaximum=1;
            internal string Error;
            internal bool Covers(double start, double length)
            {
                if(Tiles==null)return Peaks!=null && start>=Start-1e-7 && start+length<=Start+Length+1e-7;
                double covered=start;
                foreach(var tile in Tiles){if(tile.Start>covered+1e-7)break;if(tile.Peaks!=null && tile.Start+tile.Length>covered)covered=tile.Start+tile.Length;}
                return covered>=start+length-1e-7;
            }
            internal bool PixelBins(double start, double end, out int first, out int last)
            {
                first = last = 0;
                if (Peaks == null || Length <= 0 || end <= Start || start >= Start + Length) return false;
                first = Math.Max(0, Math.Min(Peaks.Length - 1, (int)Math.Floor((start - Start) / Length * Peaks.Length)));
                last = Math.Max(first + 1, Math.Min(Peaks.Length, (int)Math.Ceiling((end - Start) / Length * Peaks.Length)));
                return true;
            }
            internal double Snap(double time)
            {
                if(Tiles!=null){double bestTile=time,distance=.040001;foreach(var tile in Tiles){var snapped=tile.Snap(time);if(snapped!=time&&Math.Abs(snapped-time)<distance){bestTile=snapped;distance=Math.Abs(snapped-time);}}return bestTile;}
                if (Attacks == null) return time;
                double step = Length / Attacks.Length, bestDistance = .04, best = time;
                for (int i=1;i<Attacks.Length-1;i++)
                {
                    var candidate=Start+i*step; var distance=Math.Abs(candidate-time);
                    if(distance>bestDistance || Attacks[i]<AttackDisplayMaximum*.15f)continue;
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
        internal const int CacheLimit=16;
        internal const double ChunkSeconds=30;
        private readonly List<Range> _cache=new List<Range>();
        private readonly HashSet<double> _failed=new HashSet<double>();
        private string _path;
        private double _start=-1,_length,_pendingStart;
        private DateTime _changed;
        private bool _disposed;
        private readonly Func<string,double,double,CancellationToken,Range> _read;
        internal TimelineWaveform(Func<string,double,double,CancellationToken,Range> read=null){_read=read??Read;}
        internal Range Data {get;private set;}
        internal string Status {get;private set;}="Loading waveform...";
        internal void Update(string path,double start,double length,double duration=0)
        {
            if(_disposed)return;
            bool newPath=!string.Equals(path,_path,StringComparison.OrdinalIgnoreCase);
            if(newPath){_path=path;_cache.Clear();_failed.Clear();Data=null;_cancel?.Cancel();}
            if(newPath||start!=_start||length!=_length){_start=start;_length=length;_changed=DateTime.UtcNow;}
            double end=start+length;
            if(_task!=null){
                // Cancel obsolete far-away work, but let nearby prefetch finish.
                if(_pendingStart+ChunkSeconds<start-2*ChunkSeconds||_pendingStart>end+2*ChunkSeconds)_cancel.Cancel();
                if(!_task.IsCompleted)return;
                var result=_task.Result;_task=null;
                if(!_cancel.IsCancellationRequested){
                    if(result.Peaks!=null){
                        _cache.RemoveAll(t=>t.Start==result.Start);_cache.Add(result);
                        while(_cache.Count>CacheLimit){var farthest=_cache.OrderByDescending(t=>Math.Abs(t.Start+ t.Length/2-(start+length/2))).First();_cache.Remove(farthest);}
                        var tiles=_cache.OrderBy(t=>t.Start).ToArray();Data=new Range {Start=tiles[0].Start,Length=tiles[tiles.Length-1].Start+tiles[tiles.Length-1].Length-tiles[0].Start,Tiles=tiles};
                        Status="Waveform ready; nearby audio is cached.";
                    }else{if(_failed.Count>=CacheLimit)_failed.Clear();_failed.Add(result.Start);Status=result.Error;}
                }
                _cancel.Dispose();_cancel=null;
            }
            if(length<=0||length>300){Status="Zoom to five minutes or less for waveform.";return;}
            if((DateTime.UtcNow-_changed).TotalMilliseconds<120)return;
            if(string.IsNullOrEmpty(path)||path.StartsWith(@"\\")||!File.Exists(path)){Status="Waveform unavailable: local audio file required.";return;}
            // Visible chunks first, then two neighbours in either direction.
            var wanted=new List<double>();double first=Math.Floor(Math.Max(0,start)/ChunkSeconds)*ChunkSeconds;
            for(double t=first;t<end-1e-7;t+=ChunkSeconds)wanted.Add(t);
            double after=Math.Ceiling(end/ChunkSeconds)*ChunkSeconds;
            for(int i=0;i<2;i++){wanted.Add(after+i*ChunkSeconds);wanted.Add(first-(i+1)*ChunkSeconds);}
            foreach(double t in wanted){
                if(t<0||(duration>0&&t>=duration)||_failed.Contains(t)||_cache.Any(r=>r.Start==t))continue;
                double span=duration>0?Math.Min(ChunkSeconds,duration-t):ChunkSeconds;
                _pendingStart=t;_cancel=new CancellationTokenSource();var token=_cancel.Token;
                _task=Task.Run(()=>{bool entered=false;
                    try{DecodeGate.Wait(token);entered=true;return _read(path,t,span,token);}
                    catch(Exception ex){return new Range {Start=t,Length=span,Error="Waveform unavailable: "+ex.Message};}
                    finally{if(entered)DecodeGate.Release();}
                });
                if(Data==null)Status="Loading waveform...";
                return;
            }
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
                result.Peaks=peaks;result.Rms=rms;result.Attacks=BuildAttacks(bands,length);result.AttackDisplayMaximum=AttackScale(result.Attacks);
            }catch(Exception ex){result.Error="Waveform unavailable: "+ex.Message;}
            finally{if(library!=IntPtr.Zero)FreeLibrary(library);}
            return result;
        }
        internal static float AttackScale(float[] attacks)
        {
            var nonzero=Array.FindAll(attacks,value=>value>0);
            if(nonzero.Length==0)return 1;
            Array.Sort(nonzero);
            // Clip only the display of the strongest outliers. Keep raw
            // relative values for local-maximum timing and snapping.
            return Math.Max(.05f,nonzero[(int)Math.Floor((nonzero.Length-1)*.98)]);
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
