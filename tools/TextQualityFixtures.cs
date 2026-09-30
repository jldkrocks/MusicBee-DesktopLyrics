using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Text;
using System.Globalization;
using System.Diagnostics;
using System.Windows.Forms;
using MusicBeePlugin;
class TextQualityFixtures {
 const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
 static object Get(object o,string n){return o.GetType().GetField(n,F).GetValue(o);}
 [STAThread] static void Main(string[] args){
  var folder=Path.GetFullPath(args.Length>0?args[0]:"text-quality");Directory.CreateDirectory(folder);
  var assembly=typeof(Plugin).Assembly;var type=assembly.GetType("MusicBeePlugin.FrmLyricsWindow");
  var settings=SettingsObj.GenerateDefault();settings.PartyMode=false;settings.DisableDeezerBpmLookup=true;
  settings.Color1=Color.White;settings.Color2=Color.White;settings.BorderColor=Color.Black;
  var ctor=type.GetConstructors()[0];var pars=ctor.GetParameters();var values=new object[pars.Length];
  values[0]=settings;values[1]=new Plugin.MusicBeeApiInterface {Player_GetPosition=()=>0,Player_GetPlayState=()=>Plugin.PlayState.Paused,NowPlaying_GetFileUrl=()=>null};
  values[2]=Activator.CreateInstance(pars[2].ParameterType,true);
  values[10]=Activator.CreateInstance(pars[10].ParameterType,F,null,new object[]{folder},null);
  string[] texts={"Current lyrics stay readable while the music and dancers move", "An English translation with enough words to test wrapping above the main lyrics", "光の中で踊り続ける　次の歌が聞こえる", "Café, déjà vu: a\u0301 e\u0301 and flowing ffi letters", "مرحبا بالعالم والموسيقى"};
  using(var form=(Form)ctor.Invoke(values)) {
   var log=new StringBuilder("id,gdi_width,gdi_height,gdi_prepare_ms,lines,font,points,scale\n");
   for(int k=0;k<texts.Length;k++)foreach(int width in new[]{280,1400})foreach(float scale in new[]{1f,1.12f}) {
    int height=width==280?100:340;float points=width==280?22:68;
    string id=(width==280?"small":"4k")+"-"+k+"-"+(scale==1?"native":"scaled");
    using(var image=new Bitmap(width,height,PixelFormat.Format32bppPArgb))using(var g=Graphics.FromImage(image)) {
     g.Clear(Color.FromArgb(22,29,46));var area=new RectangleF(0,0,width,height);
     var timer=Stopwatch.StartNew();
     var geo=type.GetMethod("GetTextGeometry",F).Invoke(form,new object[]{g,texts[k],area,points});
     type.GetMethod("DrawLine",F).Invoke(form,new object[]{g,texts[k],area,points,255,null,scale});timer.Stop();
     image.Save(Path.Combine(folder,id+"-gdi.png"));
     var font=settings.Font??SystemFonts.DefaultFont;
     var fit=assembly.GetType("MusicBeePlugin.LyricTextLayout").GetMethod("Fit",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{g,texts[k],font,points,(float)width,(float)height});
     var bounds=(RectangleF)Get(geo,"Bounds");float fitted=(float)Get(geo,"FittedPoints");string wrapped=(string)Get(fit,"Text");
     var c=CultureInfo.InvariantCulture;
     string header=width+"\n"+height+"\n"+fitted.ToString(c)+"\n"+((int)font.Style)+"\n"+font.FontFamily.Name+"\n"+scale.ToString(c)+"\n";
     File.WriteAllText(Path.Combine(folder,id+".fixture"),header+wrapped,Encoding.Unicode);
     log.AppendFormat(c,"{0},{1:F3},{2:F3},{3:F3},{4},{5},{6:F3},{7}\n",id,bounds.Width,bounds.Height,timer.Elapsed.TotalMilliseconds,wrapped.Split('\n').Length,font.FontFamily.Name,fitted,scale);
    }
   }
   File.WriteAllText(Path.Combine(folder,"gdi-metrics.csv"),log.ToString());
  }
 }
}
