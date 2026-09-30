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
            internal int Row;
            internal PartyDanceStyle Style;
        }
        internal readonly List<Marker> Accents = new List<Marker>();
        internal int SelectedAccent = -1;
        internal event Action<int> AccentSelected;
        internal event Action<int,double> AccentMoved;
        internal event Action<double,double> LoopRangeSelected;
        private bool _selectingLoop;
        internal bool SelectingLoop => _selectingLoop;
        private double _loopAnchor;
        internal double ViewStart, ViewLength;
        internal double LoopStart, LoopEnd;
        internal bool Overview, EditAccents;
        private Marker _dragAccent;
        private int _dragX;
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
            DoubleBuffered = true; Height = 100; TabStop = true;
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
                    g.FillEllipse(fill, X(cue.Seconds) - 4, 23, 8, 8);
            using (var pen = new Pen(Color.FromArgb(247, 206, 115), 2))
                g.DrawLine(pen, X(Position), 29, X(Position), 62);
            var divisions = Math.Max(2, Math.Min(8, Width / 115));
            for (int i = 0; i <= divisions; i++)
            {
                var seconds = ViewStart+Span * i / divisions;
                var label = Span<30?seconds.ToString("0.00",CultureInfo.InvariantCulture)+"s":Time(seconds); var size = TextRenderer.MeasureText(label, Font);
                var left = Math.Max(0, Math.Min(Width - size.Width, (int)X(seconds) - size.Width / 2));
                TextRenderer.DrawText(g, label, Font, new Point(left, 67), Color.FromArgb(166, 177, 197));
            }
            if (Focused) ControlPaint.DrawFocusRectangle(g, new Rectangle(2, 2, Width - 4, Height - 4));
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || Duration <= 0 || !Enabled) return;
            Focus();
            if(Overview){SeekRequested?.Invoke(SecondsAt(e.X,Width,Duration));return;}
            if((ModifierKeys&Keys.Shift)!=0){_selectingLoop=true;_loopAnchor=At(e.X);Capture=true;return;}
            if (e.Y >= 20 && e.Y <= 31)
            {
                Marker accent = null; float best = 8;
                foreach (var cue in Accents)
                    if (Math.Abs(X(cue.Seconds) - e.X) < best) { accent = cue; best = Math.Abs(X(cue.Seconds) - e.X); }
                if (accent != null)
                {
                    if(EditAccents){SelectedAccent=accent.Row;AccentSelected?.Invoke(accent.Row);_dragAccent=Accents.Find(c=>c.Row==accent.Row)??accent;_dragOriginal=_dragAccent.Seconds;_dragX=e.X;Capture=true;Invalidate();return;}
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
            Scrubbing = true; Capture = true;
            Position = At(e.X); Invalidate();
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if(_selectingLoop){LoopStart=Math.Min(_loopAnchor,At(e.X));LoopEnd=Math.Max(_loopAnchor,At(e.X));Invalidate();return;}
            if(_dragAccent!=null){if(Math.Abs(e.X-_dragX)>2)_dragAccent.Seconds=Math.Round(At(e.X),3);Invalidate();return;}
            if (!Scrubbing) return;
            Position = At(e.X); Invalidate();
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if(_selectingLoop && e.Button==MouseButtons.Left){_selectingLoop=false;Capture=false;LoopStart=Math.Min(_loopAnchor,At(e.X));LoopEnd=Math.Max(_loopAnchor,At(e.X));if(LoopEnd-LoopStart>=.01)LoopRangeSelected?.Invoke(LoopStart,LoopEnd);Invalidate();return;}
            if(_dragAccent!=null && e.Button==MouseButtons.Left){var cue=_dragAccent;_dragAccent=null;Capture=false;if(Math.Abs(e.X-_dragX)>2)AccentMoved?.Invoke(cue.Row,Math.Round(At(e.X),3));Invalidate();return;}
            if (!Scrubbing || e.Button != MouseButtons.Left) return;
            Position = At(e.X); Scrubbing = false; Capture = false;
            SeekRequested?.Invoke(Position); Invalidate();
        }
        protected override void OnMouseCaptureChanged(EventArgs e)
        { base.OnMouseCaptureChanged(e); if (!Capture) {_selectingLoop=false;Scrubbing = false;if(_dragAccent!=null)_dragAccent.Seconds=_dragOriginal;_dragAccent=null;Invalidate();} }
        protected override void OnMouseWheel(MouseEventArgs e){base.OnMouseWheel(e);Zoom(e.Delta>0?.5:2);}
        protected override bool IsInputKey(Keys keyData)
        { var key = keyData & Keys.KeyCode; return key == Keys.Left || key == Keys.Right || key == Keys.Home || key == Keys.End || base.IsInputKey(keyData); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e); if (Duration <= 0 || !Enabled) return;
            if(e.KeyCode==Keys.Escape && _dragAccent!=null){Capture=false;e.Handled=true;return;}
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
