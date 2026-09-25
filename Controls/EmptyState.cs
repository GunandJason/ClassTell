using System;
using System.Drawing;
using System.Windows.Forms;

namespace ClassTell
{
    /// <summary>空状态：圆形图标 + 标题 + 说明文字，居中显示。</summary>
    internal sealed class EmptyState : MotionControl
    {
        public EmptyState()
        {
            Glyph = "\uE715";
            Title = "暂无消息";
            Hint = "等待指令邮件…";
        }

        public string Glyph { get; set; }
        public string Title { get; set; }
        public string Hint { get; set; }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Gfx.Smooth(g);

            float badge = Theme.SF(72f);
            float cx = Width / 2f;
            float totalHeight = badge + Theme.S(58) + Theme.S(22);
            float top = Math.Max(Theme.S(10), (Height - totalHeight) / 2f);

            var badgeRect = new RectangleF(cx - badge / 2f, top, badge, badge);
            Gfx.FillRounded(g, badgeRect, badge / 2f, Theme.AccentSoft);
            Gfx.StrokeRounded(g, badgeRect, badge / 2f, Theme.AccentSoftStrong, 1f);
            Theme.DrawIcon(g, Glyph, "✉", 8f, Theme.AccentText,
                new Rectangle((int)badgeRect.X, (int)badgeRect.Y, (int)badgeRect.Width, (int)badgeRect.Height),
                StringAlignment.Center);

            float y = badgeRect.Bottom + Theme.S(18);
            Font titleFont = Theme.Font(2f, FontStyle.Bold);
            Gfx.DrawText(g, Title, titleFont, Theme.TextPrimary,
                new RectangleF(Theme.S(20), y, Math.Max(1f, Width - Theme.S(40)), Theme.SF(28f)),
                Typography.SingleLineCenter);

            y += Theme.SF(30f);
            Font hintFont = Theme.Font(-0.5f, FontStyle.Regular);
            Gfx.DrawText(g, Hint, hintFont, Theme.TextSecondary,
                new RectangleF(Theme.S(20), y, Math.Max(1f, Width - Theme.S(40)), Theme.SF(22f)),
                Typography.SingleLineCenter);
        }
    }
}
