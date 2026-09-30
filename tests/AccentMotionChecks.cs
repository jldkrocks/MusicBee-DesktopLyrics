using System;
using MusicBeePlugin;
using Newtonsoft.Json;
internal static class AccentMotionChecks
{
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 internal static void Run(){
  var m=new PartyTempoMap{Version=6,TrackUrl="motions"};
  m.Sections.Add(new PartyTempoSection{Bpm=100,Style=PartyDanceStyle.Rest});
  m.Sections.Add(new PartyTempoSection{StartSeconds=2.5,Bpm=100,Style=PartyDanceStyle.Rest});
  m.Sections.Add(new PartyTempoSection{StartSeconds=5,Bpm=100});
  m.Accents.Add(new PartyAccentCue{TimeSeconds=1,Strength=2.5,Motion=PartyAccentMotion.Alternate});
  m.Accents.Add(new PartyAccentCue{TimeSeconds=2,Motion=PartyAccentMotion.Bop});
  m.Accents.Add(new PartyAccentCue{TimeSeconds=3,Strength=2.5,Motion=PartyAccentMotion.Alternate,HoldSeconds=.2});m.Validate();
  Check(m.At(1).Frame==6 && m.At(3).Frame==0,"Alternation must ignore ordinary bops.");
  Check(m.At(3.1).Frame==0,"Post-hit hold must preserve chosen side.");
  Check(m.At(1.8).Frame==6 && m.At(2.6).Frame==6 && m.At(4.8).Frame==0,"Side landing persists during Rest.");
  var clean=JsonConvert.DeserializeObject<PartyTempoMap>(JsonConvert.SerializeObject(m));clean.Accents.Clear();
  for(double t=0;t<6;t+=.007){
   var p=m.At(t);var q=clean.At(t);
   Check(Math.Abs(p.Beat-q.Beat)<1e-10 && p.Bpm==q.Bpm,"Cues must not change tempo or phase.");
   Check(p.Impact>=0 && p.Impact<4,"Rebound must stay within safe drawing extent.");
   if(t>=5)Check(p.Frame==q.Frame,"Normal section restores rhythmic choreography.");
  }
  Check(m.At(1).Impact>3.3 && m.At(1.25).Impact>.1,"Rebound needs a stronger landing and a recovery bounce.");
  Check(m.At(1).Frame==6 && m.At(3).Frame==0 && m.At(1).Frame==6,"Seeking must not mutate alternation.");
  var loaded=JsonConvert.DeserializeObject<PartyTempoMap>(JsonConvert.SerializeObject(m));loaded.Validate();Check(loaded.At(3.1).Frame==0,"Motion roundtrip failed.");
  var old=JsonConvert.DeserializeObject<PartyAccentCue>("{\"TimeSeconds\":1,\"Strength\":2.5}");Check(old.Motion==PartyAccentMotion.Bop,"Legacy cues must keep original motion.");
  foreach(var motion in new[]{PartyAccentMotion.Left,PartyAccentMotion.Right,PartyAccentMotion.Rebound}){
   m.Accents[0].Motion=motion;Check(m.At(1).Frame==(motion==PartyAccentMotion.Left?6:motion==PartyAccentMotion.Right?0:clean.At(1).Frame),"Explicit accent pose failed.");
  }
  m.Accents[0].Motion=(PartyAccentMotion)99;bool rejected=false;try{m.Validate();}catch(ArgumentException){rejected=true;}Check(rejected,"Unknown motion accepted.");
  var clock=new PartyPlaybackClock();clock.PositionAt(10000,0,1000,false,true);
  clock.PositionAt(10000,100,1000,true,true);
  var before=clock.PositionAt(10000,116,1000,true,true);
  var after=clock.PositionAt(10500,132,1000,true,true);
  Check(after-before<=20 && after<10100,"First buffered resume sample must not jump half a second.");
  for(int t=148;t<3500;t+=16)clock.PositionAt(10500+t-132,t,1000,true,true);
  Check(Math.Abs(clock.PositionAt(13868,3500,1000,true,true)-13868)<100,"Resume easing must converge to player timing.");
  Check(clock.PositionAt(20000,3516,1000,true,true)==20000,"Real seek must remain immediate.");
  clock.Reset();Check(clock.PositionAt(12345,4000,1000,false,true)==12345,"Paused precise seeks must remain exact.");
  Console.WriteLine("Accent styles, deterministic alternation, rest persistence, legacy cues and resume easing passed.");
 }
}
