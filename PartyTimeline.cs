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
        private float X(double seconds) { return MarginX + (float)(Math.Max(0, Math.Min(Duration, seconds)) / (Duration > 0 ? Duration : 1) * (Width - MarginX * 2)); }
        internal static string Time(double seconds)
        {
            seconds = Math.Max(0, seconds);
            return ((int)seconds / 60).ToString(CultureInfo.InvariantCulture) + ":" +
                ((int)seconds % 60).ToString("00", CultureInfo.InvariantCulture);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            TextRenderer.DrawText(g, "TIMELINE", Font, new Point(MarginX, 8), ForeColor);
            var clock = Duration > 0 ? Time(Position) + " / " + Time(Duration) : "Duration unavailable";
            TextRenderer.DrawText(g, clock, Font, new Rectangle(0, 6, Width - MarginX, 22), ForeColor, TextFormatFlags.Right);
            using (var track = new SolidBrush(Color.FromArgb(57, 64, 81)))
                g.FillRectangle(track, MarginX, 43, Math.Max(1, Width - MarginX * 2), 10);
            if (Duration <= 0) return;
            var ordered = new List<Marker>(Markers); ordered.Sort((a,b) => a.Seconds.CompareTo(b.Seconds));
            for (int i = 0; i < ordered.Count; i++)
            {
                var marker = ordered[i];
                var end = i + 1 < ordered.Count ? ordered[i + 1].Seconds : Duration;
                var color = marker.Style == PartyDanceStyle.Hold ? Color.FromArgb(124, 135, 155) :
                    marker.Style == PartyDanceStyle.SideToSide ? Color.FromArgb(170, 138, 235) :
                    marker.Style == PartyDanceStyle.HalfSpeed ? Color.FromArgb(223, 172, 100) : Color.FromArgb(98, 178, 221);
                using (var fill = new SolidBrush(Color.FromArgb(105, color)))
                    g.FillRectangle(fill, X(marker.Seconds), 43, Math.Max(0, X(end) - X(marker.Seconds)), 10);
                var x = X(marker.Seconds);
                using (var fill = new SolidBrush(marker.Row == SelectedRow ? Color.White : color))
                    g.FillPolygon(fill, new[] { new PointF(x, 32), new PointF(x + 6, 39), new PointF(x, 46), new PointF(x - 6, 39) });
            }
            using (var pen = new Pen(Color.FromArgb(247, 206, 115), 2))
                g.DrawLine(pen, X(Position), 29, X(Position), 62);
            var divisions = Math.Max(2, Math.Min(8, Width / 115));
            for (int i = 0; i <= divisions; i++)
            {
                var seconds = Duration * i / divisions;
                var label = Time(seconds); var size = TextRenderer.MeasureText(label, Font);
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
            Position = SecondsAt(e.X, Width, Duration); Invalidate();
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!Scrubbing) return;
            Position = SecondsAt(e.X, Width, Duration); Invalidate();
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!Scrubbing || e.Button != MouseButtons.Left) return;
            Position = SecondsAt(e.X, Width, Duration); Scrubbing = false; Capture = false;
            SeekRequested?.Invoke(Position); Invalidate();
        }
        protected override void OnMouseCaptureChanged(EventArgs e)
        { base.OnMouseCaptureChanged(e); if (!Capture) Scrubbing = false; }
        protected override bool IsInputKey(Keys keyData)
        { return keyData == Keys.Left || keyData == Keys.Right || keyData == Keys.Home || keyData == Keys.End || base.IsInputKey(keyData); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e); if (Duration <= 0 || !Enabled) return;
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
