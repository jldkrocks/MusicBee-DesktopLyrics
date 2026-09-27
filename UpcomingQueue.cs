using System;
using System.Collections.Generic;
using System.IO;

namespace MusicBeePlugin
{
    // MusicBee supplies indices in actual playback order, including shuffled
    // and manually queued tracks. Read a small snapshot outside the paint loop.
    internal static class UpcomingQueue
    {
        internal sealed class Track
        {
            public string Title;
            public string Artist;
        }

        public static List<Track> Read(Plugin.MusicBeeApiInterface musicBee, int limit = 4)
        {
            var tracks = new List<Track>();
            if (musicBee.NowPlayingList_GetNextIndex == null ||
                musicBee.NowPlayingList_GetFileTag == null) return tracks;

            try
            {
                var current = musicBee.NowPlayingList_GetCurrentIndex == null ? -1 :
                    musicBee.NowPlayingList_GetCurrentIndex();
                var seen = new HashSet<int>();
                for (var offset = 1; offset <= limit; offset++)
                {
                    var index = musicBee.NowPlayingList_GetNextIndex(offset);
                    if (index < 0 || index == current || !seen.Add(index)) break;
                    var title = musicBee.NowPlayingList_GetFileTag(index,
                        Plugin.MetaDataType.TrackTitle);
                    var artist = musicBee.NowPlayingList_GetFileTag(index,
                        Plugin.MetaDataType.Artist);
                    if (string.IsNullOrWhiteSpace(title) &&
                        musicBee.NowPlayingList_GetListFileUrl != null)
                    {
                        var url = musicBee.NowPlayingList_GetListFileUrl(index);
                        if (!string.IsNullOrWhiteSpace(url))
                            title = Path.GetFileNameWithoutExtension(url);
                    }
                    if (string.IsNullOrWhiteSpace(title)) continue;
                    tracks.Add(new Track { Title = title.Trim(), Artist = artist?.Trim() ?? "" });
                }
            }
            catch (Exception)
            {
                // The list can change during a track transition. Keep any
                // entries already read and refresh at the next notification.
            }
            return tracks;
        }
    }
}
