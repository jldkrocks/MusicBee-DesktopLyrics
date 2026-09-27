namespace MusicBeePlugin
{
    internal static class QueueNavigation
    {
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
