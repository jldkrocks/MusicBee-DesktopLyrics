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
            public string FileUrl;
            public int Index;
            public int Offset;
        }

        // The local history is only useful while those files are still in
        // MusicBee's Playing Tracks list. A successful empty query means the
        // list was cleared; null means the snapshot failed and must not erase
        // history on a transient API error.
        public static HashSet<string> ReadPlayingListUrls(Plugin.MusicBeeApiInterface musicBee)
        {
            if (musicBee.NowPlayingList_QueryFilesEx == null) return null;
            try
            {
                string[] urls;
                if (!musicBee.NowPlayingList_QueryFilesEx(null, out urls) || urls == null)
                    return null;
                return new HashSet<string>(urls, StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception) { return null; }
        }

        public static List<Track> Read(Plugin.MusicBeeApiInterface musicBee,
            int firstOffset = 1, int limit = 12)
        {
            var tracks = new List<Track>();
            if (musicBee.NowPlayingList_GetNextIndex == null ||
                musicBee.NowPlayingList_GetListFileUrl == null) return tracks;

            try
            {
                var current = musicBee.NowPlayingList_GetCurrentIndex == null ? -1 :
                    musicBee.NowPlayingList_GetCurrentIndex();
                var seen = new HashSet<int>();
                for (var offset = firstOffset; offset < firstOffset + limit; offset++)
                {
                    var index = musicBee.NowPlayingList_GetNextIndex(offset);
                    if (index < 0 || index == current || !seen.Add(index)) break;
                    var url = musicBee.NowPlayingList_GetListFileUrl(index);
                    if (string.IsNullOrWhiteSpace(url)) break;
                    var title = musicBee.NowPlayingList_GetFileTag?.Invoke(index,
                        Plugin.MetaDataType.TrackTitle);
                    var artist = musicBee.NowPlayingList_GetFileTag?.Invoke(index,
                        Plugin.MetaDataType.Artist);
                    if (string.IsNullOrWhiteSpace(title))
                        title = Path.GetFileNameWithoutExtension(url);
                    tracks.Add(new Track { Title = title.Trim(), Artist = artist?.Trim() ?? "",
                        FileUrl = url, Index = index, Offset = offset });
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
