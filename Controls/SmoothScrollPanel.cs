using System;
using System.Drawing;
using System.Windows.Forms;

namespace ClassTell
{
    /// <summary>
    /// 平滑滚动容器：内部一个 Host 载体，滚轮驱动缓动位移，并在右侧绘制细滚动条。
    /// 用法：把内容控件加到 <see cref="Host"/>，并设置 <see cref="ContentHeight"/>。
    /// </summary>
    internal sealed class SmoothScrollPanel : MotionControl
    {
        private readonly Panel _host;
        private double _offset;
        private double _target;
        private int _contentHeight;
        private Motion _scroll;
        private bool _barHover;

        public SmoothScrollPanel()
        {
            BackColor = Theme.Window;
            // 宿主用不透明底色（与滚动容器同色），避免透明链条导致主题切换后残留旧底色
            _host = new Panel { BackColor = Theme.Window, Location = new Point(0, 0) };
            _host.MouseWheel += (s, e) => HandleWheel(e.Delta);
            Controls.Add(_host);
        }

        /// <summary>滚动容器自带底色，需要跟随深浅模式。</summary>
        protected override Color ThemeBackColor { get { return Theme.Window; } }

        protected override void OnThemeChanged()
        {
            _host.BackColor = Theme.Window;
            LayoutHost();
        }

        /// <summary>内容载体：所有子控件加到这里。</summary>
        public Control Host { get { return _host; } }

        public int ContentHeight
        {
            get { return _contentHeight; }
            set
            {
                int v = Math.Max(0, value);
                if (v == _contentHeight) return;
                _contentHeight = v;
                LayoutHost();
            }
        }

        public double Offset { get { return _offset; } }

        public int BarWidth { get { return Theme.S(6); } }

        private double MaxOffset
        {
            get { return Math.Max(0d, _contentHeight - ClientSize.Height); }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutHost();
        }

        private void LayoutHost()
        {
            int w = Math.Max(0, ClientSize.Width - BarWidth - Theme.S(2));
            int h = Math.Max(ClientSize.Height, _contentHeight);
            _host.SetBounds(0, -(int)Math.Round(_offset), w, h);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            HandleWheel(e.Delta);
        }

        private void HandleWheel(int delta)
        {
            if (MaxOffset <= 0d) return;
            double step = Theme.SF(72f);
            ScrollTo(_target - Math.Sign(delta) * step);
        }

        public void ScrollTo(double value)
        {
            _target = Math.Max(0d, Math.Min(MaxOffset, value));
            if (_scroll != null) _scroll.Stop();
            double from = _offset;
            double to = _target;
            _scroll = Animator.Tween(from, to, 220, Ease.Decelerate, v =>
            {
                _offset = v;
                _host.Top = -(int)Math.Round(_offset);
                Invalidate();
            });
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool hover = MaxOffset > 0d && e.X >= ClientSize.Width - BarWidth - Theme.S(10);
            if (hover != _barHover)
            {
                _barHover = hover;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_barHover)
            {
                _barHover = false;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            double max = MaxOffset;
            if (max <= 0d) return;

            float trackTop = Theme.S(6);
            float trackHeight = Math.Max(Theme.S(24), ClientSize.Height - Theme.S(12));
            float thumbHeight = Math.Max(Theme.SF(32f), (float)(trackHeight * (ClientSize.Height / (double)Math.Max(1, _contentHeight))));
            float travel = Math.Max(0f, trackHeight - thumbHeight);
            float thumbTop = trackTop + (float)(_offset / max * travel);
            float barWidth = _barHover ? BarWidth + Theme.S(2) : BarWidth;
            float x = ClientSize.Width - barWidth - Theme.S(2);

            var thumb = new RectangleF(x, thumbTop, barWidth, thumbHeight);
            Gfx.FillRounded(e.Graphics, thumb, barWidth / 2f,
                Theme.Mix(Color.FromArgb(70, Theme.TextSecondary), Theme.Accent, _barHover ? 0.75 : 0.25));
        }
    }
}
