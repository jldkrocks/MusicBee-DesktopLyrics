using System;

namespace MusicBeePlugin
{
    public class LyricsController
    {
        // Keep the legacy setting name for compatibility with saved settings.
        // A preview is now shown even when the active lyric has a translation.
        public bool NextLineWhenNoTranslation { get; set; }
        public bool ShowTranslation { get; set; } = true;

        private readonly Plugin.MusicBeeApiInterface _interface;
        private readonly EnglishTranslationStore _englishStore;
        private string _lastLyrics;
        private LyricParser.Lyrics _lyrics;
        private string _editedTrackUrl, _editedLyrics;
        private bool _editedLyricsSaved;
        private bool _savedTagSeen;
        private string _tagTrackUrl, _taggedLyrics;
        private DateTime _nextTagCheckUtc;
        private string _englishTrackUrl;
        private LyricParser.Lyrics _englishLyrics;
        private string[] _englishLines;

        public LyricsController(Plugin.MusicBeeApiInterface @interface)
            : this(@interface, null)
        {
        }

        internal LyricsController(Plugin.MusicBeeApiInterface @interface,
            EnglishTranslationStore englishStore)
        {
            _interface = @interface;
            _englishStore = englishStore;
        }

        public void InvalidateImportedEnglish()
        {
            _englishTrackUrl = null;
            _englishLyrics = null;
            _englishLines = null;
        }

        public void PreviewLyrics(string trackUrl, string lyrics)
        {
            _editedTrackUrl = trackUrl;
            _editedLyrics = lyrics;
            _editedLyricsSaved = false;
            _savedTagSeen = false;
        }

        public void KeepSavedLyrics(string trackUrl, string lyrics)
        {
            _editedTrackUrl = trackUrl;
            _editedLyrics = lyrics;
            _editedLyricsSaved = true;
            _savedTagSeen = false;
            InvalidateTag();
        }

        public void InvalidateTag()
        {
            _nextTagCheckUtc = DateTime.MinValue;
        }

        public void CancelPreview(string trackUrl)
        {
            if (_editedTrackUrl != trackUrl || _editedLyricsSaved) return;
            _editedTrackUrl = _editedLyrics = null;
            _lastLyrics = null;
            _lyrics = null;
            InvalidateTag();
        }

        public LyricView UpdateLyrics(bool useGeneratedWhenUnavailable)
        {
            // TODO passively change?
            var currentUrl = _interface.NowPlaying_GetFileUrl?.Invoke();
            if (_editedTrackUrl != null &&
                currentUrl != _editedTrackUrl)
            {
                _editedTrackUrl = _editedLyrics = null;
                _editedLyricsSaved = false;
                _savedTagSeen = false;
                _lastLyrics = null;
                _lyrics = null;
            }
            if (_tagTrackUrl != currentUrl)
            {
                _tagTrackUrl = currentUrl;
                _taggedLyrics = null;
                InvalidateTag();
            }
            if (!string.IsNullOrWhiteSpace(currentUrl) &&
                DateTime.UtcNow >= _nextTagCheckUtc)
            {
                _nextTagCheckUtc = DateTime.UtcNow.AddMilliseconds(400);
                try
                {
                    _taggedLyrics = _interface.Library_GetFileTag?.Invoke(currentUrl,
                        Plugin.MetaDataType.Lyrics);
                }
                catch (Exception) { /* Streams may have no writable library tag. */ }
            }
            var taggedLyrics = _taggedLyrics;
            // MusicBee may keep NowPlaying_GetLyrics cached until another song
            // starts. A new tag saved in its editor must take precedence.
            if (_editedLyricsSaved && _editedTrackUrl == currentUrl)
            {
                if (taggedLyrics == _editedLyrics) _savedTagSeen = true;
                else if (_savedTagSeen && taggedLyrics != null)
                {
                    _editedTrackUrl = _editedLyrics = null;
                    _editedLyricsSaved = false;
                    _savedTagSeen = false;
                }
            }
            var hasLyrics = _interface.NowPlaying_GetFileTag(Plugin.MetaDataType.HasLyrics) ?? "";
            if (_editedTrackUrl != null || !string.IsNullOrWhiteSpace(taggedLyrics) ||
                hasLyrics.StartsWith("Y") || hasLyrics.Length == 0)
            {
                var lyrics = _editedTrackUrl != null ? _editedLyrics :
                    !string.IsNullOrWhiteSpace(taggedLyrics) ? taggedLyrics :
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
            if (_englishTrackUrl != currentUrl || !ReferenceEquals(_englishLyrics, _lyrics))
            {
                _englishTrackUrl = currentUrl;
                _englishLyrics = _lyrics;
                _englishLines = _englishStore?.Load(currentUrl, _lyrics.Entries);
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
            var english = _englishLines != null && currentIndex < _englishLines.Length
                ? _englishLines[currentIndex] : null;
            var translation = !string.IsNullOrWhiteSpace(english)
                ? english : currentEntry.LyricLine2;
            return new LyricView(currentEntry.LyricLine1,
                ShowTranslation ? translation : null, nextLine);
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
