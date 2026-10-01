using System;
using System.Threading;
using System.Diagnostics;

namespace MusicBeePlugin
{
    internal sealed class PlaybackSnapshotReader : IDisposable
    {
        internal sealed class Snapshot
        {
            internal Plugin.PlayState State;
            internal int Position, Count, Duration;
            internal long PositionTimestamp;
            internal string TrackUrl;
            internal float[] Spectrum = new float[4096];
        }
        private readonly Plugin.MusicBeeApiInterface _api;
        private Snapshot _latest = new Snapshot();
        private int _busy, _generation;
        private readonly object _publication = new object();
        private volatile bool _disposed;
        internal Snapshot Latest => Volatile.Read(ref _latest);
        internal PlaybackSnapshotReader(Plugin.MusicBeeApiInterface api) { _api = api; }
        internal void Request(bool spectrum)
        {
            if (_disposed || Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return;
            int generation; lock (_publication) generation = _generation;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    if (_disposed) return;
                    var next = new Snapshot();
                    next.TrackUrl = _api.NowPlaying_GetFileUrl?.Invoke();
                    next.State = _api.Player_GetPlayState();
                    next.Position = Math.Max(0, _api.Player_GetPosition());
                    next.PositionTimestamp = Stopwatch.GetTimestamp();
                    next.Duration = Math.Max(0, _api.NowPlaying_GetDuration?.Invoke() ?? 0);
                    if (spectrum && next.State == Plugin.PlayState.Playing && _api.NowPlaying_GetSpectrumData != null)
                        next.Count = Math.Max(0, Math.Min(next.Spectrum.Length, _api.NowPlaying_GetSpectrumData(next.Spectrum)));
                    // Do not publish data spanning a track switch.
                    var trackUnchanged = next.TrackUrl == _api.NowPlaying_GetFileUrl?.Invoke();
                    lock (_publication)
                        if (!_disposed && generation == _generation && trackUnchanged)
                            Volatile.Write(ref _latest, next);
                }
                catch (Exception) { /* Retain the last complete sample until the player is available. */ }
                finally { Interlocked.Exchange(ref _busy, 0); }
            });
        }
        internal void PublishSeek(string trackUrl, int position)
        {
            lock (_publication)
            {
                if (_disposed) return;
                ++_generation;
                var prior = Latest;
                Volatile.Write(ref _latest, new Snapshot { TrackUrl = trackUrl, Position = Math.Max(0, position),
                    PositionTimestamp = Stopwatch.GetTimestamp(),
                    Duration = prior.TrackUrl == trackUrl ? prior.Duration : 0, State = prior.State,
                    Count = prior.Count, Spectrum = prior.Spectrum });
            }
        }
        public void Dispose() { _disposed = true; }
    }
}
