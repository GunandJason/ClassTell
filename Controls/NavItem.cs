using System;
using System.Drawing;
using System.Windows.Forms;

namespace ClassTell
{
    /// <summary>左侧导航项：图标 + 文字，选中时水蓝色药丸背景与左侧指示条（带动画）。</summary>
    internal sealed class NavItem : MotionControl
    {
        private double _selected;

        public NavItem()
        {
            Cursor = Cursors.Hand;
            Height = Theme.S(44);
            TabStop = false;
        }

        public string Glyph { get; set; }
        public string Label { get; set; }

        public bool Selected
        {
            get { return _selected > 0.5d; }
        }

        /// <summary>以编程方式触发选中（等价于鼠标点击）。</summary>
        public void PerformClick()
        {
            OnClick(EventArgs.Empty);
        }

        public void SetSelected(bool value)
        {
            Animator.Tween(_selected, value ? 1d : 0d, 280, Ease.EmphasizedDecelerate, v =>
            {
                _selected = v;
                Invalidate();
            });
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Gfx.Smooth(e.Graphics);
            var r = new RectangleF(Theme.S(10), Theme.S(4), Width - Theme.S(20), Height - Theme.S(8));
            float radius = r.Height / 2f;

            Color pill = Theme.Mix(Color.Transparent, Theme.AccentSoft, _selected);
            pill = Theme.Mix(pill, Theme.HoverVeil, HoverAmount * (1d - _selected * 0.6));
            if (pill.A > 0) Gfx.FillRounded(e.Graphics, r, radius, pill);

            // 左侧指示条
            double bar = _selected;
            if (bar > 0.01d)
            {
                float barHeight = (float)(Theme.S(18) * bar);
                float x = r.X + Theme.S(3);
                var barRect = new RectangleF(x, r.Y + (r.Height - barHeight) / 2f, Theme.S(3), barHeight);
                Gfx.FillRounded(e.Graphics, barRect, Theme.SF(1.5f), Color.FromArgb((int)(255 * Math.Min(1d, bar * 1.2d)), Theme.Accent));
            }

            Color fg = Theme.Mix(Theme.Mix(Theme.TextSecondary, Theme.TextPrimary, HoverAmount), Theme.AccentText, _selected);
            float textX;
            if (string.IsNullOrEmpty(Glyph))
            {
                textX = r.X + Theme.S(16);
            }
            else
            {
                float iconX = r.X + Theme.S(14);
                Font iconFont = Theme.IconFont(0.5f);
                int iconSize = (int)Math.Round(Theme.SF(20f));
                Gfx.DrawGlyph(e.Graphics, Glyph, iconFont, fg,
                    new Rectangle((int)Math.Round(iconX), (int)Math.Round(r.Y), iconSize + Theme.S(6), (int)Math.Round(r.Height)),
                    StringAlignment.Center);
                textX = iconX + iconSize + Theme.S(14);
            }
            Font textFont = Theme.Font(0.5f, _selected > 0.5d ? FontStyle.Bold : FontStyle.Regular);
            Gfx.DrawText(e.Graphics, Label, textFont, fg,
                new RectangleF(textX, r.Y, Math.Max(1f, r.Right - textX - Theme.S(8)), r.Height),
                Typography.SingleLine);

            PaintRipples(e.Graphics, r, radius, Theme.RippleColor);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (Enabled && e.Button == MouseButtons.Left)
                StartRipple(e.Location, new RectangleF(Theme.S(10), Theme.S(4), Width - Theme.S(20), Height - Theme.S(8)), (Height - Theme.S(8)) / 2f);
        }
    }
}
