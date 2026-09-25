using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace ClassTell
{
    /// <summary>圆角 / 投影 / 文本绘制辅助。</summary>
    internal static class Gfx
    {
        public static GraphicsPath RoundedPath(RectangleF r, float radius)
        {
            var path = new GraphicsPath();
            float rad = Math.Max(0f, Math.Min(radius, Math.Min(r.Width, r.Height) / 2f));
            if (rad <= 0.5f)
            {
                path.AddRectangle(r);
                return path;
            }
            float d = rad * 2f;
            path.AddArc(r.X, r.Y, d, d, 180f, 90f);
            path.AddArc(r.Right - d, r.Y, d, d, 270f, 90f);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0f, 90f);
            path.AddArc(r.X, r.Bottom - d, d, d, 90f, 90f);
            path.CloseFigure();
            return path;
        }

        public static void FillRounded(Graphics g, RectangleF r, float radius, Color color)
        {
            if (r.Width <= 0f || r.Height <= 0f || color.A == 0) return;
            using (var path = RoundedPath(r, radius))
            using (var brush = new SolidBrush(color))
                g.FillPath(brush, path);
        }

        public static void FillRounded(Graphics g, Rectangle r, int radius, Color color)
        {
            FillRounded(g, new RectangleF(r.X, r.Y, r.Width, r.Height), radius, color);
        }

        public static void StrokeRounded(Graphics g, RectangleF r, float radius, Color color, float thickness)
        {
            if (r.Width <= 0f || r.Height <= 0f) return;
            float inset = thickness / 2f;
            var rr = new RectangleF(r.X + inset, r.Y + inset, Math.Max(0f, r.Width - thickness), Math.Max(0f, r.Height - thickness));
            using (var path = RoundedPath(rr, radius))
            using (var pen = new Pen(color, thickness))
            {
                pen.Alignment = PenAlignment.Center;
                g.DrawPath(pen, path);
            }
        }

        public static void StrokeRounded(Graphics g, Rectangle r, int radius, Color color, float thickness)
        {
            StrokeRounded(g, new RectangleF(r.X, r.Y, r.Width, r.Height), radius, color, thickness);
        }

        /// <summary>Material 风格柔和投影（多层半透明圆角矩形近似；浅色模式自动减弱）。</summary>
        public static void DrawSoftShadow(Graphics g, RectangleF r, float radius, double strength)
        {
            strength *= Theme.ShadowScale;
            if (strength <= 0.01) return;
            const int layers = 5;
            for (int i = layers; i >= 1; i--)
            {
                double f = i / (double)layers;
                int alpha = (int)Math.Round(24 * strength * (1.35 - f));
                if (alpha <= 1) continue;
                float grow = i * 1.5f * (float)Theme.DpiScale;
                var rr = new RectangleF(r.X - grow, r.Y - grow + 1.0f * (float)Theme.DpiScale, r.Width + grow * 2f, r.Height + grow * 2f);
                FillRounded(g, rr, radius + grow, Color.FromArgb(Math.Min(48, alpha), 0, 0, 0));
            }
        }

        /// <summary>绘制图标字体（Segoe MDL2 Assets）字形。align 为 Near 时按内容宽度左对齐。</summary>
        public static void DrawGlyph(Graphics g, string glyph, Font font, Color color, Rectangle bounds, StringAlignment align)
        {
            if (string.IsNullOrEmpty(glyph)) return;
            using (var fmt = new StringFormat())
            using (var brush = new SolidBrush(color))
            {
                fmt.Alignment = align;
                fmt.LineAlignment = StringAlignment.Center;
                fmt.FormatFlags = StringFormatFlags.NoWrap;
                fmt.Trimming = StringTrimming.None;
                var rect = new RectangleF(bounds.X, bounds.Y, bounds.Width, bounds.Height);
                if (align == StringAlignment.Near)
                {
                    SizeF sz = g.MeasureString(glyph, font, new SizeF(1000f, 1000f), fmt);
                    rect = new RectangleF(bounds.X, bounds.Y, Math.Min(bounds.Width, Math.Max(1f, sz.Width + 2f)), bounds.Height);
                }
                g.DrawString(glyph, font, brush, rect, fmt);
            }
        }

        public static void DrawText(Graphics g, string text, Font font, Color color, RectangleF bounds, StringFormat format)
        {
            if (string.IsNullOrEmpty(text)) return;
            using (var brush = new SolidBrush(color))
                g.DrawString(text, font, brush, bounds, format);
        }

        public static SizeF Measure(Graphics g, string text, Font font, float maxWidth, StringFormat format)
        {
            if (string.IsNullOrEmpty(text)) return SizeF.Empty;
            return g.MeasureString(text, font, new SizeF(Math.Max(1f, maxWidth), 100000f), format);
        }

        /// <summary>开启抗锯齿与高质量文本渲染。</summary>
        public static void Smooth(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        }
    }
}
