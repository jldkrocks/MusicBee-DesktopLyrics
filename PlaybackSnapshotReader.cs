using System;
using System.Threading;

namespace MusicBeePlugin
{
    internal sealed class PlaybackSnapshotReader : IDisposable
    {
        internal sealed class Snapshot
        {
            internal Plugin.PlayState State;
            internal int Position, Count;
            internal string TrackUrl;
            internal float[] Spectrum = new float[4096];
        }
        private readonly Plugin.MusicBeeApiInterface _api;
        private Snapshot _latest = new Snapshot();
        private int _busy;
        private volatile bool _disposed;
        internal Snapshot Latest => Volatile.Read(ref _latest);
        internal PlaybackSnapshotReader(Plugin.MusicBeeApiInterface api) { _api = api; }
        internal void Request(bool spectrum)
        {
            if (_disposed || Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    if (_disposed) return;
                    var next = new Snapshot();
                    next.TrackUrl = _api.NowPlaying_GetFileUrl?.Invoke();
                    next.State = _api.Player_GetPlayState();
                    next.Position = Math.Max(0, _api.Player_GetPosition());
                    if (spectrum && next.State == Plugin.PlayState.Playing && _api.NowPlaying_GetSpectrumData != null)
                        next.Count = Math.Max(0, Math.Min(next.Spectrum.Length, _api.NowPlaying_GetSpectrumData(next.Spectrum)));
                    // Do not publish data spanning a track switch.
                    if (!_disposed && next.TrackUrl == _api.NowPlaying_GetFileUrl?.Invoke())
                        Volatile.Write(ref _latest, next);
                }
                catch (Exception) { /* Retain the last complete sample until the player is available. */ }
                finally { Interlocked.Exchange(ref _busy, 0); }
            });
        }
        public void Dispose() { _disposed = true; }
    }
}
