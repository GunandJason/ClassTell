using System;
using System.Drawing;
using System.Windows.Forms;

namespace ClassTell
{
    /// <summary>状态药丸：小圆点 + 文字（连接状态、登录状态等）。</summary>
    internal sealed class StatusChip : MotionControl
    {
        private double _pulse;
        private bool _looping;
        private string _text = string.Empty;
        private Color _dot = Color.Gray;

        public StatusChip()
        {
            Height = Theme.S(28);
            Width = Theme.S(150);
        }

        public string ChipText
        {
            get { return _text; }
            set { if (_text != value) { _text = value ?? string.Empty; Invalidate(); } }
        }

        public Color DotColor
        {
            get { return _dot; }
            set { if (_dot != value) { _dot = value; Invalidate(); } }
        }

        /// <summary>圆点呼吸动画（连接中 / 收取中使用）。</summary>
        public void SetPulsing(bool on)
        {
            if (on == _looping) return;
            _looping = on;
            if (on) StartPulseLoop();
            else { _pulse = 0d; Invalidate(); }
        }

        private void StartPulseLoop()
        {
            if (!_looping) return;
            Animator.Run(1100, Ease.Standard, p =>
            {
                _pulse = Math.Sin(p * Math.PI * 2d) * 0.5d + 0.5d;
                Invalidate();
            }, () =>
            {
                if (_looping) StartPulseLoop();
            });
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Gfx.Smooth(e.Graphics);
            var r = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            float radius = r.Height / 2f;

            Gfx.FillRounded(e.Graphics, r, radius, Theme.SurfaceAlt);
            Gfx.StrokeRounded(e.Graphics, r, radius, Theme.Mix(Theme.Outline, Theme.Accent, HoverAmount * 0.5), 1f);

            float dotSize = Theme.SF(8f);
            float dotAlpha = _looping ? (float)(0.35d + 0.65d * _pulse) : 1f;
            var dotRect = new RectangleF(r.X + Theme.S(10), r.Y + (r.Height - dotSize) / 2f, dotSize, dotSize);
            Gfx.FillRounded(e.Graphics, dotRect, dotSize / 2f, Color.FromArgb((int)(255 * dotAlpha), _dot));

            Font font = Theme.Font(-1.2f, FontStyle.Regular);
            float textX = dotRect.Right + Theme.S(7);
            Gfx.DrawText(e.Graphics, _text, font, Theme.TextSecondary,
                new RectangleF(textX, r.Y, Math.Max(1f, r.Right - textX - Theme.S(10)), r.Height),
                Typography.SingleLine);
        }
    }

    /// <summary>Material 圆形加载指示器（旋转弧线）。</summary>
    internal sealed class Spinner : MotionControl
    {
        private readonly Timer _timer;
        private double _angle;

        public Spinner()
        {
            Width = Theme.S(18);
            Height = Theme.S(18);
            _timer = new Timer { Interval = 16 };
            _timer.Tick += (s, e) =>
            {
                _angle = (_angle + 6d) % 360d;
                Invalidate();
            };
        }

        public Color ArcColor { get; set; }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible) _timer.Start(); else _timer.Stop();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _timer.Stop();
                _timer.Dispose();
            }
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Gfx.Smooth(e.Graphics);
            float thickness = Theme.SF(2.4f);
            var rect = new RectangleF(thickness, thickness, Width - thickness * 2f, Height - thickness * 2f);
            Color color = ArcColor.A == 0 ? Theme.Accent : ArcColor;
            using (var pen = new Pen(Theme.Mix(Theme.Outline, color, 0.35), thickness))
            {
                pen.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                pen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                e.Graphics.DrawEllipse(pen, rect);
            }
            using (var pen = new Pen(color, thickness))
            {
                pen.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                pen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                e.Graphics.DrawArc(pen, rect, (float)_angle, 100f);
            }
        }
    }
}
