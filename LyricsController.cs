namespace MusicBeePlugin
{
    public class LyricsController
    {
        // Keep the legacy setting name for compatibility with saved settings.
        // A preview is now shown even when the active lyric has a translation.
        public bool NextLineWhenNoTranslation { get; set; }

        private readonly Plugin.MusicBeeApiInterface _interface;
        private string _lastLyrics;
        private LyricParser.Lyrics _lyrics;
        private string _editedTrackUrl, _editedLyrics;
        private bool _editedLyricsSaved;
        public LyricsController(Plugin.MusicBeeApiInterface @interface)
        {
            _interface = @interface;
        }

        public void PreviewLyrics(string trackUrl, string lyrics)
        {
            _editedTrackUrl = trackUrl;
            _editedLyrics = lyrics;
            _editedLyricsSaved = false;
        }

        public void KeepSavedLyrics(string trackUrl, string lyrics)
        {
            _editedTrackUrl = trackUrl;
            _editedLyrics = lyrics;
            _editedLyricsSaved = true;
        }

        public void CancelPreview(string trackUrl)
        {
            if (_editedTrackUrl != trackUrl || _editedLyricsSaved) return;
            _editedTrackUrl = _editedLyrics = null;
            _lastLyrics = null;
            _lyrics = null;
        }

        public LyricView UpdateLyrics(bool useGeneratedWhenUnavailable)
        {
            // TODO passively change?
            if (_editedTrackUrl != null &&
                _interface.NowPlaying_GetFileUrl() != _editedTrackUrl)
            {
                _editedTrackUrl = _editedLyrics = null;
                _editedLyricsSaved = false;
                _lastLyrics = null;
                _lyrics = null;
            }
            var hasLyrics = _interface.NowPlaying_GetFileTag(Plugin.MetaDataType.HasLyrics) ?? "";
            if (_editedTrackUrl != null || hasLyrics.StartsWith("Y") || hasLyrics.Length == 0)
            {
                var lyrics = _editedTrackUrl != null ? _editedLyrics :
                    _interface.NowPlaying_GetLyrics();
                if (lyrics != _lastLyrics)
                {
                    _lyrics = LyricParser.ParseLyric(lyrics);
                    _lastLyrics = lyrics;
                }
            }
            else
            {
                _lyrics = null;
                _lastLyrics = null;
            }
            

            if (_lyrics == null)
            {
                if (useGeneratedWhenUnavailable)
                    return new LyricView(
                    _interface.NowPlaying_GetFileTag(Plugin.MetaDataType.TrackTitle) + " - " +
                    _interface.NowPlaying_GetFileTag(Plugin.MetaDataType.Artist), null);
                return null;
            }
                
            var time = _interface.Player_GetPosition();
            var nTime = time + _lyrics.Offset;
            var entries = _lyrics.Entries;

            if (entries.Count == 0 || nTime < entries[0].TimeMs) return null;

            var currentIndex = entries.Count - 1;
            for (var i = 1; i < entries.Count; i++)
            {
                if (entries[i].TimeMs > nTime)
                {
                    currentIndex = i - 1;
                    break;
                }
            }

            var currentEntry = entries[currentIndex];
            string nextLine = null;
            if (NextLineWhenNoTranslation)
            {
                for (var i = currentIndex + 1; i < entries.Count; i++)
                {
                    if (string.IsNullOrWhiteSpace(entries[i].LyricLine1)) continue;
                    nextLine = entries[i].LyricLine1;
                    break;
                }
            }
            return new LyricView(currentEntry.LyricLine1, currentEntry.LyricLine2, nextLine);
        }

        public class LyricView
        { 
            // C# version < 8.0, can't use nullable reference feature....
            public string LyricLine1 { get; set; } // Nullable
            public string LyricLine2 { get; set; } // Nullable
            public string NextLine { get; set; } // Nullable; preview of the next timed entry

            public LyricView(string lyricLine1, string lyricLine2, string nextLine = null)
            {
                LyricLine1 = lyricLine1;
                LyricLine2 = lyricLine2;
                NextLine = nextLine;
            }

            public override string ToString()
            {
                return $"[LyricView: {LyricLine1}, {LyricLine2}, {NextLine}]";
            }
        }
    }
}
