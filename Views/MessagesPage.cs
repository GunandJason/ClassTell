using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace ClassTell
{
    /// <summary>
    /// 消息页：以“田字”（2×2）网格展示邮件，支持前后翻页；软件重启后消息清空。
    /// 顶部显示连接状态与刷新按钮，底部显示翻页控件。
    /// </summary>
    internal sealed class MessagesPage : MotionControl
    {
        public const int PageSize = 4;

        private readonly List<MessageItem> _items = new List<MessageItem>();
        private readonly MessageCard[] _cards = new MessageCard[PageSize];
        private readonly MaterialButton _prevBtn;
        private readonly MaterialButton _nextBtn;
        private readonly MaterialButton _refreshBtn;
        private readonly StatusChip _statusChip;
        private readonly Spinner _spinner;
        private readonly EmptyState _empty;
        private readonly MessageDetailPanel _detail;
        private readonly string _header;
        private readonly string _emptySubtitle;
        private readonly bool _showMailStatus;
        private int _page;
        private double _pageShift;

        /// <summary>「消息」页（默认）：带邮箱连接状态与“立即收取”。</summary>
        public MessagesPage()
            : this("消息", "登录后会实时收取指令邮件；每页 4 条，重启软件后消息清空",
                   "暂无消息", "等待指令邮件…", true)
        {
        }

        /// <summary>可配置的邮件列表页（用于「消息」与「过期」两个分项）。</summary>
        public MessagesPage(string header, string emptySubtitle, string emptyTitle, string emptyHint, bool showMailStatus)
        {
            _header = header;
            _emptySubtitle = emptySubtitle;
            _showMailStatus = showMailStatus;
            BackColor = Theme.Window;

            for (int i = 0; i < PageSize; i++)
            {
                var card = new MessageCard { Visible = false };
                card.Click += CardClicked;
                _cards[i] = card;
                Controls.Add(card);
            }

            _empty = new EmptyState { Title = emptyTitle, Hint = emptyHint };
            Controls.Add(_empty);

            _statusChip = new StatusChip();
            Controls.Add(_statusChip);

            _spinner = new Spinner { Visible = false };
            Controls.Add(_spinner);

            _prevBtn = new MaterialButton { Kind = ButtonKind.Outlined, Text = "<", Bold = true, MinWidth = Theme.S(40) };
            _prevBtn.Click += (s, e) => ChangePage(_page - 1);
            Controls.Add(_prevBtn);

            _nextBtn = new MaterialButton { Kind = ButtonKind.Outlined, Text = ">", Bold = true, MinWidth = Theme.S(40) };
            _nextBtn.Click += (s, e) => ChangePage(_page + 1);
            Controls.Add(_nextBtn);

            _refreshBtn = new MaterialButton { Kind = ButtonKind.Text, Text = "立即收取", Glyph = Theme.IconOr("\uE72C", ""), Compact = true };
            _refreshBtn.Click += (s, e) =>
            {
                Action handler = RefreshRequested;
                if (handler != null) handler();
            };
            Controls.Add(_refreshBtn);

            _detail = new MessageDetailPanel();
            _detail.CloseRequested += CloseDetail;
            Controls.Add(_detail);

            // 「过期」页不需要邮箱状态与“立即收取”（只在「消息」页显示）
            if (!_showMailStatus)
            {
                _statusChip.Visible = false;
                _spinner.Visible = false;
                _refreshBtn.Visible = false;
            }

            UpdatePagerButtons();
        }

        public event Action RefreshRequested;

        /// <summary>页面自带底色，需要跟随深浅模式。</summary>
        protected override Color ThemeBackColor { get { return Theme.Window; } }

        public int MessageCount { get { return _items.Count; } }

        /// <summary>本页是否显示邮箱连接状态与“立即收取”（「消息」页为 true，「过期」页为 false）。</summary>
        internal bool ShowsMailStatus { get { return _showMailStatus; } }
        public bool IsDetailOpen { get { return _detail.Visible; } }
        public int PageCount { get { return Math.Max(1, (int)Math.Ceiling(_items.Count / (double)PageSize)); } }

        // ---------- 数据 ----------
        public void AddMessage(MessageItem item)
        {
            if (item == null) return;
            _items.Insert(0, item);
            if (_page >= PageCount) _page = PageCount - 1;

            LayoutChildren();
            RefreshCards();
        }

        public void ClearMessages()
        {
            _items.Clear();
            _page = 0;
            LayoutChildren();
            RefreshCards();
        }

        public void SetStatus(MailState state, string message)
        {
            if (!_showMailStatus) return;
            Color color;
            switch (state)
            {
                case MailState.Listening: color = Theme.Success; break;
                case MailState.Error: color = Theme.Error; break;
                case MailState.AuthRequired: color = Theme.Warning; break;
                case MailState.Stopped: color = Theme.TextSecondary; break;
                default: color = Theme.Warning; break;
            }
            _statusChip.ChipText = message;
            _statusChip.DotColor = color;
            _statusChip.SetPulsing(state == MailState.Connecting || state == MailState.Authenticating ||
                                   state == MailState.Syncing || state == MailState.Listening);
            LayoutChildren();
        }

        public void SetBusy(bool busy)
        {
            if (!_showMailStatus) return;
            _spinner.Visible = busy;
            _refreshBtn.Enabled = !busy;
            if (busy) _spinner.BringToFront();
            LayoutChildren();
        }

        /// <summary>在页内显示某条消息的完整内容（不使用弹窗/浮层）。</summary>
        public void ShowDetail(MessageItem item)
        {
            if (item == null) return;
            _detail.SetBounds(0, HeaderHeight, Math.Max(1, ClientSize.Width), Math.Max(1, ClientSize.Height - HeaderHeight - FooterHeight));
            _detail.ShowItem(item);
            SetGridVisible(false);
        }

        /// <summary>返回消息列表。</summary>
        public void CloseDetail()
        {
            if (!_detail.Visible) return;
            _detail.Clear();
            SetGridVisible(true);
            RefreshCards();
        }

        /// <summary>切换“田字格列表 / 页内详情”的显隐。</summary>
        private void SetGridVisible(bool visible)
        {
            foreach (MessageCard card in _cards) card.Visible = visible && card.Item != null;
            _prevBtn.Visible = visible;
            _nextBtn.Visible = visible;
            _empty.Visible = visible && _items.Count == 0;
            Invalidate();
        }

        // ---------- 分页 ----------
        private void ChangePage(int page)
        {
            int target = Math.Max(0, Math.Min(PageCount - 1, page));
            if (target == _page) return;
            bool forward = target > _page;
            _page = target;
            RefreshCards();
            Animator.Tween(forward ? Theme.SF(26f) : -Theme.SF(26f), 0d, 300, Ease.EmphasizedDecelerate, v =>
            {
                _pageShift = v;
                ApplyCardBounds();
            });
            UpdatePagerButtons();
        }

        private void UpdatePagerButtons()
        {
            _prevBtn.Enabled = _page > 0;
            _nextBtn.Enabled = _items.Count > PageSize && _page < PageCount - 1;
            Invalidate();
        }

        private void CardClicked(object sender, EventArgs e)
        {
            var card = sender as MessageCard;
            if (card != null && card.Item != null) ShowDetail(card.Item);
        }

        private void RefreshCards()
        {
            int start = _page * PageSize;
            bool grid = !_detail.Visible;
            for (int i = 0; i < PageSize; i++)
            {
                int index = start + i;
                MessageItem item = index < _items.Count ? _items[index] : null;
                _cards[i].Item = item;
                _cards[i].Visible = grid && item != null;
            }
            _empty.Visible = grid && _items.Count == 0;
            LayoutChildren();
            UpdatePagerButtons();
        }

        // ---------- 布局（全部按字体度量推导，字号变大时自动让出空间） ----------
        /// <summary>页头与卡片网格之间的留白（把消息卡片整体下移一点，避免贴顶）。</summary>
        internal int GridTopGap { get { return Theme.S(14); } }

        /// <summary>网格起点（供自测校验卡片下移）。</summary>
        internal int HeaderHeightForTest { get { return HeaderHeight; } }

        private int HeaderHeight
        {
            get
            {
                float titleLine = Theme.LineHeight(Theme.Font(6f, FontStyle.Bold));
                float subLine = Theme.LineHeight(Theme.Font(-1.2f, FontStyle.Regular));
                return Theme.S(14) + (int)Math.Ceiling(titleLine) + Theme.S(4) + (int)Math.Ceiling(subLine)
                     + Theme.S(12) + GridTopGap;
            }
        }

        private int PagerHeight
        {
            get { return Math.Max(Theme.S(40), (int)Math.Ceiling(Theme.LineHeight(Theme.Body)) + Theme.S(12)); }
        }

        /// <summary>页脚高度（供自测定位探针）。</summary>
        internal int FooterHeightForTest { get { return FooterHeight; } }

        private int FooterHeight
        {
            get { return Theme.S(10) + PagerHeight + Theme.S(12); }
        }

        private Rectangle _pageLabelRect;

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutChildren();
        }

        protected override void OnThemeChanged()
        {
            LayoutChildren();
        }

        private void LayoutChildren()
        {
            int w = ClientSize.Width;
            int h = ClientSize.Height;
            if (w <= Theme.S(160) || h <= Theme.S(160)) return;

            int pad = Theme.PagePadding;
            int headerTop = Theme.S(12);
            if (_showMailStatus)
            {
                _refreshBtn.FitToContent();
                int controlH = Math.Max(Math.Max(Theme.S(36), _refreshBtn.Height), (int)Math.Ceiling(Theme.LineHeight(Theme.Body)) + Theme.S(10));

                _refreshBtn.SetBounds(w - pad - _refreshBtn.Width, headerTop, _refreshBtn.Width, controlH);
                int chipW = Math.Max(Theme.S(130), Math.Min(Theme.S(230), w / 4));
                int chipX = Math.Max(pad + Theme.S(150), _refreshBtn.Left - Theme.S(12) - chipW);
                int chipH = Math.Max(Theme.S(26), (int)Math.Ceiling(Theme.LineHeight(Theme.Font(-1.2f, FontStyle.Regular))) + Theme.S(8));
                _statusChip.SetBounds(chipX, headerTop + (controlH - chipH) / 2, chipW, chipH);
                _spinner.SetBounds(chipX - Theme.S(24), headerTop + (controlH - Theme.S(18)) / 2, Theme.S(18), Theme.S(18));
            }

            int gridTop = HeaderHeight;
            int gridH = Math.Max(Theme.S(80), h - gridTop - FooterHeight);
            int gridW = Math.Max(Theme.S(120), w - pad * 2);
            _empty.SetBounds(pad, gridTop, gridW, gridH);

            ApplyCardBounds();

            // 详情浮层必须覆盖整个页面，才能形成“遮罩 + 居中卡片”的层次
            _detail.SetBounds(0, 0, Math.Max(1, w), Math.Max(1, h));

            int footerTop = h - FooterHeight + Theme.S(10);
            int labelW = Theme.S(112);
            _nextBtn.SetBounds(w - pad - _nextBtn.Width, footerTop, _nextBtn.Width, PagerHeight);
            _prevBtn.SetBounds(_nextBtn.Left - labelW - _prevBtn.Width, footerTop, _prevBtn.Width, PagerHeight);
            _pageLabelRect = new Rectangle(_prevBtn.Right, footerTop, labelW, PagerHeight);

            if (_detail.Visible)
                _detail.SetBounds(0, HeaderHeight, Math.Max(1, w), Math.Max(1, h - HeaderHeight - FooterHeight));
        }

        private void ApplyCardBounds()
        {
            int w = ClientSize.Width;
            int h = ClientSize.Height;
            if (w <= Theme.S(160) || h <= Theme.S(160)) return;

            int pad = Theme.PagePadding;
            int gap = Theme.Gap;
            int gridW = Math.Max(Theme.S(120), w - pad * 2);
            int gridH = Math.Max(Theme.S(80), h - HeaderHeight - FooterHeight);
            int minCardW = Math.Max(Theme.S(140), (int)Math.Ceiling(Theme.MeasureLine("来源于：teacher@example.com", Theme.Font(-1.2f, FontStyle.Regular))) + Theme.S(70));
            int minCardH = (int)Math.Ceiling(Theme.LineHeight(Theme.Font(3f, FontStyle.Bold)) + Theme.LineHeight(Theme.Body) * 3f) + Theme.S(60);
            int cw = Math.Max(Math.Min(minCardW, gridW), (gridW - gap) / 2);
            int ch = Math.Max(Math.Min(minCardH, gridH), (gridH - gap) / 2);

            for (int i = 0; i < PageSize; i++)
            {
                int col = i % 2;
                int row = i / 2;
                int x = pad + col * (cw + gap) + (int)Math.Round(_pageShift * (0.55d + i * 0.16d));
                int y = HeaderHeight + row * (ch + gap);
                _cards[i].SetBounds(x, y, cw, ch);
            }
        }

        // ---------- 绘制 ----------
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Gfx.Smooth(g);
            g.Clear(Theme.Window);

            int pad = Theme.PagePadding;
            float titleLine = Theme.LineHeight(Theme.Font(6f, FontStyle.Bold));
            float subLine = Theme.LineHeight(Theme.Font(-1.2f, FontStyle.Regular));
            float textWidth = Math.Max(Theme.S(120), ClientSize.Width - pad * 2 - Theme.S(280));

            Font titleFont = Theme.Font(6f, FontStyle.Bold);
            Gfx.DrawText(g, _header, titleFont, Theme.TextPrimary,
                new RectangleF(pad, Theme.S(14), textWidth, titleLine), Typography.SingleLine);

            Font subFont = Theme.Font(-1.2f, FontStyle.Regular);
            string subtitle = _items.Count == 0
                ? _emptySubtitle
                : string.Format("已接收 {0} 条 · 第 {1} / {2} 页 · 每页 4 条", _items.Count, _page + 1, PageCount);
            Gfx.DrawText(g, subtitle, subFont, Theme.TextSecondary,
                new RectangleF(pad, Theme.S(14) + titleLine + Theme.S(4), textWidth, subLine), Typography.SingleLine);

            Font labelFont = Theme.Font(-0.8f, FontStyle.Regular);
            Gfx.DrawText(g, string.Format("第 {0} / {1} 页", _page + 1, PageCount), labelFont, Theme.TextSecondary, _pageLabelRect, Typography.SingleLineCenter);

            if (_items.Count > 0)
            {
                float hintWidth = Math.Max(Theme.S(80), ClientSize.Width - pad * 2 - (_nextBtn.Width * 2 + _pageLabelRect.Width + Theme.S(24)));
                Gfx.DrawText(g, "提示：点击卡片可查看完整正文", subFont, Theme.TextSecondary,
                    new RectangleF(pad, _pageLabelRect.Y, hintWidth, _pageLabelRect.Height), Typography.SingleLine);
            }
        }
    }
}
