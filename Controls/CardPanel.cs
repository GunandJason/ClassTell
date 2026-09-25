using System;
using System.Drawing;
using System.Windows.Forms;

namespace ClassTell
{
    /// <summary>圆角卡片容器：可带标题 / 副标题，子控件由外部按 HeaderHeight 定位。</summary>
    internal class CardPanel : MotionControl
    {
        public CardPanel()
        {
            Shadow = true;
        }

        /// <summary>是否绘制柔和投影。</summary>
        public bool Shadow { get; set; }

        /// <summary>卡片标题（可空）。</summary>
        public string CardTitle { get; set; }

        /// <summary>卡片标题下的小字说明（可空）。</summary>
        public string CardSubtitle { get; set; }

        /// <summary>是否显示左侧水蓝色竖条（用于强调分区）。</summary>
        public bool ShowAccentBar { get; set; }

        /// <summary>标题区占用的高度，子控件从这里往下排布。</summary>
        public int HeaderHeight
        {
            get { return Theme.S(64); }
        }

        /// <summary>卡片内容区的背景色（卡片内部为 Surface，供其子控件自绘背景使用）。</summary>
        public override Color ContentBackground { get { return Theme.Surface; } }

        protected override void OnPaint(PaintEventArgs e)
        {
            Gfx.Smooth(e.Graphics);
            var r = new RectangleF(Theme.S(10), Theme.S(8), Width - Theme.S(20), Height - Theme.S(16));
            float radius = Theme.RadiusCard;

            if (Shadow) Gfx.DrawSoftShadow(e.Graphics, r, radius, 1.0);
            Gfx.FillRounded(e.Graphics, r, radius, Theme.Surface);
            Gfx.StrokeRounded(e.Graphics, r, radius, Theme.Outline, 1f);

            if (ShowAccentBar)
            {
                float barHeight = r.Height - Theme.S(28);
                var bar = new RectangleF(r.X + Theme.S(1), r.Y + Theme.S(14), Theme.S(3), barHeight);
                Gfx.FillRounded(e.Graphics, bar, Theme.SF(1.5f), Theme.Accent);
            }

            if (!string.IsNullOrEmpty(CardTitle))
            {
                float textX = r.X + Theme.S(ShowAccentBar ? 20 : 18);
                float textWidth = Math.Max(1f, r.Width - Theme.S(36));
                Font titleFont = Theme.Font(1.5f, FontStyle.Bold);
                Gfx.DrawText(e.Graphics, CardTitle, titleFont, Theme.TextPrimary,
                    new RectangleF(textX, r.Y + Theme.S(14), textWidth, Theme.SF(26f)), Typography.SingleLine);

                if (!string.IsNullOrEmpty(CardSubtitle))
                {
                    Font subFont = Theme.Font(-1f, FontStyle.Regular);
                    Gfx.DrawText(e.Graphics, CardSubtitle, subFont, Theme.TextSecondary,
                        new RectangleF(textX, r.Y + Theme.S(15) + Theme.SF(25f), textWidth, Theme.SF(20f)), Typography.SingleLine);
                }
            }
        }
    }
}
