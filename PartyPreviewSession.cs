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
            if (_listening && (position.Value >= _end || (DateTime.UtcNow - _started).TotalSeconds >= 6)) Stop();
        }
        internal void Stop()
        {
            if (!Active) return;
            if (_commandPending) { _stopRequested = true; return; }
            int generation = ++_generation; _listening = false;
            Command(false, generation, () => { _seek(_anchor); Active = false; _status("Preview finished. Returned to the selected time, paused."); });
        }
        internal void Cancel() { ++_generation; Active = _listening = _stopRequested = _commandPending = false; }
    }
}
