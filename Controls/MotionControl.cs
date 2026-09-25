using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace ClassTell
{
    /// <summary>
    /// 自绘控件基类：双缓冲、悬停/按压插值动画、Material 水波纹，以及主题变化的自动重绘。
    /// </summary>
    internal abstract class MotionControl : Control
    {
        private sealed class RippleBurst
        {
            public PointF Origin;
            public double Progress;
            public float MaxRadius;
            public Motion Handle;
        }

        private readonly List<RippleBurst> _ripples = new List<RippleBurst>();

        /// <summary>悬停进度 0→1。</summary>
        protected double HoverAmount { get; private set; }
        /// <summary>按压进度 0→1。</summary>
        protected double PressAmount { get; private set; }

        protected MotionControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = ThemeBackColor;
            Theme.Changed += HandleThemeChanged;
        }

        /// <summary>
        /// 控件的底色：默认透明（由父容器绘制），需要自带底色的控件覆写它，
        /// 这样深浅模式切换时底色会自动跟随，不会残留另一种模式的颜色。
        /// </summary>
        protected virtual Color ThemeBackColor
        {
            get { return Color.Transparent; }
        }

        /// <summary>
        /// 控件“内容区”的背景色（自绘，不依赖 WinForms 的透明背景缓存，
        /// 从而彻底避免主题切换后出现随机残留的旧底色）。
        /// </summary>
        public virtual Color ContentBackground
        {
            get
            {
                Color self = ThemeBackColor;
                if (self.A == 255) return self;
                var parent = Parent as MotionControl;
                if (parent != null) return parent.ContentBackground;
                Control p = Parent;
                while (p != null)
                {
                    if (p.BackColor.A == 255) return p.BackColor;
                    p = p.Parent;
                }
                return Theme.Window;
            }
        }

        private void HandleThemeChanged()
        {
            try
            {
                Color back = ThemeBackColor;
                if (BackColor != back) BackColor = back;
                OnThemeChanged();
            }
            catch (Exception) { }
            Invalidate();
        }

        protected virtual void OnThemeChanged()
        {
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Theme.Changed -= HandleThemeChanged;
                StopRipples();
            }
            base.Dispose(disposing);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // 自己填背景（而不是依赖 WinForms 的透明背景缓存），避免主题切换后残留旧色
            Color c = ThemeBackColor;
            if (c.A == 0) c = ContentBackground;
            if (c.A == 0) return;
            using (var brush = new SolidBrush(c))
                e.Graphics.FillRectangle(brush, ClientRectangle);
        }

        // ---------- 悬停 / 按压 ----------
        protected void AnimateHover(bool entered, int durationMs = 150)
        {
            double to = entered ? 1d : 0d;
            Animator.Tween(HoverAmount, to, durationMs, Ease.Standard, v => { HoverAmount = v; Invalidate(); });
        }

        protected void AnimatePress(bool down, int durationMs = -1)
        {
            if (durationMs < 0) durationMs = down ? 80 : 220;
            double to = down ? 1d : 0d;
            Animator.Tween(PressAmount, to, durationMs, Ease.Standard, v => { PressAmount = v; Invalidate(); });
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            if (Enabled) AnimateHover(true);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            AnimateHover(false);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (Enabled && e.Button == MouseButtons.Left) AnimatePress(true);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            AnimatePress(false);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            if (!Enabled)
            {
                AnimateHover(false);
                AnimatePress(false);
            }
            Invalidate();
        }

        // ---------- 水波纹 ----------
        /// <summary>在指定位置触发一次水波纹。</summary>
        protected void StartRipple(Point origin, RectangleF shape, float cornerRadius)
        {
            if (!Enabled) return;
            float maxRadius = Math.Max(
                Distance(origin, new PointF(shape.Left, shape.Top)),
                Math.Max(
                    Distance(origin, new PointF(shape.Right, shape.Top)),
                    Math.Max(Distance(origin, new PointF(shape.Left, shape.Bottom)), Distance(origin, new PointF(shape.Right, shape.Bottom)))));
            var burst = new RippleBurst { Origin = origin, Progress = 0d, MaxRadius = maxRadius };
            _ripples.Add(burst);
            burst.Handle = Animator.Run(420, Ease.Decelerate, p =>
            {
                burst.Progress = p;
                Invalidate();
            }, () =>
            {
                _ripples.Remove(burst);
                Invalidate();
            });
        }

        private static float Distance(Point from, PointF to)
        {
            float dx = to.X - from.X;
            float dy = to.Y - from.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        private void StopRipples()
        {
            foreach (RippleBurst b in _ripples.ToArray())
            {
                if (b.Handle != null) b.Handle.Stop();
            }
            _ripples.Clear();
        }

        /// <summary>在圆角裁剪范围内绘制进行中的水波纹。</summary>
        protected void PaintRipples(Graphics g, RectangleF shape, float cornerRadius, Color color)
        {
            if (_ripples.Count == 0) return;
            using (System.Drawing.Drawing2D.GraphicsPath clip = Gfx.RoundedPath(shape, cornerRadius))
            {
                System.Drawing.Region old = g.Clip.Clone();
                g.SetClip(clip, System.Drawing.Drawing2D.CombineMode.Intersect);
                foreach (RippleBurst b in _ripples.ToArray())
                {
                    float radius = b.MaxRadius * (float)b.Progress;
                    int alpha = (int)Math.Round(color.A * (1d - b.Progress));
                    if (alpha <= 0 || radius <= 0f) continue;
                    using (var brush = new SolidBrush(Color.FromArgb(alpha, color)))
                        g.FillEllipse(brush, b.Origin.X - radius, b.Origin.Y - radius, radius * 2f, radius * 2f);
                }
                g.Clip = old;
            }
        }
    }
}
