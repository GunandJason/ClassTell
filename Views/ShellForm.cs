using System;
using System.Drawing;
using System.Windows.Forms;

namespace ClassTell
{
    /// <summary>左侧导航栏：品牌区 + 导航项 + 底部账号/版本信息（使用系统窗口与标准控件，不再自绘标题栏）。</summary>
    internal sealed class NavRail : MotionControl
    {
        public NavRail()
        {
            BackColor = Theme.NavBackground;
        }

        /// <summary>导航栏自带底色，需要跟随深浅模式。</summary>
        protected override Color ThemeBackColor { get { return Theme.NavBackground; } }

        public string AccountText { get; set; }
        public string FooterText { get; set; }

        /// <summary>品牌区占用的高度（导航项从这里往下排）。</summary>
        public int HeaderHeight
        {
            get { return Theme.S(16) + Theme.S(30) + Theme.S(18); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Gfx.Smooth(g);
            g.Clear(Theme.NavBackground);

            using (var pen = new Pen(Theme.Outline, 1f))
                g.DrawLine(pen, Width - 1, 0, Width - 1, Height);

            int iconSize = Theme.S(30);
            using (Bitmap brand = IconFactory.CreateBrandBitmap(iconSize, true))
                g.DrawImage(brand, new Rectangle(Theme.S(18), Theme.S(14), iconSize, iconSize));

            float line1 = Theme.LineHeight(Theme.Font(2.5f, FontStyle.Bold));
            float line2 = Theme.LineHeight(Theme.Font(-1.5f, FontStyle.Regular));
            float textX = Theme.S(58);
            float textWidth = Math.Max(Theme.S(40), Width - textX - Theme.S(10));

            Gfx.DrawText(g, "ClassTell", Theme.Font(2.5f, FontStyle.Bold), Theme.TextPrimary,
                new RectangleF(textX, Theme.S(12), textWidth, line1), Typography.SingleLine);
            Gfx.DrawText(g, "邮件指令通知", Theme.Font(-1.5f, FontStyle.Regular), Theme.TextSecondary,
                new RectangleF(textX, Theme.S(12) + line1, textWidth, line2), Typography.SingleLine);

            // 底部信息（自下而上排列，避免与导航项重叠）
            float footerLine = Theme.LineHeight(Theme.Font(-1.3f, FontStyle.Regular));
            float versionLine = Theme.LineHeight(Theme.Font(-1.8f, FontStyle.Regular));
            string account = string.IsNullOrEmpty(AccountText) ? "未登录邮箱" : AccountText;
            Gfx.DrawText(g, account, Theme.Font(-1.3f, FontStyle.Regular),
                string.IsNullOrEmpty(AccountText) ? Theme.Warning : Theme.TextSecondary,
                new RectangleF(Theme.S(18), Height - Theme.S(14) - footerLine - versionLine - Theme.S(2), Width - Theme.S(36), footerLine),
                Typography.SingleLine);
            Gfx.DrawText(g, FooterText ?? string.Empty, Theme.Font(-1.8f, FontStyle.Regular), Theme.TextSecondary,
                new RectangleF(Theme.S(18), Height - Theme.S(14) - versionLine, Width - Theme.S(36), versionLine),
                Typography.SingleLine);
        }
    }

    /// <summary>
    /// 主窗口：使用系统标准窗口组件（原生标题栏、最小化/最大化/关闭、系统拖动与缩放），
    /// 客户区内是左侧导航 + 内容区；主题切换时同步系统标题栏的深/浅外观。
    /// </summary>
    internal sealed class ShellForm : Form
    {
        private readonly AuthService _auth = new AuthService();
        private IMailSource _mail;
        private string _mailMode;
        private readonly Notifier _notifier;
        private readonly MessagesPage _messages;
        private readonly MessagesPage _stale;
        private readonly AboutPage _about;
        private readonly NavRail _nav;
        private readonly NavItem _navMessages;
        private readonly NavItem _navStale;
        private readonly NavItem _navAbout;
        private readonly TransitionOverlay _transition;
        private readonly Control[] _pages;

