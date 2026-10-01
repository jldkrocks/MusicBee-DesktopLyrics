using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using MusicBeePlugin;
using Newtonsoft.Json;
internal static class Editor47Checks
{
 static object Field(object x,string n)=>x.GetType().GetField(n,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(x);
 static object Call(object x,string n,params object[] args)=>x.GetType().GetMethod(n,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(x,args);
 static void Check(bool ok,string m){if(!ok)throw new Exception(m);}
 internal static void Run(){
  using(var entered=new ManualResetEventSlim())using(var release=new ManualResetEventSlim()){
   int actual=1234;var api=new Plugin.MusicBeeApiInterface{NowPlaying_GetFileUrl=()=>"seek",Player_GetPlayState=()=>Plugin.PlayState.Playing,
    Player_GetPosition=()=>{int old=actual;entered.Set();release.Wait(3000);return old;}};
   using(var reader=new PlaybackSnapshotReader(api)){
    reader.Request(false);Check(entered.Wait(2000),"Seek race did not start.");
    reader.PublishSeek("seek",10000);reader.PublishSeek("seek",20000);actual=20000;
    Check(reader.Latest.Position==20000,"Accepted seek must publish immediately.");
    release.Set();Check(SpinWait.SpinUntil(()=>(int)Field(reader,"_busy")==0,2000),"Old read did not finish.");
    Check(reader.Latest.Position==20000,"Pre-seek read must never replace newest seek.");
    actual=20100;reader.Request(false);Check(SpinWait.SpinUntil(()=>reader.Latest.Position==20100,2000),"Fresh post-seek snapshot must publish.");
   }
  }
  var clock=new PartyPlaybackClock();clock.PositionAt(1000,0,1000,false,true);clock.PositionAt(1000,100,1000,true,true);
  clock.Seek(103675,120,1000,true);Check(clock.PositionAt(103675,120,1000,true,true)==103675,"Explicit seek must bypass resume easing.");
  clock.Seek(500,125,1000,false);Check(clock.PositionAt(500,125,1000,false,true)==500,"Backward paused seek must be exact.");
  var m=new PartyTempoMap{TrackUrl="return",Version=6,InitialBeat=-2.9933333333333332};
  m.Sections.Add(new PartyTempoSection{Bpm=100,Style=PartyDanceStyle.Rest});
  m.Sections.Add(new PartyTempoSection{StartSeconds=15.555,Bpm=100,Rhythm=PartyRhythm.Swing,SwingPercent=66,AlignBeat=true});
  foreach(double t in new[]{98.783,100.311,101.3,103.675})m.Sections.Add(new PartyTempoSection{StartSeconds=t,Bpm=100,Rhythm=PartyRhythm.Swing,SwingPercent=66,Style=t<103?PartyDanceStyle.Rest:PartyDanceStyle.Normal});
  m.Accents.Add(new PartyAccentCue{TimeSeconds=98.783,Motion=PartyAccentMotion.Right,Strength=2.5});
  m.Accents.Add(new PartyAccentCue{TimeSeconds=100.31,Motion=PartyAccentMotion.Alternate,Strength=2.5});
  m.Accents.Add(new PartyAccentCue{TimeSeconds=101.3,Motion=PartyAccentMotion.Alternate,Strength=2.5});m.Validate();
  Check(m.At(98.77).Frame==m.At(98.683).Frame,"Directional preparation must not flash a middle pose.");
  Check(m.At(103.674).Frame==0 && m.At(103.675).Frame==0 && m.At(103.73).Frame==0 && m.At(103.76).Frame==0,"Rest return must suppress the 80 ms centre blip.");
  var shortReturn=JsonConvert.DeserializeObject<PartyTempoMap>(JsonConvert.SerializeObject(m));
  shortReturn.Sections.Add(new PartyTempoSection{StartSeconds=103.72,Bpm=100,Style=PartyDanceStyle.Hold});
  Check(shortReturn.At(103.68).Frame==3,"Exit smoothing must not look past an explicit next section.");
  var plain=JsonConvert.DeserializeObject<PartyTempoMap>(JsonConvert.SerializeObject(m));plain.Accents.Clear();
  Check(Math.Abs(m.At(103.73).Beat-plain.At(103.73).Beat)<1e-9,"Exit fix must preserve beat phase.");
  foreach(var motion in new[]{PartyAccentMotion.Bop,PartyAccentMotion.Rebound})foreach(PartyAccentPose pose in Enum.GetValues(typeof(PartyAccentPose))){
   var cue=m.Accents[0];cue.Motion=motion;cue.Pose=pose;var hit=m.At(cue.TimeSeconds);
   Check(Math.Abs(hit.Impact-(motion==PartyAccentMotion.Bop?2.5:3.375))<1e-5,"Independent motion strength failed.");
   if(pose==PartyAccentPose.Left||pose==PartyAccentPose.Alternate)Check(hit.Frame==6,"Left/alternate pose failed.");
   if(pose==PartyAccentPose.Right)Check(hit.Frame==0,"Right pose failed.");
  }
  foreach(bool custom in new[]{false,true}){
   var source=new PartyTempoMap{TrackUrl="copy",Version=6};
   source.Sections.Add(new PartyTempoSection{Bpm=custom?60:120,RampStartBpm=custom?(double?)120:null,RampSeconds=custom?8:0,RampToNext=!custom});
   source.Sections.Add(new PartyTempoSection{StartSeconds=10,Bpm=60});
   source.Accents.Add(new PartyAccentCue{TimeSeconds=4,Strength=2.5,Motion=PartyAccentMotion.Alternate});PartyTempoMap saved=null;int seeks=0;
   using(var form=new FrmPartyTempoMap(source,"Copy",()=>0,_=>seeks++,x=>saved=x,20,()=>{},()=>false)){
    var sections=(DataGridView)Field(form,"_grid");var cues=(DataGridView)Field(form,"_accentGrid");
    Check(Convert.ToString(cues.Rows[0].Cells[4].Value)=="Rebound" && Convert.ToString(cues.Rows[0].Cells[5].Value)=="Alternate sides","Legacy directional cue must migrate to both independent controls.");
    cues.CurrentCell=cues.Rows[0].Cells[0];Call(form,"CopyTimeToOtherTab",cues);Check(sections.Rows.Count==3 && seeks==0,"Copying accent time must add a section without seeking.");
    Call(form,"SaveMap");Check(saved!=null&&saved.Version==8,"New map must save.");
    for(double t=0;t<12;t+=.013)Check(Math.Abs(source.At(t).Bpm-saved.At(t).Bpm)<1e-6 && Math.Abs(source.At(t).Beat-saved.At(t).Beat)<1e-6,"Inserting a section must preserve linked/custom ramp integration.");
    Call(form,"CopyTimeToOtherTab",cues);Check(sections.Rows.Count==3,"Existing section must be selected, not duplicated.");
    sections.CurrentCell=sections.Rows[1].Cells[0];Call(form,"CopyTimeToOtherTab",sections);
    Check(cues.Rows.Count==2 && seeks==0 && Convert.ToDouble(cues.Rows[1].Cells[0].Value)==10 && Convert.ToDouble(cues.Rows[1].Cells[1].Value)==2.5,"Section-to-accent copy must use exact time and selected accent settings.");
    Call(form,"CopyTimeToOtherTab",sections);Check(cues.Rows.Count==2,"Existing accent must be selected, not duplicated.");
    form.Opacity=0;form.Show();Application.DoEvents();
    foreach(int col in new[]{4,5}){
     cues.CurrentCell=cues.Rows[0].Cells[col];
     typeof(DataGridView).GetMethod("OnCellClick",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(cues,new object[]{new DataGridViewCellEventArgs(col,0)});
     Application.DoEvents();var combo=cues.EditingControl as ComboBox;Check(combo!=null && combo.DroppedDown,"Accent dropdown must open on one click.");combo.DroppedDown=false;cues.EndEdit();
    }
    cues.CurrentCell=cues.Rows[0].Cells[0];var menu=(ContextMenuStrip)Field(form,"_rowMenu");menu.Show(cues,new System.Drawing.Point(30,30));Application.DoEvents();
    Check(menu.Items[0].Text=="Add section at this time","Context action must identify destination.");menu.Items[0].PerformClick();menu.Close();
    Check(sections.Rows.Count==3 && ((TabControl)Field(form,"_tabs")).SelectedIndex==0 && seeks==0,"Context action must use source row, switch tabs and preserve playback.");form.Hide();
   }
  }
  Console.WriteLine("Seek race rejection, exact clock reset, rest/lead-in blips, independent accents and timestamp-copy curves passed.");
 }
}
