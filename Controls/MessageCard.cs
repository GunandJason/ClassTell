using System;
using System.Drawing;
using System.Windows.Forms;

namespace ClassTell
{
    /// <summary>
    /// 消息卡片（消息界面“田字”网格中的一格）。
    /// 所有行按字体度量自上而下依次排布，正文占据剩余空间并用省略号收尾，
    /// 因此字体调到最大也不会出现文字被裁切或互相重叠。
    /// </summary>
    internal sealed class MessageCard : MotionControl
    {
        private MessageItem _item;
        private double _lift;

        public MessageCard()
        {
            Cursor = Cursors.Hand;
            BackColor = Color.Transparent;
        }

        public MessageItem Item
        {
            get { return _item; }
            set { _item = value; Invalidate(); }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            Animator.Tween(_lift, 1d, 200, Ease.EmphasizedDecelerate, v => { _lift = v; Invalidate(); });
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            Animator.Tween(_lift, 0d, 240, Ease.Standard, v => { _lift = v; Invalidate(); });
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (Enabled && e.Button == MouseButtons.Left)
                StartRipple(e.Location, CardRect, Theme.RadiusCard);
        }

        private RectangleF CardRect
        {
            get
            {
                float lift = (float)(_lift * Theme.SF(2f));
                return new RectangleF(Theme.S(5), Theme.S(8) - lift, Width - Theme.S(10), Height - Theme.S(18));
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Gfx.Smooth(g);
            if (_item == null) return;

            RectangleF r = CardRect;
            float radius = Theme.RadiusCard;
            bool call = _item.IsCall;

            Gfx.DrawSoftShadow(g, r, radius, 0.55 + _lift * 0.85);
            Gfx.FillRounded(g, r, radius, Theme.Mix(Theme.Surface, Theme.SurfaceAlt, _lift * 0.5));
            Gfx.StrokeRounded(g, r, radius,
                Theme.Mix(Theme.Outline, Theme.Accent, (call ? 0.45 : 0.0) + _lift * 0.45), 1f);

            float pad = Theme.S(16);
            float x = r.X + pad;
            float width = Math.Max(Theme.S(40), r.Width - pad * 2f);
            float y = r.Y + Theme.S(12);

            Font chipFont = Theme.Font(-1.6f, FontStyle.Bold);
            Font timeFont = Theme.Font(-1.2f, FontStyle.Regular);
            Font titleFont = Theme.Font(3f, FontStyle.Bold);
            Font sourceFont = Theme.Font(-1.2f, FontStyle.Regular);

            // 第 1 行：命令角标 + 时间
            float chipH = Math.Max(Theme.SF(20f), Theme.LineHeight(chipFont));
            string chipText = call ? "呼叫  C" : "提示  T";
            float chipW = Math.Min(width * 0.55f, Theme.MeasureLine(chipText, chipFont) + Theme.S(16));
            var chipRect = new RectangleF(x, y, Math.Max(Theme.S(40), chipW), chipH);
            Gfx.FillRounded(g, chipRect, chipH / 2f, call ? Theme.AccentSoftStrong : Theme.SurfaceAlt);
            Gfx.DrawText(g, chipText, chipFont, call ? Theme.AccentText : Theme.TextSecondary, chipRect, Typography.SingleLineCenter);

            float timeWidth = Math.Max(Theme.S(56), width * 0.45f);
            Gfx.DrawText(g, _item.ReceivedText, timeFont, Theme.TextSecondary,
                new RectangleF(r.Right - pad - timeWidth, chipRect.Y, timeWidth, chipH),
                new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter });

            y = chipRect.Bottom + Theme.S(8);

            // 底部来源行（自下而上定位，确保永远在卡片内部）
            float sourceLine = Theme.LineHeight(sourceFont);
            float dividerY = r.Bottom - Theme.S(12) - sourceLine - Theme.S(6);
            float titleLine = Theme.LineHeight(titleFont);

            // 第 2 行：标题（单行省略，高度受限时自动压缩）
            float titleHeight = Math.Max(1f, Math.Min(titleLine, dividerY - y - Theme.S(4)));
            var titleRect = new RectangleF(x, y, Math.Max(Theme.S(30), width - Theme.S(10)), titleHeight);
            Gfx.DrawText(g, string.IsNullOrEmpty(_item.Title) ? "（无标题）" : _item.Title, titleFont, Theme.TextPrimary, titleRect, Typography.SingleLine);
            y = titleRect.Bottom + Theme.S(4);

            // 正文：占据标题与来源行之间的剩余空间
            float bodyBottom = dividerY - Theme.S(6);
            float bodyHeight = bodyBottom - y;
            float bodyLine = Theme.LineHeight(Theme.Body);
            var bodyRect = new RectangleF(x, y, width - Theme.S(6), Math.Max(0f, bodyHeight));
            if (bodyHeight > bodyLine * 0.55f)
            {
                Gfx.DrawText(g, _item.Body, Theme.Body, Theme.Mix(Theme.TextPrimary, Theme.TextSecondary, 0.35),
                    bodyRect, Typography.WrapEllipsis);
            }

            // 分隔线 + 来源 + 查看箭头
            using (var pen = new Pen(Theme.Outline, 1f))
                g.DrawLine(pen, x, dividerY, r.Right - pad, dividerY);

            float arrowSpace = Theme.S(22);
            Gfx.DrawText(g, "来源于：" + (_item.Source ?? string.Empty), sourceFont, Theme.TextSecondary,
                new RectangleF(x, dividerY + Theme.S(4), Math.Max(Theme.S(30), width - arrowSpace), sourceLine),
                Typography.SingleLine);

            Theme.DrawIcon(g, "\uE76C", "›", -0.5f, Theme.Mix(Theme.TextSecondary, Theme.AccentText, Math.Max(0.25, _lift)),
                new Rectangle((int)Math.Round(r.Right - pad - Theme.S(20)), (int)Math.Round(dividerY + Theme.S(3)), Theme.S(22), (int)Math.Round(sourceLine)),
                StringAlignment.Center);

            PaintRipples(g, r, radius, Theme.RippleColor);
        }
    }
}