        private int _pageIndex;
        private bool _started;
        private bool _closing;
        private bool _errorHintShown;
        private string _cachedAccount;
        private bool _accountLookupRunning;

        /// <summary>窗口基标题（状态提示会以 “基标题 · 状态” 的形式呈现）。</summary>
        private const string BaseTitle = "ClassTell 邮箱指令通知";

        /// <summary>标题栏状态提示的自动还原计时器。</summary>
        private readonly Timer _titleTimer;
        private string _titleNote;

        /// <summary>是否已隐藏到托盘（关闭窗口默认驻留托盘继续收信）。</summary>
        private bool _hiddenToTray;

        /// <summary>真正退出程序的标志（托盘菜单“退出”时置位，绕过驻留托盘逻辑）。</summary>
        private bool _exiting;

        /// <summary>最近一次已写入注册表的“开机自启动”状态（避免重复写）。</summary>
        private bool _startupApplied;

        /// <summary>当前页面索引（0 = 消息，1 = 关于），供诊断与自测使用。</summary>
        public int CurrentPageIndex { get { return _pageIndex; } }

        public ShellForm()
        {
            Theme.Apply(Settings.FontSize, Settings.DarkMode, Settings.AccentKey);
            Theme.Changed += OnThemeChanged;

            Text = BaseTitle;
            Icon = IconFactory.AppIcon;
            FormBorderStyle = FormBorderStyle.Sizable;   // 使用系统标准窗口框架
            MaximizeBox = true;
            MinimizeBox = true;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Window;
            DoubleBuffered = true;
            KeyPreview = true;
            // 界面自绘并按 Theme.S()/DpiScale 自行缩放，关闭 WinForms 自动缩放带来的二次缩放
            AutoScaleMode = AutoScaleMode.None;

            _mailMode = Settings.MailMode;
            _mail = MailSourceFactory.Create(_auth);
            _notifier = new Notifier(this);

            _messages = new MessagesPage();
            _messages.RefreshRequested += OnRefreshRequested;
            _messages.Visible = true;

            // 「过期」：打开软件之前（默认 24 小时内）收到且未读的指令邮件——只显示、不提醒
            _stale = new MessagesPage("过期", MailWindow.StaleSubtitle(), "暂无过期邮件", MailWindow.StaleEmptyHint(), false);
            _stale.Visible = false;

            _about = new AboutPage(_auth);
            _about.Visible = false;

            _pages = new Control[] { _messages, _stale, _about };

            _nav = new NavRail();
            _navMessages = new NavItem { Glyph = Theme.IconOr("\uE715", ""), Label = "消息" };
            _navMessages.Click += (s, e) => SwitchPage(0);
            _navStale = new NavItem { Glyph = Theme.IconOr("\uE823", ""), Label = "过期" };
            _navStale.Click += (s, e) => SwitchPage(1);
            _navAbout = new NavItem { Glyph = Theme.IconOr("\uE946", ""), Label = "关于" };
            _navAbout.Click += (s, e) => SwitchPage(2);
            _nav.Controls.Add(_navMessages);
            _nav.Controls.Add(_navStale);
            _nav.Controls.Add(_navAbout);

            _transition = new TransitionOverlay();
            _transition.Finished += OnTransitionFinished;

            Controls.Add(_nav);
            Controls.Add(_messages);
            Controls.Add(_stale);
            Controls.Add(_about);
            Controls.Add(_transition);

            _notifier.NotificationClicked += OnNotificationClicked;
            _notifier.ShowRequested += RestoreWindow;
            _notifier.RefreshRequested += OnRefreshRequested;
            _notifier.ExitRequested += ExitFromTray;
            _mail.MessageReceived += OnMessageReceived;
            _mail.StatusChanged += OnMailStatusChanged;
            Settings.Changed += OnSettingsChanged;
            _startupApplied = Settings.RunAtStartup;

            _titleTimer = new Timer { Interval = 6000 };
            _titleTimer.Tick += (s, e) =>
            {
                _titleTimer.Stop();
                SetTitleNote(null);
            };

            _about.Login.SignedIn += OnSignedIn;
            _about.Login.SignedOut += OnSignedOut;
            // 运行状态类信息只做软件内提示（标题栏），不走系统通知；错误用系统对话框
            _about.Login.Message += message => ShowInlineStatus(message);
            _about.Login.Error += message => ShowSystemDialog("登录失败", message);

            _navMessages.SetSelected(true);
            UpdateFooter();

            ClientSize = new Size(1140, 736);
            MinimumSize = new Size(920, 620);
        }

