using System;
using System.Drawing;
using System.Windows.Forms;

namespace ClassTell
{
    /// <summary>
    /// 配色色卡：圆形色块 + 选中环 + 名称，点击切换强调色（Material 风格动效）。
    /// </summary>
    internal sealed class AccentSwatch : MotionControl
    {
        private bool _selected;
        private double _selectAmount;

        public AccentSwatch()
        {
            Cursor = Cursors.Hand;
            Size = new Size(Theme.S(78), Theme.S(72));
            TabStop = false;
            Swatch = Theme.Accent;
            Name = string.Empty;
        }

        /// <summary>该色卡对应的配色键（Theme.Palettes 里的 Key）。</summary>
        public string PaletteKey { get; set; }

        public Color Swatch { get; set; }

        public bool Selected
        {
            get { return _selected; }
            set
            {
                if (_selected == value) return;
                _selected = value;
                Animator.Tween(_selectAmount, value ? 1d : 0d, 260, Ease.EmphasizedDecelerate, v =>
                {
                    _selectAmount = v;
                    Invalidate();
                });
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Gfx.Smooth(g);

            float radius = Theme.SF(19f) * (float)(1d + 0.08d * HoverAmount);
            float cy = Theme.S(24);
            float cx = Width / 2f;

            // 悬停/选中的外圈
            double ring = Math.Max(HoverAmount * 0.5d, _selectAmount);
            if (ring > 0.02d)
            {
                float ringRadius = radius + (float)(Theme.SF(6f) * ring);
                Gfx.FillRounded(g, new RectangleF(cx - ringRadius, cy - ringRadius, ringRadius * 2f, ringRadius * 2f),
                    ringRadius, Theme.WithAlpha(Swatch, (int)(90 * ring)));
            }

            Gfx.FillRounded(g, new RectangleF(cx - radius, cy - radius, radius * 2f, radius * 2f), radius, Swatch);

            if (_selectAmount > 0.05d)
            {
                float inner = radius * 0.46f;
                Gfx.FillRounded(g, new RectangleF(cx - inner, cy - inner, inner * 2f, inner * 2f), inner,
                    Theme.Mix(Color.Transparent, Theme.OnAccent, _selectAmount));
            }

            Font nameFont = Theme.Font(-1.6f, _selected ? FontStyle.Bold : FontStyle.Regular);
            string label = Name + (Selected ? " ✓" : string.Empty);
            PaintTextWithFallback(g, label, nameFont,
                _selected ? Theme.AccentText : Theme.TextSecondary,
                new RectangleF(0f, cy + radius + Theme.S(4), Width, Theme.SF(20f)));
        }

        private static void PaintTextWithFallback(Graphics g, string text, Font font, Color color, RectangleF bounds)
        {
            Gfx.DrawText(g, text, font, color, bounds, Typography.SingleLineCenter);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (Enabled && e.Button == MouseButtons.Left)
            {
                StartRipple(new Point(Width / 2, (int)Theme.S(24)), new RectangleF(0, 0, Width, Height), Theme.RadiusSmall);
                PerformClick();
            }
        }

        /// <summary>以编程方式触发一次点击（等价于鼠标左键点击）。</summary>
        public void PerformClick()
        {
            OnClick(EventArgs.Empty);
        }
    }
}
