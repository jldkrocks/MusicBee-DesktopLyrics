using System;
using System.Drawing;
using System.Windows.Forms;
using Newtonsoft.Json;
namespace MusicBeePlugin
{
    public partial class FrmSettings : Form
    {
        private readonly SettingsObj _settings;
        public event EventHandler<SettingsObj> SettingsChanged;
        public event EventHandler ShowWindowRequested;
        private void Changed(){SettingsChanged?.Invoke(this,_settings);}
        public FrmSettings(SettingsObj settings){
            _settings=settings;
            Text="KoreKara settings (v"+GetType().Assembly.GetName().Version+")";
            Font=SystemFonts.MessageBoxFont;BackColor=Color.FromArgb(23,27,38);ForeColor=Color.FromArgb(232,236,245);
            ClientSize=new Size(620,630);MinimumSize=new Size(570,550);StartPosition=FormStartPosition.CenterParent;
            var tabs=new SettingsTabs {Dock=DockStyle.Fill};
            Controls.Add(tabs);
            var display=Page(tabs,"Display");var lyrics=Page(tabs,"Lyrics");var performance=Page(tabs,"Performance");
            Check(display,"Show song title",settings.ShowSongTitle,v=>settings.ShowSongTitle=v);
            Check(display,"Show album artwork",settings.ShowAlbumArt,v=>settings.ShowAlbumArt=v);
            Check(display,"Show playback controls",settings.ShowTransportControls,v=>settings.ShowTransportControls=v);
            Check(display,"Show visualizer",settings.ShowVisualizer,v=>settings.ShowVisualizer=v);
            Check(display,"Show queue and history",settings.ShowSongQueue,v=>settings.ShowSongQueue=v);
            Check(display,"Match album artwork colours",settings.UseArtworkColors,v=>{settings.UseArtworkColors=v;settings.ArtworkColorsPreferenceSet=true;});
            Check(display,"Transparent canvas (BG)",settings.TransparentCanvas,v=>settings.TransparentCanvas=v);
            Check(display,"Hide lyrics when stopped",settings.AutoHide,v=>settings.AutoHide=v);
            Check(display,"Hide window when no lyrics are available",settings.HideWhenUnavailable,v=>settings.HideWhenUnavailable=v);
            Button(display,"Show lyrics window",()=>ShowWindowRequested?.Invoke(this,EventArgs.Empty));
            Check(lyrics,"Show English / translation",settings.ShowTranslation,v=>settings.ShowTranslation=v);
            Check(lyrics,"Preview next lyric",settings.NextLineWhenNoTranslation,v=>settings.NextLineWhenNoTranslation=v);
            Check(lyrics,"Leave / characters as written",settings.PreserveSlash,v=>settings.PreserveSlash=v);
            Button(lyrics,"Choose lyric font...",()=>{
                using(var dialog=new FontDialog {Font=settings.Font??SystemFonts.DefaultFont})
                    if(dialog.ShowDialog(this)==DialogResult.OK){settings.Font=(Font)dialog.Font.Clone();Changed();}
            });
            ColorButton(lyrics,"Text colour",()=>settings.Color1,c=>settings.Color1=c);
            ColorButton(lyrics,"Second gradient colour",()=>settings.Color2,c=>settings.Color2=c);
            ColorButton(lyrics,"Outline colour",()=>settings.BorderColor,c=>settings.BorderColor=c);
            Label(lyrics,"Text gradient");
            var gradient=new ComboBox {DropDownStyle=ComboBoxStyle.DropDownList,Width=250,BackColor=BackColor,ForeColor=ForeColor};
            gradient.Items.AddRange(new object[]{"No gradient","Two colours","Three colours"});
            gradient.SelectedIndex=Math.Max(0,Math.Min(2,settings.GradientType));gradient.SelectedIndexChanged+=(a,b)=>{settings.GradientType=gradient.SelectedIndex;Changed();};lyrics.Controls.Add(gradient);
            Check(performance,"GPU rendering",!settings.DisableGpuRendering,v=>settings.DisableGpuRendering=!v);
            Check(performance,"Sharper lyric outlines",!settings.UseBitmapLyrics,v=>settings.UseBitmapLyrics=!v);
            Label(performance,"Animation target");
            var fps=new ComboBox {DropDownStyle=ComboBoxStyle.DropDownList,Width=250,BackColor=BackColor,ForeColor=ForeColor};
            fps.Items.AddRange(new object[]{"60 FPS","120 FPS"});fps.SelectedIndex=settings.RenderTargetFps==60?0:1;
            fps.SelectedIndexChanged+=(a,b)=>{settings.RenderTargetFps=fps.SelectedIndex==0?60:120;Changed();};performance.Controls.Add(fps);
            Label(performance,"Targets are saved. Actual frame rate depends on display and drawing cost. Transparent BG mode uses CPU drawing with the selected pacing; unsupported systems retain the compatibility timer.");
            Label(performance,"Turn off GPU rendering for troubleshooting. Sharper outlines apply to the GPU lyric renderer.");
            var footer=new Panel {Dock=DockStyle.Bottom,Height=48,Padding=new Padding(8)};
            var close=new Button {Text="Close",Dock=DockStyle.Right,Width=90,FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(43,53,73),ForeColor=ForeColor};
            close.Click+=(sender,args)=>Close();footer.Controls.Add(close);Controls.Add(footer);tabs.BringToFront();
        }
        private FlowLayoutPanel Page(TabControl tabs,string name){
            var page=new TabPage(name){BackColor=BackColor,ForeColor=ForeColor,Padding=new Padding(18)};
            var flow=new FlowLayoutPanel {Dock=DockStyle.Fill,AutoScroll=true,FlowDirection=FlowDirection.TopDown,WrapContents=false};
            page.Controls.Add(flow);tabs.TabPages.Add(page);return flow;
        }
        private void Check(FlowLayoutPanel panel,string text,bool value,Action<bool> set){
            var c=new CheckBox {Text=text,Checked=value,AutoSize=true,Margin=new Padding(4,10,4,8)};
            c.CheckedChanged+=(a,b)=>{set(c.Checked);Changed();};panel.Controls.Add(c);
        }
        private void Label(FlowLayoutPanel panel,string text){panel.Controls.Add(new Label {Text=text,AutoSize=true,MaximumSize=new Size(480,0),Margin=new Padding(4,16,4,8)});}
        private void Button(FlowLayoutPanel panel,string text,Action action){
            var b=new Button {Text=text,AutoSize=true,Height=30,FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(43,53,73),ForeColor=ForeColor,Margin=new Padding(4,8,4,6)};
            b.Click+=(a,c)=>action();panel.Controls.Add(b);
        }
        private void ColorButton(FlowLayoutPanel panel,string text,Func<Color> get,Action<Color> set){
            Button(panel,text+"...",()=>{using(var d=new ColorDialog {Color=get(),FullOpen=true})if(d.ShowDialog(this)==DialogResult.OK){set(d.Color);Changed();}});
        }
        private sealed class SettingsTabs:TabControl{
            internal SettingsTabs(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);SizeMode=TabSizeMode.Fixed;ItemSize=new Size(170,36);}
            protected override void OnPaint(PaintEventArgs e){
                e.Graphics.Clear(Color.FromArgb(23,27,38));
                for(int i=0;i<TabCount;i++){var r=GetTabRect(i);using(var b=new SolidBrush(i==SelectedIndex?Color.FromArgb(60,87,118):Color.FromArgb(30,35,48)))e.Graphics.FillRectangle(b,r);
                    TextRenderer.DrawText(e.Graphics,TabPages[i].Text,Font,r,Color.FromArgb(232,236,245),TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);}
            }
            protected override void OnSelectedIndexChanged(EventArgs e){base.OnSelectedIndexChanged(e);Invalidate();}
        }
    }

    public class SettingsObj
    {
        public int RenderTargetFps = 120;
        public bool DisableGpuRendering;
        public bool UseBitmapLyrics;
        public Color Color1;
        public Color Color2;
        public Color BorderColor;
        public int GradientType;
        public int AlignmentType;
        public int BackgroundOpacity;
        public bool PreserveSlash;
        public bool AutoHide;
        public bool NextLineWhenNoTranslation;
        public bool HideOnStartup;
        public bool WindowCloseRecoveryApplied;
        public bool HideWhenUnavailable;
        public bool CompactWindow;
        public bool CompactWindowPreferenceSet;
        public bool UseArtworkColors;
        public bool ArtworkColorsPreferenceSet;
        public bool ShowTransportControls;
        public bool ShowSongTitle;
        public bool ShowAlbumArt;
        public bool ShowVisualizer;
        public bool TransparentCanvas;
        public bool PartyMode;
        public bool DisableDeezerBpmLookup;
        public bool ShowSongQueue;
        public bool SongQueuePreferenceSet;
        public bool ShowTranslation;
        public bool TranslationPreferenceSet;
        public bool WindowFeaturesPreferenceSet;
        public int WindowPosX = -1;
        public int WindowPosY = -1;
        public int WindowWidth = 760;
        public int WindowHeight = 230;
        public int PosY = -1;
        public int PosX = -1;
        [JsonConverter(typeof(FontConverter))]
        public Font Font;

        public static SettingsObj GenerateDefault()
        {
            var font = new Font(FontFamily.GenericSansSerif, 34.0f, FontStyle.Regular, GraphicsUnit.Point);
            return new SettingsObj
            {
                BorderColor = Color.Black,
                Color1 = Color.GhostWhite,
                Color2 = Color.LightGray,
                Font = font,
                GradientType = 1,
                AlignmentType = 0,
                BackgroundOpacity = 40,
                NextLineWhenNoTranslation = true,
                CompactWindow = true,
                CompactWindowPreferenceSet = true,
                UseArtworkColors = true,
                ArtworkColorsPreferenceSet = true,
                WindowCloseRecoveryApplied = true,
                ShowTransportControls = true,
                ShowSongTitle = true,
                ShowAlbumArt = true,
                ShowVisualizer = true,
                ShowSongQueue = true,
                SongQueuePreferenceSet = true,
                ShowTranslation = true,
                TranslationPreferenceSet = true,
                WindowFeaturesPreferenceSet = true,
            };
        }
    }

    // This corresponds to items of comboBoxGradientType!
    public enum GradientType
    {
        NoGradient = 0,
        DoubleColor = 1,
        TripleColor = 2
    }

    // This corresponds to items of comboBoxAlignment!
    public enum AlignmentType
    {
        Center = 0,
        Left = 1,
        Right = 2
    }
}
