using System;

namespace MusicBeePlugin
{
    internal static class Program
    {
        private static void Main()
        {
            const string lrc = "[00:09.68] First line\n" +
                               "[00:17.30] \n" +
                               "[00:17.30] Second line\n" +
                               "[00:24.68] \n" +
                               "[00:31.38] Third line\n" +
                               "[00:40.00] Original\n" +
                               "[00:40.00] Translation";

            var lyrics = LyricParser.ParseLyric(lrc);
            if (lyrics == null || lyrics.Entries.Count != 4)
                throw new Exception("Empty timestamps must not become timed lyrics.");
            if (lyrics.Entries[1].LyricLine1 != " Second line" || lyrics.Entries[1].LyricLine2 != null)
                throw new Exception("An empty duplicate timestamp must not become a translation.");
            if (lyrics.Entries[2].LyricLine1 != " Third line")
                throw new Exception("An empty pause must not replace the active lyric.");
            if (lyrics.Entries[3].LyricLine2 != " Translation")
                throw new Exception("A real translation must remain on the second line.");
            Console.WriteLine("LRC parser checks passed.");
        }
    }
}
