using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace MusicBeePlugin
{
    internal sealed class PartyTimeline : Control
    {
        internal sealed class Marker
        {
            internal double Seconds;
            internal double Prepare=.1, Hold, Recovery=.22;
            internal int Row;
            internal PartyDanceStyle Style;
        }
        internal readonly List<Marker> Accents = new List<Marker>();
        internal int SelectedAccent = -1;
        internal event Action<int> AccentSelected;
        internal event Action<int,double> AccentMoved;
        internal event Action<int,double,double,double> EnvelopeChanged;
        internal Func<double,double> SnapTime;
        internal TimelineWaveform.Range Waveform;
        internal string WaveformStatus;
        private Marker _envelope;
        private int _handle;
        private double _prepareOriginal,_holdOriginal,_recoveryOriginal;
        private void MoveEnvelope(int x)
        {
            var time=At(x);
            if(_handle==0)_envelope.Prepare=Math.Round(Math.Max(0,Math.Min(1,_envelope.Seconds-time)),3);
            if(_handle==1)_envelope.Hold=Math.Round(Math.Max(0,Math.Min(5,time-_envelope.Seconds)),3);
            if(_handle==2)_envelope.Recovery=Math.Round(Math.Max(.02,Math.Min(2,time-_envelope.Seconds-_envelope.Hold)),3);
        }
        internal event Action<double,double> LoopRangeSelected;
        private bool _selectingLoop;
        internal bool SelectingLoop => _selectingLoop;
        private double _loopAnchor;
        internal double ViewStart, ViewLength;
        internal double LoopStart, LoopEnd;
        internal bool Overview, EditAccents;
        private Marker _dragAccent;
        private int _dragX;
        private bool _accentDragging, _panning;
        private double _panStart;
        private bool _detailPending, _detailPanning;
        private double _panSpan;
        private void PanDetail(int x)
        {
            ViewStart = Math.Max(0, Math.Min(Duration - _panSpan, _panStart - (x - _panX) * _panSpan / Math.Max(1, Width - MarginX * 2)));
            ViewPanned?.Invoke(ViewStart); Invalidate();
        }
        private int _panX;
        internal event Action<double> ViewPanned;
        private void Pan(double start)
        {
            var length = Math.Min(Duration, Math.Max(0, LoopEnd - LoopStart));
            LoopStart = Math.Max(0, Math.Min(Duration - length, start));
            LoopEnd = LoopStart + length;
            ViewPanned?.Invoke(LoopStart);
            Invalidate();
        }
        internal void ZoomAt(double factor, int x)
        {
            if (Duration <= 0 || _dragAccent != null || _envelope != null || _selectingLoop || _detailPending || _detailPanning || _panning) return;
            var fraction = SecondsAt(x, Width, 1);
            var anchor = ViewStart + fraction * Span;
            ViewLength = Math.Min(Duration, Math.Max(.5, Span * factor));
            ViewStart = Math.Max(0, Math.Min(Duration - ViewLength, anchor - fraction * ViewLength));
            Invalidate();
        }
        private double DragTime(int x) => Math.Round(Math.Max(0, Math.Min(Duration,
            _dragOriginal + (x - _dragX) * Span / Math.Max(1, Width - MarginX * 2))), 3);
        private double _dragOriginal;
        private double Span => ViewLength>0?Math.Min(Duration,ViewLength):Duration;
        private double At(int x) => Math.Max(0,Math.Min(Duration,ViewStart+SecondsAt(x,Width,Span)));
        internal void Zoom(double factor) {
            if(Duration<=0)return;
            var anchor=Math.Max(ViewStart,Math.Min(ViewStart+Span,Position));
            ViewLength=Math.Min(Duration,Math.Max(.5,Span*factor));
            ViewStart=Math.Max(0,Math.Min(Duration-ViewLength,anchor-ViewLength/2));Invalidate();
        }
        internal readonly List<Marker> Markers = new List<Marker>();
        internal double Duration, Position;
        internal int SelectedRow = -1;
        internal bool Scrubbing { get; private set; }
        internal event Action<double> SeekRequested;
        internal event Action<int> MarkerSelected;
        private const int MarginX = 18;
        internal PartyTimeline()
        {
            DoubleBuffered = true; Height = 220; TabStop = true;
            BackColor = Color.FromArgb(23, 27, 38); ForeColor = Color.FromArgb(232, 236, 245);
            AccessibleName = "Song timeline. Arrow keys seek five seconds; click a marker to select its section.";
            AccessibleRole = AccessibleRole.Slider;
        }
        internal static double SecondsAt(int x, int width, double duration)
        {
            if (duration <= 0 || width <= MarginX * 2) return 0;
            return Math.Max(0, Math.Min(1, (x - MarginX) / (double)(width - MarginX * 2))) * duration;
        }
        private float X(double seconds) { return MarginX + (float)((seconds-ViewStart) / (Span > 0 ? Span : 1) * (Width - MarginX * 2)); }
        internal static string Time(double seconds)
        {
            seconds = Math.Max(0, seconds);
            return ((int)seconds / 60).ToString(CultureInfo.InvariantCulture) + ":" +
                ((int)seconds % 60).ToString("00", CultureInfo.InvariantCulture);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            if(Overview){
                using(var track=new SolidBrush(Color.FromArgb(57,64,81)))g.FillRectangle(track,MarginX,10,Math.Max(1,Width-36),12);
                using(var selected=new SolidBrush(Color.FromArgb(100,98,178,221)))g.FillRectangle(selected,MarginX+(float)(LoopStart/Math.Max(1,Duration)*(Width-36)),8,Math.Max(2,(float)((LoopEnd-LoopStart)/Math.Max(1,Duration)*(Width-36))),16);
                return;
            }
            TextRenderer.DrawText(g, "TIMELINE", Font, new Point(MarginX, 8), ForeColor);
            var clock = Duration > 0 ? Time(Position) + "." + ((int)(Math.Max(0, Position) * 1000) % 1000).ToString("000", CultureInfo.InvariantCulture) + " / " + Time(Duration) : "Duration unavailable";
            TextRenderer.DrawText(g, clock, Font, new Rectangle(0, 6, Width - MarginX, 22), ForeColor, TextFormatFlags.Right);
            using (var track = new SolidBrush(Color.FromArgb(57, 64, 81)))
                g.FillRectangle(track, MarginX, 43, Math.Max(1, Width - MarginX * 2), 10);
            if (Duration <= 0) return;
            var contentClip = g.Save();
            g.SetClip(new Rectangle(MarginX, 20, Math.Max(1, Width - 2 * MarginX), 165), CombineMode.Intersect);
            if(LoopEnd>LoopStart)using(var loop=new SolidBrush(Color.FromArgb(35,247,206,115)))g.FillRectangle(loop,X(LoopStart),20,X(LoopEnd)-X(LoopStart),43);
            var ordered = new List<Marker>(Markers); ordered.Sort((a,b) => a.Seconds.CompareTo(b.Seconds));
            for (int i = 0; i < ordered.Count; i++)
            {
                var marker = ordered[i];
                var end = i + 1 < ordered.Count ? ordered[i + 1].Seconds : Duration;
                var color = marker.Style == PartyDanceStyle.Rest ? Color.FromArgb(88, 205, 183) :
                    marker.Style == PartyDanceStyle.Hold ? Color.FromArgb(124, 135, 155) :
                    marker.Style == PartyDanceStyle.SideToSide ? Color.FromArgb(170, 138, 235) :
                    marker.Style == PartyDanceStyle.HalfSpeed ? Color.FromArgb(223, 172, 100) : Color.FromArgb(98, 178, 221);
                using (var fill = new SolidBrush(Color.FromArgb(105, color)))
                    g.FillRectangle(fill, Math.Max(MarginX,X(marker.Seconds)), 43, Math.Max(0, Math.Min(Width-MarginX,X(end)) - Math.Max(MarginX,X(marker.Seconds))), 10);
                var x = X(marker.Seconds);if(x<MarginX || x>Width-MarginX)continue;
                using (var fill = new SolidBrush(marker.Row == SelectedRow ? Color.White : color))
                    g.FillPolygon(fill, new[] { new PointF(x, 32), new PointF(x + 6, 39), new PointF(x, 46), new PointF(x - 6, 39) });
            }
            foreach (var cue in Accents.FindAll(c=>c.Seconds>=ViewStart && c.Seconds<=ViewStart+Span))
                using (var fill = new SolidBrush(cue.Row == SelectedAccent ? Color.White : Color.FromArgb(247, 206, 115)))
                    g.FillEllipse(fill, X(_dragAccent != null && cue.Row == _dragAccent.Row ? _dragAccent.Seconds : cue.Seconds) - 4, 23, 8, 8);
            using (var pen = new Pen(Color.FromArgb(247, 206, 115), 2))
                g.DrawLine(pen, X(Position), 29, X(Position), 62);
            g.Restore(contentClip);
            var divisions = Math.Max(2, Math.Min(8, Width / 115));
            for (int i = 0; i <= divisions; i++)
            {
                var seconds = ViewStart+Span * i / divisions;
                var label = Span<30?seconds.ToString("0.00",CultureInfo.InvariantCulture)+"s":Time(seconds); var size = TextRenderer.MeasureText(label, Font);
                var left = Math.Max(0, Math.Min(Width - size.Width, (int)X(seconds) - size.Width / 2));
                TextRenderer.DrawText(g, label, Font, new Point(left, 67), Color.FromArgb(166, 177, 197));
            }
            contentClip = g.Save();
            g.SetClip(new Rectangle(MarginX, 86, Math.Max(1, Width - 2 * MarginX), 99), CombineMode.Intersect);
            if(Waveform?.Peaks != null && Waveform.Start < ViewStart+Span && Waveform.Start+Waveform.Length > ViewStart)
            {
                using(var outline=new Pen(Color.FromArgb(140,184,209)))
                using(var body=new Pen(Color.FromArgb(80,134,166)))
                using(var attacks=new Pen(Color.FromArgb(250,187,86)))
                {
                    int pixels=Math.Max(1,Width-MarginX*2);var peaks=Waveform.Peaks;float previousPeak=0;
                    for(int px=0;px<pixels;px++){
                        int first,last;
                        if(!Waveform.PixelBins(ViewStart+px*Span/pixels,ViewStart+(px+1)*Span/pixels,out first,out last)){previousPeak=0;continue;}
                        float peak=0,attack=0;double energy=0;int bins=0;
                        for(int n=first;n<Math.Min(last,peaks.Length);n++){
                            peak=Math.Max(peak,peaks[n]);
                            var rms=Waveform.Rms==null?0:Waveform.Rms[n];energy+=rms*rms;bins++;
                            if(Waveform.Attacks!=null)attack=Math.Max(attack,Waveform.Attacks[n]);
                        }
                        float level=(float)Math.Sqrt(energy/Math.Max(1,bins));
                        g.DrawLine(body,MarginX+px,108-level*19,MarginX+px,108+level*19);
                        g.DrawLine(outline,MarginX+Math.Max(0,px-1),108-(px==0?peak:previousPeak)*19,MarginX+px,108-peak*19);
                        g.DrawLine(outline,MarginX+Math.Max(0,px-1),108+(px==0?peak:previousPeak)*19,MarginX+px,108+peak*19);previousPeak=peak;
                        if(attack>0)g.DrawLine(attacks,MarginX+px,169,MarginX+px,169-Math.Min(1,attack/Waveform.AttackDisplayMaximum)*25);
                    }
                }
                TextRenderer.DrawText(g,"ATTACK STRENGTH (relative)",Font,new Point(MarginX,127),Color.FromArgb(250,187,86));
            }
            else TextRenderer.DrawText(g,WaveformStatus??"Waveform",Font,new Point(MarginX,94),Color.Silver);
            g.Restore(contentClip);
            contentClip = g.Save();
            g.SetClip(new Rectangle(MarginX, 20, Math.Max(1, Width - 2 * MarginX), 165), CombineMode.Intersect);
            foreach(var cue in Accents) {
                var time=_dragAccent!=null && cue.Row==_dragAccent.Row?_dragAccent.Seconds:cue.Seconds;
                if(time<ViewStart || time>ViewStart+Span)continue;
                using(var guide=new Pen(Color.FromArgb(cue.Row==SelectedAccent?180:70,247,206,115))) {
                    guide.DashStyle=DashStyle.Dot;g.DrawLine(guide,X(time),31,X(time),170);
                }
            }
            var selectedAccent=_envelope??Accents.Find(c=>c.Row==SelectedAccent);
            if(selectedAccent!=null){
                var cue=selectedAccent;
                var points=new[]{cue.Seconds-cue.Prepare,cue.Seconds,cue.Seconds+cue.Hold,cue.Seconds+cue.Hold+cue.Recovery};
                var colors=new[]{Color.MediumPurple,Color.Gold,Color.MediumSeaGreen};
                for(int i=0;i<3;i++)using(var fill=new SolidBrush(colors[i])){
                    float left=Math.Max(MarginX,X(points[i])),right=Math.Min(Width-MarginX,X(points[i+1]));
                    if(right>left)g.FillRectangle(fill,left,175,right-left,6);
                }
                foreach(var time in new[]{points[0],points[2],points[3]}){
                    float x=X(time);if(x>=MarginX&&x<=Width-MarginX)g.FillRectangle(Brushes.White,x-3,172,6,12);
                }
                g.Restore(contentClip);
                contentClip = g.Save();
                TextRenderer.DrawText(g,"Selected accent: purple = preparation, gold = hold, green = recovery. Drag white handles.",Font,new Point(MarginX,190),Color.Silver);
            }
            g.Restore(contentClip);
            if (Focused) ControlPaint.DrawFocusRectangle(g, new Rectangle(2, 2, Width - 4, Height - 4));
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || Duration <= 0 || !Enabled) return;
            Focus();
            if (Overview)
            {
                var left = MarginX + LoopStart / Duration * (Width - MarginX * 2);
                var right = MarginX + LoopEnd / Duration * (Width - MarginX * 2);
                if (e.X < left - 3 || e.X > right + 3)
                    Pan(SecondsAt(e.X, Width, Duration) - (LoopEnd - LoopStart) / 2);
                _panStart = LoopStart; _panX = e.X; _panning = true; Capture = true;
                return;
            }
            if(e.Y>=169 && e.Y<=184){
                var cue=Accents.Find(c=>c.Row==SelectedAccent);
                if(cue!=null){
                    var times=new[]{cue.Seconds-cue.Prepare,cue.Seconds+cue.Hold,cue.Seconds+cue.Hold+cue.Recovery};
                    for(int i=0;i<3;i++)if(Math.Abs(X(times[i])-e.X)<=6){
                        _envelope=cue;_handle=i;_prepareOriginal=cue.Prepare;_holdOriginal=cue.Hold;_recoveryOriginal=cue.Recovery;Capture=true;return;
                    }
                }
            }
            if((ModifierKeys&Keys.Shift)!=0){_selectingLoop=true;_loopAnchor=At(e.X);Capture=true;return;}
            if (e.Y >= 20 && e.Y <= 31)
            {
                Marker accent = null; float best = 8;
                foreach (var cue in Accents)
                    if (Math.Abs(X(cue.Seconds) - e.X) < best) { accent = cue; best = Math.Abs(X(cue.Seconds) - e.X); }
                if (accent != null)
                {
                    if(EditAccents){SelectedAccent=accent.Row;AccentSelected?.Invoke(accent.Row);_dragAccent=Accents.Find(c=>c.Row==accent.Row)??accent;_dragOriginal=_dragAccent.Seconds;_dragX=e.X;_accentDragging=false;Capture=true;Invalidate();return;}
                    AccentSelected?.Invoke(accent.Row); Position = Math.Max(0, Math.Min(Duration, accent.Seconds));
                    SeekRequested?.Invoke(Position); Invalidate(); return;
                }
            }
            Marker nearest = null; float distance = 8;
            foreach (var marker in Markers)
                if (Math.Abs(X(marker.Seconds) - e.X) < distance && e.Y >= 27 && e.Y <= 55)
                { nearest = marker; distance = Math.Abs(X(marker.Seconds) - e.X); }
            if (nearest != null)
            {
                MarkerSelected?.Invoke(nearest.Row);
                Position = Math.Max(0, Math.Min(Duration, nearest.Seconds));
                SeekRequested?.Invoke(Position); Invalidate(); return;
            }
            if (Math.Abs(X(Position) - e.X) > 6 || e.Y < 29 || e.Y > 62)
            {
                _detailPending = true; _panStart = ViewStart; _panSpan = Span; _panX = e.X; Capture = true; return;
            }
            Scrubbing = true; Capture = true;
            Position = At(e.X); Invalidate();
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_detailPending || _detailPanning)
            {
                if (Math.Abs(e.X - _panX) > 3) _detailPanning = true;
                if (_detailPanning) { _detailPending = false; Cursor = Cursors.Hand; PanDetail(e.X); }
                return;
            }
            if (_envelope != null) { MoveEnvelope(e.X); Invalidate(); return; }
            if (_panning) { Pan(_panStart + (e.X - _panX) * Duration / Math.Max(1, Width - MarginX * 2)); return; }
            if(_selectingLoop){LoopStart=Math.Min(_loopAnchor,At(e.X));LoopEnd=Math.Max(_loopAnchor,At(e.X));Invalidate();return;}
            if (_dragAccent != null)
            {
                _accentDragging |= Math.Abs(e.X - _dragX) > 2;
                if (_accentDragging) _dragAccent.Seconds = DragTime(e.X);
                Invalidate(); return;
            }
            if (!Scrubbing) return;
            Position = At(e.X); Invalidate();
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if ((_detailPending || _detailPanning) && e.Button == MouseButtons.Left)
            {
                bool dragged = _detailPanning;
                if (dragged) PanDetail(e.X);
                _detailPending = _detailPanning = false; Capture = false; Cursor = Cursors.Default;
                if (!dragged) { Position = At(e.X); SeekRequested?.Invoke(Position); Invalidate(); }
                return;
            }
            if (_envelope != null && e.Button==MouseButtons.Left) {
                MoveEnvelope(e.X);var cue=_envelope;_envelope=null;Capture=false;
                EnvelopeChanged?.Invoke(cue.Row,cue.Prepare,cue.Hold,cue.Recovery);Invalidate();return;
            }
            if (_panning && e.Button == MouseButtons.Left) { Pan(_panStart + (e.X - _panX) * Duration / Math.Max(1, Width - MarginX * 2)); _panning = false; Capture = false; return; }
            if(_selectingLoop && e.Button==MouseButtons.Left){_selectingLoop=false;Capture=false;LoopStart=Math.Min(_loopAnchor,At(e.X));LoopEnd=Math.Max(_loopAnchor,At(e.X));if(LoopEnd-LoopStart>=.01)LoopRangeSelected?.Invoke(LoopStart,LoopEnd);Invalidate();return;}
            if(_dragAccent!=null && e.Button==MouseButtons.Left){var cue=_dragAccent;_dragAccent=null;Capture=false;if(_accentDragging)AccentMoved?.Invoke(cue.Row,SnapTime?.Invoke(DragTime(e.X))??DragTime(e.X));Invalidate();return;}
            if (!Scrubbing || e.Button != MouseButtons.Left) return;
            Position = At(e.X); Scrubbing = false; Capture = false;
            SeekRequested?.Invoke(Position); Invalidate();
        }
        protected override void OnMouseCaptureChanged(EventArgs e)
        { base.OnMouseCaptureChanged(e); if (!Capture) {_detailPending=_detailPanning=false;Cursor=Cursors.Default;_panning=false;_selectingLoop=false;Scrubbing = false;
            if(_envelope!=null){_envelope.Prepare=_prepareOriginal;_envelope.Hold=_holdOriginal;_envelope.Recovery=_recoveryOriginal;_envelope=null;}if(_dragAccent!=null)_dragAccent.Seconds=_dragOriginal;_dragAccent=null;Invalidate();} }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (e.Delta == 0) return;
            if (Overview) Pan(LoopStart - e.Delta / 120d * (LoopEnd - LoopStart) * .2);
            else ZoomAt(Math.Pow(.8, e.Delta / 120d), e.X);
            if (e is HandledMouseEventArgs handled) handled.Handled = true;
        }
        protected override bool IsInputKey(Keys keyData)
        { var key = keyData & Keys.KeyCode; return key == Keys.Left || key == Keys.Right || key == Keys.Home || key == Keys.End || base.IsInputKey(keyData); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e); if (Duration <= 0 || !Enabled) return;
            if(e.KeyCode==Keys.Escape && (_dragAccent!=null || _envelope!=null || _detailPending || _detailPanning)){Capture=false;e.Handled=true;return;}
            if(EditAccents && SelectedAccent>=0 && (e.KeyCode==Keys.Left||e.KeyCode==Keys.Right)){
                var cue=Accents.Find(c=>c.Row==SelectedAccent);if(cue!=null)AccentMoved?.Invoke(cue.Row,Math.Round(Math.Max(0,Math.Min(Duration,cue.Seconds+(e.KeyCode==Keys.Left?-1:1)*(e.Shift?.001:.01))),3));
                e.Handled=true;e.SuppressKeyPress=true;return;
            }
            var value = Position;
            if (e.KeyCode == Keys.Left) value -= 5;
            else if (e.KeyCode == Keys.Right) value += 5;
            else if (e.KeyCode == Keys.Home) value = 0;
            else if (e.KeyCode == Keys.End) value = Duration;
            else return;
            Position = Math.Max(0, Math.Min(Duration, value)); SeekRequested?.Invoke(Position);
            e.Handled = true; Invalidate();
        }
    }
}
