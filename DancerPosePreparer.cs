using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading.Tasks;

namespace MusicBeePlugin
{
    // Raster preparation only. Never reads playback state or calls a window/GPU.
    // One shared serial worker prevents rapid resize/restore cycles from spawning
    // concurrent PNG decoders. Requests replace obsolete sizes, not append to a queue.
    internal sealed class DancerPosePreparer : IDisposable
    {
        private static readonly TaskScheduler Scheduler =
            new ConcurrentExclusiveSchedulerPair(TaskScheduler.Default, 1).ExclusiveScheduler;
        private readonly object _gate = new object();
        private readonly Bitmap[] _sources = new Bitmap[2];
        private readonly Bitmap[] _ready = new Bitmap[8];
        private readonly Size[] _sizes = new Size[2];
        private readonly int[] _generation = new int[2];
        private readonly int[] _priority = new int[2];
        private readonly bool[] _produced = new bool[8];
        private bool _running, _disposed;
        private int _nextCharacter;
        private Exception _failure;
        internal bool ReleaseSourcesWhenIdle;

        internal void Request(int character, Size size, int frame)
        {
            if (character < 0 || character > 1 || frame < 0 || frame > 9 || frame % 3 != 0 ||
                size.Width <= 0 || size.Height <= 0 || (long)size.Width * size.Height * 4 > 8 * 1024 * 1024)
                throw new ArgumentOutOfRangeException("pose");
            lock (_gate) {
                if (_disposed) throw new ObjectDisposedException(nameof(DancerPosePreparer));
                if (_failure != null) throw new InvalidOperationException("Dancer preparation failed.", _failure);
                if (_sizes[character] != size) {
                    _sizes[character] = size; _generation[character]++;
                    for (int i = character * 4; i < character * 4 + 4; i++) {
                        _ready[i]?.Dispose(); _ready[i] = null; _produced[i] = false;
                    }
                }
                _priority[character] = frame / 3;
                if (!_running && HasWork()) {
                    _running = true;
                    try {
                        Task.Factory.StartNew(Prepare, System.Threading.CancellationToken.None,
                            TaskCreationOptions.DenyChildAttach, Scheduler);
                    } catch { _running = false; throw; }
                }
            }
        }

        private bool HasWork()
        {
            for (int i = 0; i < 8; i++) if (!_sizes[i / 4].IsEmpty && !_produced[i]) return true;
            return false;
        }

        internal Bitmap Take(int slot)
        {
            lock (_gate) {
                if (_failure != null) throw new InvalidOperationException("Dancer preparation failed.", _failure);
                var pose = _ready[slot]; _ready[slot] = null; return pose;
            }
        }

        private void Prepare()
        {
            try {
                while (true) {
                    int character = -1, slot = -1, generation = 0;
                    Size size = Size.Empty;
                    lock (_gate) {
                        if (_disposed || !HasWork()) {
                            _running = false;
                            if (_disposed) Release();
                            else if (ReleaseSourcesWhenIdle) ReleaseSources();
                            return;
                        }
                        for (int attempt = 0; attempt < 2 && slot < 0; attempt++) {
                            character = (_nextCharacter + attempt) % 2;
                            if (_sizes[character].IsEmpty) continue;
                            for (int j = 0; j < 4; j++) {
                                int candidate = character * 4 + (_priority[character] + j) % 4;
                                if (!_produced[candidate]) { slot = candidate; break; }
                            }
                        }
                        _nextCharacter = 1 - character;
                        size = _sizes[character]; generation = _generation[character];
                        _produced[slot] = true;
                    }
                    if (_sources[character] == null) {
                        using (var stream = typeof(DancerPosePreparer).Assembly.GetManifestResourceStream(
                            character == 0 ? "MusicBeePlugin.PartyRem.png" : "MusicBeePlugin.PartyRam.png"))
                        using (var image = Image.FromStream(stream))
                        using (var decoded = new Bitmap(image))
                            _sources[character] = decoded.Clone(new Rectangle(Point.Empty, decoded.Size), PixelFormat.Format32bppPArgb);
                    }
                    var pose = PartyDancerWindow.CreatePose(_sources[character], size, slot % 4 * 3);
                    lock (_gate) {
                        if (_disposed || generation != _generation[character]) pose.Dispose();
                        else _ready[slot] = pose;
                    }
                }
            } catch (Exception error) {
                lock (_gate) { _failure = error; _running = false; Release(); }
            }
        }

        private void Release()
        {
            for (int i = 0; i < 8; i++) { _ready[i]?.Dispose(); _ready[i] = null; }
            ReleaseSources();
        }

        private void ReleaseSources()
        {
            for (int i = 0; i < 2; i++) { _sources[i]?.Dispose(); _sources[i] = null; }
        }

        // Do not join a worker on the UI thread. It drops an in-flight result and
        // releases its sources before any subsequent preparer can start decoding.
        public void Dispose()
        {
            lock (_gate) { _disposed = true; if (!_running) Release(); }
        }
    }
}