        // ---------- 生命周期 ----------
        protected override async void OnShown(EventArgs e)
        {
            base.OnShown(e);
            bool firstRunFont = !Settings.HasSetting("FontSize");
            Rectangle work = Screen.FromControl(this).WorkingArea;
            // 按 DPI + 可用显示区域做响应式适配（紧凑系数、逻辑尺寸、推荐字号）
            Theme.UpdateDpi(DeviceDpi / 96f, new Size(work.Width, work.Height));
            if (firstRunFont)
            {
                // 首次运行：按逻辑可用高度挑选默认字号，避免高分辨率下字体过大
                Settings.FontSize = Theme.RecommendedFontSize;
            }
            Theme.Apply(Settings.FontSize, Settings.DarkMode, Settings.AccentKey);
            Native.SetSystemTitleBarDark(Handle, Theme.Dark);
            Native.TryEnableRoundedCorners(Handle);   // 主窗口圆角（Win11；旧系统自动忽略）
            Settings.EnsureDefaults();
            AppLog.Info("ClassTell 启动，" + Theme.VersionText +
                        " · 屏幕 " + work.Width + "x" + work.Height + "（逻辑 " +
                        Math.Round(Theme.LogicalWidth) + "x" + Math.Round(Theme.LogicalHeight) + "）" +
                        " · DeviceDpi " + DeviceDpi + " · 缩放 " + Theme.DpiScale.ToString("0.00") +
                        " · 紧凑系数 " + Theme.Density.ToString("0.00") +
                        " · 字号 " + Theme.FontSize.ToString("0.#") + "pt" +
                        " · 模式 " + Theme.Mode + " · 配色 " + Theme.Palette.Name + Theme.Palette.Hex);

            ApplyInitialSize();
            AppLog.Info("窗口客户区 " + ClientSize.Width + "x" + ClientSize.Height + "，位置 " + Location.X + "," + Location.Y);

            LayoutChrome();
            _about.RefreshLoginState();
            _messages.SetStatus(MailState.Stopped, "未连接");
            RefreshAccountAsync();

            if (AppOptions.Demo)
            {
                StartDemo();
                return;
            }

            if (_started) return;
            _started = true;

            bool hasAccount = await _auth.HasCachedAccountAsync();
            if (!hasAccount)
            {
                SwitchPage(2);
                ShowInlineStatus("请先登录邮箱账号（OAuth2 / Modern Auth）");
                AppLog.Info("未检测到登录状态，打开登录页");
                if (AppOptions.NoAutoLogin)
                {
                    _messages.SetStatus(MailState.Stopped, "等待登录");
                    return;
                }
                Animator.Delay(700, () =>
                {
                    if (!_closing) _about.Login.StartBrowserLogin();
                });
            }
            else
            {
                ShowInlineStatus("已恢复登录状态，正在接收邮件");
                _mail.Start();
            }

            // 开机自启动：启动后直接驻留托盘（不弹主窗口）
            if (AppOptions.StartInTray) HideToTray();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // 关闭窗口 ≠ 退出程序：默认驻留托盘继续收信（托盘菜单“退出 ClassTell”才真正退出）。
            // 注意：UserClosing = 点 X / Alt+F4；TaskManagerClosing = 外部进程发来 WM_CLOSE
            //（如“结束任务”、脚本调用 CloseMainWindow）；Windows 关机必须放行。
            bool hideToTray = !_exiting && Settings.CloseToTray &&
                              (e.CloseReason == CloseReason.UserClosing || e.CloseReason == CloseReason.TaskManagerClosing);
            AppLog.Info("窗口关闭请求：reason=" + e.CloseReason + " closeToTray=" + Settings.CloseToTray +
                        " exiting=" + _exiting + " → " + (hideToTray ? "驻留托盘" : "退出程序"));

            if (hideToTray)
            {
                e.Cancel = true;
                HideToTray();
                return;
            }

            _closing = true;
            Theme.Changed -= OnThemeChanged;
            Settings.Changed -= OnSettingsChanged;
            try { _mail.Dispose(); } catch (Exception) { }
            try
            {
                _notifier.ShowRequested -= RestoreWindow;
                _notifier.RefreshRequested -= OnRefreshRequested;
                _notifier.ExitRequested -= ExitFromTray;
                _notifier.Dispose();
            }
            catch (Exception) { }
            try
            {
                _titleTimer.Stop();
                _titleTimer.Dispose();
            }
            catch (Exception) { }
            try { Settings.Save(); } catch (Exception) { }
            base.OnFormClosing(e);
        }

