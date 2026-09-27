namespace MusicBeePlugin
{
    internal static class QueueNavigation
    {
        public static bool TryPlayQueuedTrack(Plugin.MusicBeeApiInterface musicBee,
            UpcomingQueue.Track track, out string error)
        {
            error = null;
            if (track == null || string.IsNullOrWhiteSpace(track.FileUrl) ||
                musicBee.NowPlayingList_GetNextIndex == null ||
                musicBee.NowPlayingList_GetListFileUrl == null ||
                musicBee.NowPlayingList_PlayNow == null)
            {
                error = "This song cannot be played from the queue.";
                return false;
            }
            try
            {
                if (musicBee.NowPlayingList_GetNextIndex(track.Offset) != track.Index ||
                    musicBee.NowPlayingList_GetListFileUrl(track.Index) != track.FileUrl)
                {
                    error = "The queue changed. Try that song again.";
                    return false;
                }
                // PlayNow accepts a file URL, not a queue index. It selects
                // the first copy if the same file occurs more than once.
                if (musicBee.NowPlaying_GetFileUrl?.Invoke() == track.FileUrl)
                {
                    error = "MusicBee cannot jump to a second copy of this song.";
                    return false;
                }
                for (var offset = 1; offset < track.Offset; offset++)
                {
                    var earlier = musicBee.NowPlayingList_GetNextIndex(offset);
                    if (earlier < 0) break;
                    if (musicBee.NowPlayingList_GetListFileUrl(earlier) == track.FileUrl)
                    {
                        error = "MusicBee cannot choose between duplicate songs in the queue.";
                        return false;
                    }
                }
                if (musicBee.NowPlayingList_PlayNow(track.FileUrl)) return true;
                error = "MusicBee could not start that song.";
            }
            catch (System.Exception)
            {
                error = "The queue changed while opening that song.";
            }
            return false;
        }

        public static bool TryPlayNext(Plugin.MusicBeeApiInterface musicBee)
        {
            // Avoid sending Next when MusicBee has no following track.
            if (musicBee.Player_GetButtonEnabled != null &&
                !musicBee.Player_GetButtonEnabled(Plugin.PlayButtonType.NextTrack))
                return false;

            if (musicBee.NowPlayingList_IsAnyFollowingTracks != null &&
                !musicBee.NowPlayingList_IsAnyFollowingTracks())
            {
                var repeat = musicBee.Player_GetRepeat != null &&
                    musicBee.Player_GetRepeat() != Plugin.RepeatMode.None;
                var autoDj = musicBee.Player_GetAutoDjEnabled != null &&
                    musicBee.Player_GetAutoDjEnabled();
                if (!repeat && !autoDj) return false;
            }

            return musicBee.Player_PlayNextTrack != null &&
                musicBee.Player_PlayNextTrack();
        }
    }
}
