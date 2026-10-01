using System;using System.Drawing;using System.Diagnostics;using System.Reflection;using System.Windows.Forms;using System.Collections.Generic;using System.Linq;using System.Threading;
class DancerPreparationBenchmark {
 const BindingFlags F=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
 static object Call(object o,string n,params object[] a){return o.GetType().GetMethod(n,F).Invoke(o,a);}
 static object Get(object o,string n){return o.GetType().GetField(n,F).GetValue(o);}
 [STAThread]static void Main(){var a=Assembly.LoadFrom(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"mb_DesktopLyrics.dll"));using(var form=new Form()){form.ClientSize=new Size(3840,2160);var hwnd=form.Handle;
 for(int run=0;run<3;run++)using(var r=(IDisposable)Activator.CreateInstance(a.GetType("MusicBeePlugin.GpuSceneRenderer"),F,null,new object[]{hwnd,form.ClientSize,false},null)){
 foreach(int height in new[]{1626,1500}){int width=(int)Math.Round(height*180d/353);var times=new List<double>();var watch=Stopwatch.StartNew();double first=-1;
 while(true){var t=Stopwatch.StartNew();Call(r,"BeginDancers");for(int c=0;c<2;c++)Call(r,"AddDancer",c,new Rectangle(c*2000,0,width,height),0,1f,0f,0f);Call(r,"CommitDancers");times.Add(t.Elapsed.TotalMilliseconds);
 var sizes=(Size[])Get(r,"_dancerSizes");if(first<0 && sizes[0]==new Size(width,height)&&sizes[4]==new Size(width,height))first=watch.Elapsed.TotalMilliseconds;
 if(sizes.All(s=>s==new Size(width,height)))break;if(watch.ElapsedMilliseconds>10000)throw new Exception("timeout");Thread.Sleep(8);}
 times.Sort();Console.WriteLine("run="+run+" height="+height+" uiMean="+times.Average().ToString("F3")+" uiP95="+times[(int)Math.Floor((times.Count-1)*.95)].ToString("F3")+" uiMax="+times.Last().ToString("F3")+" firstPairReady="+first.ToString("F3")+" allReady="+watch.Elapsed.TotalMilliseconds.ToString("F3"));
 }
 Call(r,"ClearDancers");
 }}}}