        // ---------- 托盘 / 标题栏状态 ----------
        /// <summary>
        /// 隐藏主窗口到托盘（后台继续收信）。
        /// 注意：这里只 Hide()，**不要**改 ShowInTaskbar —— 该属性会重建窗口句柄，
        /// 在关闭流程中重建句柄会让窗口真的被关掉（曾导致“关闭即退出”）。
        /// </summary>
        private void HideToTray()
        {
            _hiddenToTray = true;
            Hide();
            AppLog.Info("主窗口已隐藏到托盘（后台继续收信）");
        }

        /// <summary>从托盘恢复主窗口。</summary>
        private void RestoreWindow()
        {
            if (_closing || IsDisposed || Disposing) return;
            _hiddenToTray = false;
            if (!Visible) Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Native.StopFlash(Handle);
            try { Activate(); } catch (Exception) { }
            SetTitleNote(null);
        }

        /// <summary>托盘菜单“退出 ClassTell”：真正退出（不驻留托盘）。</summary>
        private void ExitFromTray()
        {
            _exiting = true;
            try { Close(); } catch (Exception) { }
        }

        /// <summary>
        /// 软件内状态提示：写入窗口标题并 6 秒后自动还原。
        /// 邮箱检查、登录成功、已连接等状态一律走这里，不再使用系统通知通道。
        /// </summary>
        private void ShowInlineStatus(string text)
        {
            if (_closing || IsDisposed || Disposing) return;
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action<string>(ShowInlineStatus), text); }
                catch (Exception) { }
                return;
            }

