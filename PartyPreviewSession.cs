using System;

namespace MusicBeePlugin
{
    // Commands complete asynchronously on the editor UI. A generation prevents
    // an old completion from resuming or seeking a different song/session.
    internal sealed class PartyPreviewSession
    {
        private readonly Action<bool, Action<bool>> _play;
        private readonly Action<double> _seek;
        private readonly Func<bool> _available;
        private readonly Action<string> _status;
        private int _generation;
        private bool _listening, _commandPending, _stopRequested;
        private double _anchor, _end;
        private double _loopStart;
        private bool _waitingForLoopPosition;
        internal bool Looping {get;private set;}
        private DateTime _started;
        internal bool Active { get; private set; }
        internal PartyPreviewSession(Action<bool, Action<bool>> play, Action<double> seek,
            Func<bool> available, Action<string> status)
        { _play = play; _seek = seek; _available = available; _status = status; }

        internal void Start(double anchor, double duration)
        {
            if (Active) { Stop(); return; }
            if (!_available() || duration <= 0) return;
            _anchor = Math.Max(0, Math.Min(duration, anchor));
            Looping=false;_waitingForLoopPosition=false;
            _end = Math.Min(duration, _anchor + 1.5);
            var start = Math.Max(0, _anchor - .5);
            int generation = ++_generation;
            Active = true; _listening = false; _started = DateTime.UtcNow;
            _status("Preparing preview...");
            Command(false, generation, () => {
                _seek(start);
                Command(true, generation, () => { _listening = true; _started = DateTime.UtcNow; _status("Previewing; playback will pause and return to your selected time."); });
            });
        }
        internal void StartLoop(double start,double end,double duration) {
            if(Active){Stop();return;}
            if(!_available() || double.IsNaN(start+end+duration) || double.IsInfinity(start+end+duration) || start<0 || end<=start || end>duration)return;
            _anchor=start;_loopStart=Math.Max(0,start-.5);_end=end;Looping=true;
            Active=true;_listening=false;_started=DateTime.UtcNow;
            Repeat(++_generation);
        }
        private void Repeat(int generation){
            _listening=false;_waitingForLoopPosition=true;
            Command(false,generation,()=>{_seek(_loopStart);Command(true,generation,()=>{
                _listening=true;_started=DateTime.UtcNow;_status("Looping the selected range with 0.5 s lead-in. Stop preview returns paused to its start.");
            });});
        }
        private void Command(bool playing, int generation, Action next)
        {
            try
            {
                _commandPending = true;
                _play(playing, accepted => {
                    if (generation != _generation || !Active) return;
                    _commandPending = false;
                    if (!_available()) { Cancel(); return; }
                    if (!accepted) { Cancel(); _status("MusicBee could not complete the preview command."); return; }
                    if (_stopRequested) { _stopRequested = false; Stop(); return; }
                    try { next(); }
                    catch (Exception ex) { Cancel(); _status(ex.Message); }
                });
            }
            catch (Exception ex) { Cancel(); _status(ex.Message); }
        }
        internal void Tick(double? position)
        {
            if (!Active) return;
            if (!position.HasValue || !_available()) { Cancel(); return; }
            if (_listening && _waitingForLoopPosition && position.Value < _end) _waitingForLoopPosition = false;
            if(_listening && !_waitingForLoopPosition && position.Value>=_end){if(Looping)Repeat(++_generation);else Stop();}
            else if(_listening && (DateTime.UtcNow-_started).TotalSeconds>= (Looping?_end-_loopStart+6:6))Stop();
        }
        internal void Stop()
        {
            if (!Active) return;
            if (_commandPending) { _stopRequested = true; return; }
            int generation = ++_generation; _listening = false;
            Command(false, generation, () => { _seek(_anchor); Active = Looping = false; _status("Preview finished. Returned to the selected time, paused."); });
        }
        internal void Cancel() { ++_generation; Active = _listening = _stopRequested = _commandPending = Looping = false; }
    }
}
