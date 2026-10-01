using System.Windows.Forms;

namespace MusicBeePlugin
{
    public interface IDesktopLyricsView
    {
        Form Form { get; }
        void UpdateFromSettings(SettingsObj settings);
        void UpdateLyrics(string line1, string line2, string nextLine);
        void Clear();
    }
}