            SetTitleNote(text);
            _titleTimer.Stop();
            _titleTimer.Start();
        }

        private void SetTitleNote(string note)
        {
            if (_closing || IsDisposed || Disposing) return;
            _titleNote = note;
            Text = string.IsNullOrEmpty(note) ? BaseTitle : BaseTitle + " · " + note;
        }

        /// <summary>开机自启动设置变化 → 写/删当前用户的 Run 项；失败则回滚并提示。</summary>
        private void ApplyStartupSetting()
        {
            bool want = Settings.RunAtStartup;
            if (want == _startupApplied) return;
            _startupApplied = want;
            if (Startup.Apply(want))
            {
                ShowInlineStatus(want ? "已开启开机自启动" : "已关闭开机自启动");
            }
            else
            {
                Settings.RunAtStartup = !want;
                _startupApplied = !want;
                ShowInlineStatus("开机自启动设置失败（可能被系统策略限制）");
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) Theme.Changed -= OnThemeChanged;
            base.Dispose(disposing);
        }

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            Rectangle work = Screen.FromControl(this).WorkingArea;
            Theme.UpdateDpi(DeviceDpi / 96f, new Size(work.Width, work.Height));
            ApplyInitialSize();
        }

        private void OnThemeChanged()
        {
            if (_closing || IsDisposed || Disposing) return;
            BackColor = Theme.Window;
            _nav.BackColor = Theme.NavBackground;
            Native.SetSystemTitleBarDark(Handle, Theme.Dark);
            IconFactory.InvalidateAppIcon();
            Icon = IconFactory.AppIcon;
            _notifier.RefreshIcon();
            LayoutChrome();
            // 整棵树重绘 + 立即刷新，彻底消除主题切换后可能残留的旧底色
            Theme.InvalidateTree(this);
            Update();
        }

        // ---------- 布局 ----------
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutChrome();
        }

        /// <summary>按设计尺寸与屏幕工作区计算窗口初始大小（高 DPI / 小屏幕下自动收缩）。</summary>
        private void ApplyInitialSize()
        {
            Rectangle wa = Screen.FromControl(this).WorkingArea;
            int maxW = Math.Max(Theme.S(480), (int)(wa.Width * 0.94));
            int maxH = Math.Max(Theme.S(360), (int)(wa.Height * 0.94));
            int minW = Math.Min(Theme.S(880), wa.Width);
            int minH = Math.Min(Theme.S(600), wa.Height);

            int w = Math.Max(minW, Math.Min(Theme.S(1140), maxW));
            int h = Math.Max(minH, Math.Min(Theme.S(736), maxH));

            MinimumSize = new Size(Math.Min(minW, w), Math.Min(minH, h));
            ClientSize = new Size(w, h);
            Location = new Point(wa.Left + Math.Max(0, (wa.Width - Width) / 2), wa.Top + Math.Max(0, (wa.Height - Height) / 2));
            LayoutChrome();
        }

        private void LayoutChrome()
        {
            if (_nav == null || _pages == null || _transition == null) return;

            int navWidth = Theme.NavWidth;
            int w = ClientSize.Width;
            int h = ClientSize.Height;

            _nav.SetBounds(0, 0, navWidth, Math.Max(Theme.S(80), h));

            int itemHeight = Math.Max(Theme.S(42), (int)Math.Ceiling(Theme.LineHeight(Theme.Font(0.5f, FontStyle.Regular))) + Theme.S(16));
            int itemWidth = Math.Max(Theme.S(80), navWidth - Theme.S(20));
            int itemTop = _nav.HeaderHeight;
            _navMessages.SetBounds(Theme.S(10), itemTop, itemWidth, itemHeight);
            _navStale.SetBounds(Theme.S(10), itemTop + itemHeight + Theme.S(6), itemWidth, itemHeight);
            _navAbout.SetBounds(Theme.S(10), itemTop + (itemHeight + Theme.S(6)) * 2, itemWidth, itemHeight);

            int contentW = Math.Max(Theme.S(120), w - navWidth);
            int contentH = Math.Max(Theme.S(120), h);
            foreach (Control page in _pages) page.SetBounds(navWidth, 0, contentW, contentH);

            _transition.SetBounds(navWidth, 0, contentW, contentH);
            _transition.BringToFront();

            UpdateFooter();
            Invalidate();
        }

        private void UpdateFooter()
        {
            string account = !string.IsNullOrEmpty(Settings.Account) ? Settings.Account : _cachedAccount;
            _nav.AccountText = account;
            _nav.FooterText = "版本 " + Theme.VersionText + " · " + (Theme.Dark ? "深色" : "浅色") + " · " + Theme.Palette.Name;
            _nav.Invalidate();
        }

        /// <summary>异步读取 MSAL 缓存里的账号名（避免在布局过程中阻塞 UI 线程）。</summary>
        private async void RefreshAccountAsync()
        {
            if (_accountLookupRunning) return;
            _accountLookupRunning = true;
            try
            {
                _cachedAccount = await _auth.GetCachedAccountNameAsync();
            }
            catch (Exception ex)
            {
                AppLog.Exception_("读取缓存账号失败", ex);
            }
            finally
            {
                _accountLookupRunning = false;
            }
            UpdateFooter();
        }

        /// <summary>演示模式：注入示例消息并模拟一次“C / call”通知。</summary>
        private void StartDemo()
        {
            _started = true;
            _messages.SetStatus(MailState.Listening, "演示模式 · 未连接邮箱");
            var items = DemoData.CreateMessages();
            for (int i = 0; i < items.Count; i++)
            {
                // 演示：前 4 条进「消息」，其余当作打开软件前收到的“过期邮件”演示「过期」分项
                if (i < MessagesPage.PageSize) _messages.AddMessage(items[i]);
                else _stale.AddMessage(items[i]);
            }
            ShowInlineStatus("演示模式：已注入 " + items.Count + " 条示例消息（重启后清空）");
            AppLog.Info("演示模式：注入 " + items.Count + " 条示例消息，消息页 " + _messages.MessageCount +
                        " 条、过期页 " + _stale.MessageCount + " 条");

            Animator.Delay(1500, () =>
            {
                if (_closing) return;
                MessageItem call = items[0];
                _messages.ShowDetail(call);
                _notifier.NotifyCall(call);
                AppLog.Info("演示模式：模拟 C/call 通知完成（" + call.Title + "）");
            });
        }

        // ---------- 页面切换 ----------
        private void SwitchPage(int index)
        {
            SwitchPage(index, true);
        }

        private void SwitchPage(int index, bool animate)
        {
            if (index < 0 || index >= _pages.Length) return;
            int dx = index > _pageIndex ? Theme.S(30) : -Theme.S(30);
            Control current = _pages[_pageIndex];
            Control next = _pages[index];

            _navMessages.SetSelected(index == 0);
            _navStale.SetSelected(index == 1);
            _navAbout.SetSelected(index == 2);

            if (index == _pageIndex && next.Visible) return;

            if (!animate || !current.Visible || !IsHandleCreated || _transition.Visible)
            {
                current.Visible = false;
                next.Visible = true;
                next.BringToFront();
                _pageIndex = index;
                if (next == _about) _about.RefreshLoginState();
                return;
            }

            Size size = next.Size;
            next.Location = new Point(next.Left - dx, next.Top);
            next.Visible = true;
            next.BringToFront();

            Bitmap from = TransitionOverlay.CaptureControl(current, size);
            Bitmap to = TransitionOverlay.CaptureControl(next, size);

            next.Location = new Point(next.Left + dx, next.Top);
            next.Visible = false;
            current.Visible = false;
            _pageIndex = index;

            if (from == null || to == null)
            {
                next.Visible = true;
                next.BringToFront();
                if (next == _about) _about.RefreshLoginState();
                return;
            }

            _transition.BringToFront();
            _transition.Play(from, to, dx, 340);
        }

        private void OnTransitionFinished()
        {
            Control page = _pages[_pageIndex];
            page.Visible = true;
            page.BringToFront();
            if (page == _about) _about.RefreshLoginState();
        }

        /// <summary>
        /// 设置变化：若“收信方式”改了，就地替换收信实现并重连（无需重启程序）。
        /// 其他设置（字号/主题/轮询间隔等）不在这里处理。
        /// </summary>
        private void OnSettingsChanged()
        {
            if (_closing || IsDisposed || Disposing) return;
            ApplyStartupSetting();
            if (Settings.MailMode == _mailMode) return;

            _mailMode = Settings.MailMode;
            AppLog.Info("收信方式切换为 " + MailSourceFactory.ModeName);

            bool wasRunning = _mail.IsRunning;
            try
            {
                _mail.MessageReceived -= OnMessageReceived;
                _mail.StatusChanged -= OnMailStatusChanged;
            }
            catch (Exception) { }
            try { _mail.Dispose(); } catch (Exception) { }

            _mail = MailSourceFactory.Create(_auth);
            _mail.MessageReceived += OnMessageReceived;
            _mail.StatusChanged += OnMailStatusChanged;
            _errorHintShown = false;
            _messages.SetStatus(MailState.Stopped, "收信方式已切换为 " + MailSourceFactory.ModeName);

            if (wasRunning || _started) _mail.Start();
        }

        // ---------- 邮件与通知 ----------
        private void OnRefreshRequested()
        {
            _mail.RequestRefresh();
            ShowInlineStatus("正在检查新邮件…");
        }

        private void OnMessageReceived(MessageItem item)
        {
            if (item == null) return;
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action<MessageItem>(OnMessageReceived), item); }
                catch (Exception) { }
                return;
            }

            // 打开软件之前收到的邮件（默认 24 小时内）→ 归入「过期」：只显示、不提醒
            if (MailWindow.IsStale(item.ReceivedLocal))
            {
                _stale.AddMessage(item);
                AppLog.Info("过期邮件（只显示不提醒）：" + item.ReceivedText + " · " + item.Title);
                return;
            }

            _messages.AddMessage(item);
            if (item.IsCall && Settings.NotifyOnCall) AlertCall(item);
        }

        /// <summary>
        /// 呼叫提醒（多路并用，确保一定看得见）：
        ///   1) 系统通知（托盘气泡）——可能被 Windows 通知/专注助手设置抑制；
        ///   2) 若窗口已驻留托盘则召回窗口，否则闪烁任务栏按钮（不强行抢焦点）；
        ///   3) 标题栏显示 “【呼叫】标题” 并保持 6 秒。
        /// </summary>
        private void AlertCall(MessageItem item)
        {
            ShowInlineStatus("【呼叫】" + (item == null ? string.Empty : item.Title));
            _notifier.NotifyCall(item);
            if (_hiddenToTray) RestoreWindow();
            Native.FlashWindow(Handle);
            AppLog.Info("呼叫提醒已发出（系统通知 + 任务栏闪烁 + 标题提示）");
        }

        private void OnSignedIn()
        {
            RefreshAccountAsync();
            UpdateFooter();
            ShowInlineStatus("登录成功，开始接收邮件");
            SwitchPage(0);
            if (!_mail.IsRunning) _mail.Start();
        }

        private void OnSignedOut()
        {
            _cachedAccount = null;
            UpdateFooter();
            _messages.ClearMessages();
            _stale.ClearMessages();
            _messages.SetStatus(MailState.Stopped, "未连接");
            _mail.Stop();
        }

        private void OnNotificationClicked(MessageItem item)
        {
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action<MessageItem>(OnNotificationClicked), item); }
                catch (Exception) { }
                return;
            }

            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Show();
            Activate();
            SwitchPage(0);
            MessageItem target = item ?? _notifier.LastNotified;
            if (target != null) _messages.ShowDetail(target);
        }

        private void OnMailStatusChanged(MailStatusEventArgs args)
        {
            if (args == null) return;
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action<MailStatusEventArgs>(OnMailStatusChanged), args); }
                catch (Exception) { }
                return;
            }

            try
            {
                _messages.SetStatus(args.State, args.Message);
                _messages.SetBusy(args.State == MailState.Connecting || args.State == MailState.Authenticating || args.State == MailState.Syncing);

                switch (args.State)
                {
                    case MailState.Listening:
                        if (_started) ShowInlineStatus("已连接邮箱，等待新邮件");
                        break;
                    case MailState.AuthRequired:
                        _mail.Stop();
                        SwitchPage(2);
                        ShowSystemDialog("需要重新登录", string.IsNullOrEmpty(args.Hint)
                            ? "登录状态已失效，请重新登录邮箱。"
                            : args.Hint);
                        AppLog.Warn("需要重新登录：" + args.Message);
                        break;
                    case MailState.Error:
                        ShowInlineStatus("收取出错：" + args.Message);
                        // 需要用户动手处理的故障（如邮箱未开启 IMAP）：用系统对话框提示一次
                        if (!string.IsNullOrEmpty(args.Hint) && !_errorHintShown)
                        {
                            _errorHintShown = true;
                            ShowSystemDialog(args.Message, args.Hint);
                        }
                        break;
                }
                UpdateFooter();
            }
            catch (Exception ex)
            {
                AppLog.Exception_("处理邮件状态失败", ex);
            }
        }

        /// <summary>用系统对话框提示需要用户确认的信息（本软件不再有任何自有弹窗）。</summary>
        private void ShowSystemDialog(string title, string message)
        {
            try
            {
                if (!IsHandleCreated || _closing) return;
                MessageBox.Show(this, message, "ClassTell · " + title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                AppLog.Exception_("显示系统对话框失败", ex);
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape && _messages.IsDetailOpen)
            {
                _messages.CloseDetail();
                return true;
            }
            if (keyData == Keys.F5)
            {
                OnRefreshRequested();
                return true;
            }
            if (keyData == (Keys.Control | Keys.D1))
            {
                SwitchPage(0);
                return true;
            }
            if (keyData == (Keys.Control | Keys.D2))
            {
                SwitchPage(1);
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
