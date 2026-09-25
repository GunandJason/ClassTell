using System;
using System.Drawing;
using System.Windows.Forms;

namespace ClassTell
{
    /// <summary>
    /// 消息详情（页内视图，不是弹窗）：点击卡片后在本页内容区直接显示完整标题与正文，
    /// 不使用遮罩或浮层窗口；左上角有“返回列表”按钮，Esc 也可返回。
    /// </summary>
    internal sealed class MessageDetailPanel : MotionControl
    {
        private MessageItem _item;
        private double _reveal;
        private double _scroll;
        private double _scrollTarget;
        private float _contentHeight;
        private RectangleF _bodyRect;
        private readonly MaterialButton _backBtn;

        public MessageDetailPanel()
        {
            Visible = false;
            _backBtn = new MaterialButton { Kind = ButtonKind.Text, Text = "‹ 返回列表", Compact = true };
            _backBtn.Click += (s, e) => RaiseClose();
            Controls.Add(_backBtn);
        }

        /// <summary>请求关闭详情（返回列表）。</summary>
        public event Action CloseRequested;

        public MessageItem Item { get { return _item; } }

        public bool HasItem { get { return _item != null; } }

        /// <summary>显示某条消息的完整内容。</summary>
        public void ShowItem(MessageItem item)
        {
            if (item == null) return;
            _item = item;
            _scroll = 0d;
            _scrollTarget = 0d;
            _reveal = 0d;
            Visible = true;
            BringToFront();
            LayoutBackButton();
            Animator.Run(220, Ease.EmphasizedDecelerate, p =>
            {
                _reveal = p;
                Invalidate();
            });
        }

        /// <summary>清空并隐藏（返回列表用）。</summary>
        public void Clear()
        {
            _item = null;
            _scroll = 0d;
            _scrollTarget = 0d;
            _reveal = 0d;
            Visible = false;
        }

        private void RaiseClose()
        {
            Action handler = CloseRequested;
            if (handler != null) handler();
        }

        // ---------- 布局 ----------
        private RectangleF CardRect
        {
            get
            {
                float pad = Theme.PagePadding;
                return new RectangleF(pad, Theme.S(10), Math.Max(Theme.S(160), Width - pad * 2), Math.Max(Theme.S(120), Height - Theme.S(20)));
            }
        }

        private void LayoutBackButton()
        {
            RectangleF card = CardRect;
            _backBtn.FitToContent();
            _backBtn.SetBounds((int)card.Right - _backBtn.Width - Theme.S(10), (int)card.Y + Theme.S(12), _backBtn.Width, _backBtn.Height);
        }

        private float BodyTop { get { return CardRect.Y + Theme.S(104); } }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutBackButton();
        }

        protected override void OnThemeChanged()
        {
            base.OnThemeChanged();
            LayoutBackButton();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            float max = Math.Max(0f, _contentHeight - Math.Max(Theme.SF(40f), _bodyRect.Height));
            if (max <= 0f) return;
            _scrollTarget = Math.Max(0d, Math.Min(max, _scrollTarget - Math.Sign(e.Delta) * Theme.SF(90f)));
            double from = _scroll;
            double to = _scrollTarget;
            Animator.Tween(from, to, 200, Ease.Decelerate, v => { _scroll = v; Invalidate(); });
        }

        // ---------- 绘制（页内排版，无遮罩） ----------
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Gfx.Smooth(g);
            g.Clear(Theme.Window);
            if (_item == null) return;

            RectangleF card = CardRect;
            float radius = Theme.RadiusCard;
            float slide = (float)((1d - _reveal) * Theme.SF(10f));

            Gfx.DrawSoftShadow(g, card, radius, 0.7);
            Gfx.FillRounded(g, card, radius, Theme.Surface);
            Gfx.StrokeRounded(g, card, radius, Theme.Outline, 1f);

            float pad = Theme.S(26);
            float x = card.X + pad;
            float width = Math.Max(Theme.S(80), card.Width - pad * 2f - _backBtn.Width - Theme.S(14));

