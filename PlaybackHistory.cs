using System;
using System.Collections.Generic;
using System.IO;

namespace MusicBeePlugin
{
    // MusicBee exposes the upcoming order, but no play-history list through
    // the plugin API. Keep the last tracks observed by this plugin instance.
    internal sealed class PlaybackHistory
    {
        private readonly List<UpcomingQueue.Track> _played = new List<UpcomingQueue.Track>();
        private UpcomingQueue.Track _current;

        public bool Observe(string fileUrl, string title, string artist)
        {
            if (string.IsNullOrWhiteSpace(fileUrl)) return false;
            lock (_played)
            {
                if (_current != null && string.Equals(_current.FileUrl, fileUrl,
                    StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrWhiteSpace(title)) _current.Title = title.Trim();
                    if (!string.IsNullOrWhiteSpace(artist)) _current.Artist = artist.Trim();
                    return false;
                }
                if (_current != null)
                {
                    // Replaying a song moves its existing history entry to
                    // the current position instead of listing it twice.
                    _played.RemoveAll(track => string.Equals(track.FileUrl, fileUrl,
                        StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(track.FileUrl, _current.FileUrl,
                            StringComparison.OrdinalIgnoreCase));
                    _played.Add(_current);
                    if (_played.Count > 80) _played.RemoveAt(0);
                }
                _current = new UpcomingQueue.Track
                {
                    Title = string.IsNullOrWhiteSpace(title) ?
                        Path.GetFileNameWithoutExtension(fileUrl) : title.Trim(),
                    Artist = artist?.Trim() ?? "", FileUrl = fileUrl, Index = -1,
                    Offset = 0
                };
                return true;
            }
        }

        public List<UpcomingQueue.Track> Snapshot()
        {
            lock (_played)
            {
                var result = new List<UpcomingQueue.Track>(_played.Count + 1);
                for (var i = 0; i < _played.Count; i++)
                    result.Add(new UpcomingQueue.Track
                    {
                        Title = _played[i].Title, Artist = _played[i].Artist,
                        FileUrl = _played[i].FileUrl, Index = -1,
                        Offset = i - _played.Count
                    });
                if (_current != null)
                    result.Add(new UpcomingQueue.Track
                    {
                        Title = _current.Title, Artist = _current.Artist,
                        FileUrl = _current.FileUrl, Index = -1, Offset = 0
                    });
                return result;
            }
        }

        public List<UpcomingQueue.Track> Timeline(IList<UpcomingQueue.Track> future)
        {
            var timeline = Snapshot();
            var upcomingUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var track in future) upcomingUrls.Add(track.FileUrl);
            // A track can be both previously played and upcoming after a jump.
            // Keep its upcoming position, and keep every file visible once.
            timeline.RemoveAll(track => track.Offset < 0 &&
                upcomingUrls.Contains(track.FileUrl));
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var track in timeline) seen.Add(track.FileUrl);
            foreach (var track in future)
                if (seen.Add(track.FileUrl)) timeline.Add(track);
            return timeline;
        }
    }
}
