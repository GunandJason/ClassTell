using System;
using System.Drawing;
using System.Windows.Forms;

namespace ClassTell
{
    internal enum ButtonKind
    {
        /// <summary>水蓝色实心按钮。</summary>
        Filled,
        /// <summary>浅色底（Accent 淡色）按钮。</summary>
        Tonal,
        /// <summary>描边按钮。</summary>
        Outlined,
        /// <summary>纯文字按钮。</summary>
        Text
    }

    /// <summary>Material 风格按钮：水波纹 + 悬停/按压插值动画，可带图标字形。</summary>
    internal sealed class MaterialButton : MotionControl
    {
        public MaterialButton()
        {
            Cursor = Cursors.Hand;
            Size = new Size(Theme.S(120), Theme.S(38));
            Kind = ButtonKind.Tonal;
            TabStop = true;
            AutoSizeToContent = true;
        }

        public ButtonKind Kind { get; set; }

        /// <summary>Segoe MDL2 图标字形（可空）。</summary>
        public string Glyph { get; set; }

        /// <summary>紧凑模式（更小的内边距与字号）。</summary>
        public bool Compact { get; set; }

        /// <summary>字体大小偏移（相对全局字号）。</summary>
        public float TextDelta { get; set; }

        /// <summary>是否按内容自动调整宽度（默认开启，避免文字被裁切）。</summary>
        public bool AutoSizeToContent { get; set; }

        /// <summary>最小宽度（例如分页箭头要保持方形按钮）。</summary>
        public int MinWidth { get; set; }

        /// <summary>最小高度（由宿主按行高统一设置，避免布局与自适应互相打架）。</summary>
        public int MinHeight { get; set; }

        private int GapPx { get { return Theme.S(Compact ? 6 : 8); } }

        private int PadPx { get { return Theme.S(Compact ? 12 : 16); } }

        /// <summary>容纳图标 + 文字所需的宽度。</summary>
        public int IdealWidth
        {
            get
            {
                float icon = string.IsNullOrEmpty(Glyph) ? 0f : Theme.PointToPixel(Theme.FontSize + TextDelta + 0.5f) * 1.15f;
                float text = string.IsNullOrEmpty(Text) ? 0f : Theme.MeasureLine(Text, TextFont());
                float gap = (icon > 0f && text > 0f) ? GapPx : 0f;
                return (int)Math.Ceiling(icon + text + gap + PadPx * 2);
            }
        }

        /// <summary>容纳文字所需的行高。</summary>
        public int IdealHeight
        {
            get { return (int)Math.Ceiling(Theme.LineHeight(TextFont()) + Theme.S(Compact ? 10 : 14)); }
        }

        /// <summary>是否显示粗体文字（分页箭头用）。</summary>
        public bool Bold
        {
            get { return _bold; }
            set { _bold = value; Invalidate(); }
        }

        private bool _bold;

        private Font TextFont()
        {
            float delta = TextDelta + (Compact ? -0.5f : 0f);
            return Theme.Font(delta, (Kind == ButtonKind.Filled || _bold) ? FontStyle.Bold : FontStyle.Regular);
        }

        /// <summary>按内容调整尺寸（文字更长或字号变化后调用）。</summary>
        public void FitToContent()
        {
            if (!AutoSizeToContent) return;
            int w = Math.Max(Theme.S(IconOnlyMinWidth), Math.Max(MinWidth, IdealWidth));
            int h = Math.Max(Theme.S(30), Math.Max(MinHeight, IdealHeight));
            if (Width != w || Height != h) Size = new Size(w, h);
        }

        /// <summary>只有图标时的最小方形尺寸。</summary>
        private const int IconOnlyMinWidth = 40;

        protected override void OnCreateControl()
        {
            base.OnCreateControl();
            FitToContent();
        }