            Font titleFont = Theme.Font(4f, FontStyle.Bold);
            string title = string.IsNullOrEmpty(_item.Title) ? "（无标题）" : _item.Title;
            float titleLine = Theme.LineHeight(titleFont);
            var titleRect = new RectangleF(x, card.Y + Theme.S(16) + slide, width, titleLine * 2f);
            Gfx.DrawText(g, title, titleFont, Theme.TextPrimary, titleRect, Typography.WrapEllipsis);

            float metaY = titleRect.Bottom + Theme.S(8);
            Font chipFont = Theme.Font(-1.6f, FontStyle.Bold);
            float chipH = Math.Max(Theme.SF(20f), Theme.LineHeight(chipFont));
            string chipText = _item.IsCall ? "呼叫  C" : "提示  T";
            float chipW = Theme.MeasureLine(chipText, chipFont) + Theme.S(16);
            var chipRect = new RectangleF(x, metaY, chipW, chipH);
            Gfx.FillRounded(g, chipRect, chipH / 2f, _item.IsCall ? Theme.AccentSoftStrong : Theme.SurfaceAlt);
            Gfx.DrawText(g, chipText, chipFont, _item.IsCall ? Theme.AccentText : Theme.TextSecondary, chipRect, Typography.SingleLineCenter);

            Font metaFont = Theme.Font(-1f, FontStyle.Regular);
            float metaX = chipRect.Right + Theme.S(12);
            Gfx.DrawText(g, "来源于：" + _item.SourceText + "   ·   " + _item.ReceivedText, metaFont, Theme.TextSecondary,
                new RectangleF(metaX, metaY, Math.Max(Theme.S(40), card.Right - pad - metaX), chipH), Typography.SingleLine);

            float dividerY = metaY + chipH + Theme.S(10);
            using (var pen = new Pen(Theme.Outline, 1f))
                g.DrawLine(pen, x, dividerY, card.Right - pad, dividerY);

            Font bodyFont = Theme.Body;
            bool hasBody = !string.IsNullOrEmpty(_item.Body);
            string body = hasBody ? _item.Body : "（无正文内容）";
            float bodyWidth = Math.Max(Theme.S(60), card.Width - pad * 2f - Theme.S(10));
            SizeF bodySize = Gfx.Measure(g, body, bodyFont, bodyWidth, Typography.Wrap);
            _contentHeight = bodySize.Height;

            float bodyTop = Math.Max(BodyTop, dividerY + Theme.S(12));
            _bodyRect = new RectangleF(x, bodyTop, bodyWidth, Math.Max(Theme.SF(40f), card.Bottom - Theme.S(26) - bodyTop));
            float max = Math.Max(0f, _contentHeight - _bodyRect.Height);
            if (_scroll > max) _scroll = max;
            if (_scrollTarget > max) _scrollTarget = max;

            System.Drawing.Region old = g.Clip.Clone();
            g.SetClip(_bodyRect, System.Drawing.Drawing2D.CombineMode.Intersect);
            Gfx.DrawText(g, body, bodyFont, hasBody ? Theme.TextPrimary : Theme.TextSecondary,
                new RectangleF(_bodyRect.X, _bodyRect.Y - (float)_scroll, _bodyRect.Width - Theme.S(8), Math.Max(_contentHeight, _bodyRect.Height)),
                Typography.Wrap);
            g.Clip = old;

            if (max > 0f)
            {
                float barW = Theme.S(4);
                float trackH = _bodyRect.Height;
                float thumbH = Math.Max(Theme.SF(28f), trackH * (trackH / Math.Max(1f, _contentHeight)));
                float travel = Math.Max(0f, trackH - thumbH);
                float thumbTop = _bodyRect.Y + (float)(_scroll / max * travel);
                Gfx.FillRounded(g, new RectangleF(card.Right - pad + Theme.S(2), thumbTop, barW, thumbH),
                    barW / 2f, Theme.Mix(Color.FromArgb(80, Theme.TextSecondary), Theme.Accent, 0.4));
            }

            Font hintFont = Theme.Font(-1.5f, FontStyle.Regular);
            Gfx.DrawText(g, "滚轮浏览全文 · Esc 返回列表", hintFont, Theme.TextSecondary,
                new RectangleF(x, card.Bottom - Theme.S(22), Math.Max(Theme.S(60), width), Theme.SF(18f)), Typography.SingleLine);
        }
    }
}