        protected override void OnThemeChanged()
        {
            FitToContent();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Gfx.Smooth(e.Graphics);
            var r = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            float radius = Theme.RadiusSmall;

            double hover = HoverAmount;
            double press = PressAmount;
            Color background = Color.Empty;
            Color border = Color.Empty;
            Color foreground;

            switch (Kind)
            {
                case ButtonKind.Filled:
                    background = Theme.Mix(Theme.Accent, Theme.AccentHover, hover);
                    background = Theme.Mix(background, Theme.AccentPressed, press * 0.8);
                    foreground = Theme.OnAccent;
                    break;
                case ButtonKind.Tonal:
                    background = Theme.Mix(Theme.AccentSoft, Theme.AccentSoftStrong, Math.Max(hover, press));
                    foreground = Theme.Mix(Theme.AccentText, Theme.Darken(Theme.AccentText, 0.15), hover);
                    break;
                case ButtonKind.Outlined:
                    background = Theme.Mix(Color.Transparent, Theme.HoverVeil, Math.Max(hover, press));
                    border = Theme.Mix(Theme.Outline, Theme.Accent, hover * 0.9);
                    foreground = Theme.Mix(Theme.TextPrimary, Theme.AccentText, hover);
                    break;
                default:
                    background = Theme.Mix(Color.Transparent, Theme.HoverVeil, Math.Max(hover, press));
                    border = Color.Empty;
                    foreground = Theme.Mix(Theme.TextSecondary, Theme.AccentText, Math.Max(hover, 0.45));
                    break;
            }

            if (background.A > 0) Gfx.FillRounded(e.Graphics, r, radius, background);
            if (border.A > 0) Gfx.StrokeRounded(e.Graphics, r, radius, border, 1f);
            if (!Enabled) foreground = Color.FromArgb(110, foreground);

            PaintContent(e.Graphics, r, foreground);

            Color ripple = Kind == ButtonKind.Filled ? Color.FromArgb(70, 0, 0, 0) : Theme.RippleColor;
            PaintRipples(e.Graphics, r, radius, ripple);
        }

        private void PaintContent(Graphics g, RectangleF r, Color foreground)
        {
            bool hasGlyph = !string.IsNullOrEmpty(Glyph);
            float textDelta = TextDelta + (Compact ? -0.5f : 0f);
            Font textFont = Theme.Font(textDelta, (Kind == ButtonKind.Filled || _bold) ? FontStyle.Bold : FontStyle.Regular);

            // 与 IdealWidth 使用同一套度量，保证“算出来的宽度”与“画出来的内容”一致
            float iconWidth = hasGlyph ? Theme.PointToPixel(Theme.FontSize + TextDelta + 0.5f) * 1.15f : 0f;
            float textWidth = string.IsNullOrEmpty(Text) ? 0f : Gfx.Measure(g, Text, textFont, r.Width * 4f, Typography.SingleLine).Width;
            float gap = (iconWidth > 0f && textWidth > 0f) ? GapPx : 0f;

            float total = iconWidth + textWidth + gap;
            bool fits = total <= r.Width - Theme.S(4);
            float x = r.X + Math.Max(0f, (r.Width - Math.Min(total, r.Width)) / 2f);

            if (hasGlyph)
            {
                Font iconFont = Theme.IconFont(textDelta + 0.5f);
                Gfx.DrawGlyph(g, Glyph, iconFont, foreground,
                    new Rectangle((int)Math.Round(x), (int)Math.Round(r.Y), (int)Math.Round(iconWidth), (int)Math.Round(r.Height)),
                    StringAlignment.Center);
                x += iconWidth + gap;
            }

            if (!string.IsNullOrEmpty(Text))
            {
                float w = Math.Max(1f, r.Right - Theme.S(fits ? 2 : 0) - x);
                // 内容确实放不下时才用省略号收尾（正常情况下 AutoSizeToContent 已保证放得下）
                Gfx.DrawText(g, Text, textFont, foreground, new RectangleF(x, r.Y, w, r.Height),
                    fits ? Typography.SingleLineCenter : Typography.SingleLineCenter);
            }
        }

        /// <summary>以编程方式触发一次点击（等价于鼠标左键点击）。</summary>
        public void PerformClick()
        {
            OnClick(EventArgs.Empty);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (Enabled && e.Button == MouseButtons.Left)
                StartRipple(e.Location, new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f), Theme.RadiusSmall);
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            if (!Enabled) return;
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)
            {
                OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, Width / 2, Height / 2, 0));
                OnClick(EventArgs.Empty);
                OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, Width / 2, Height / 2, 0));
            }
        }

        protected override bool IsInputKey(Keys keyData)
        {
            if (keyData == Keys.Space || keyData == Keys.Enter) return true;
            return base.IsInputKey(keyData);
        }
    }
}

