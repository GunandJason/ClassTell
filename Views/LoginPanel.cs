using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ClassTell
{
    /// <summary>
    /// 邮箱登录面板（“关于 → 邮箱登录”）：兼容 Outlook / Microsoft 365，
    /// 支持系统浏览器交互登录与设备代码登录（OAuth2 / Modern Auth）。
    /// 按钮按内容自动定宽并自动换行，输入框为圆角容器，深浅模式下底色都会跟随。
    /// </summary>
    internal sealed class LoginPanel : MotionControl
    {
        private readonly AuthService _auth;
        private readonly MaterialButton _btnBrowser;
        private readonly MaterialButton _btnDevice;
        private readonly MaterialButton _btnSignOut;
        private readonly MaterialButton _btnCancel;
        private readonly MaterialButton _btnCopy;
        private readonly MaterialButton _btnAdvanced;
        private readonly MaterialButton _btnSaveAdvanced;
        private readonly MaterialButton _btnMailMode;
        private readonly Spinner _spinner;
        private readonly RoundedInputHost _inputClientId;
        private readonly RoundedInputHost _inputTenant;
        private readonly RoundedInputHost _inputScopes;

        private bool _busy;
        private bool _advancedOpen;
        private double _advancedReveal;
        private string _deviceCode = string.Empty;
        private string _deviceUrl = string.Empty;
        private string _statusMessage = "尚未登录";
        private string _account = string.Empty;
        private CancellationTokenSource _cts;
        private Rectangle _deviceRect;
        private Rectangle _statusRect;
        private Rectangle _clientIdLabelRect;
        private Rectangle _tenantLabelRect;
        private Rectangle _scopesLabelRect;
        private Rectangle _mailModeLabelRect;
        private Rectangle _hintRect;

        public LoginPanel(AuthService auth)
        {
            _auth = auth;
            BackColor = Color.Transparent;

            _btnBrowser = new MaterialButton { Kind = ButtonKind.Filled, Glyph = Theme.IconOr("\uE77B", ""), Text = "使用浏览器登录" };
            _btnBrowser.Click += (s, e) => StartBrowserLogin();
            Controls.Add(_btnBrowser);

            _btnDevice = new MaterialButton { Kind = ButtonKind.Outlined, Glyph = Theme.IconOr("\uE8EA", ""), Text = "使用设备代码登录" };
            _btnDevice.Click += (s, e) => StartDeviceCodeLogin();
            Controls.Add(_btnDevice);

            _btnSignOut = new MaterialButton { Kind = ButtonKind.Text, Glyph = Theme.IconOr("\uF3B1", ""), Text = "退出登录" };
            _btnSignOut.Click += (s, e) => SignOut();
            Controls.Add(_btnSignOut);

            _btnCancel = new MaterialButton { Kind = ButtonKind.Text, Text = "取消", Visible = false };
            _btnCancel.Click += (s, e) => CancelCurrent();
            Controls.Add(_btnCancel);

            _btnCopy = new MaterialButton { Kind = ButtonKind.Text, Glyph = Theme.IconOr("\uE8C8", ""), Text = "复制代码", Visible = false };
            _btnCopy.Click += (s, e) => CopyDeviceCode();
            Controls.Add(_btnCopy);

            _btnAdvanced = new MaterialButton { Kind = ButtonKind.Text, Glyph = Theme.IconOr("\uE713", ""), Text = "高级设置" };
            _btnAdvanced.Click += (s, e) => ToggleAdvanced();
            Controls.Add(_btnAdvanced);

            _btnSaveAdvanced = new MaterialButton { Kind = ButtonKind.Tonal, Text = "保存并重新登录", Visible = false };
            _btnSaveAdvanced.Click += (s, e) => SaveAdvanced();
            Controls.Add(_btnSaveAdvanced);

            // 收信方式（Graph / IMAP）：放在高级设置里，切换后主窗口会就地重连
            _btnMailMode = new MaterialButton { Kind = ButtonKind.Tonal, Visible = false, Text = MailModeButtonText() };
            _btnMailMode.Click += (s, e) => ToggleMailMode();
            Controls.Add(_btnMailMode);

            _spinner = new Spinner { Visible = false };
            Controls.Add(_spinner);

            _inputClientId = CreateInput("例如 00000000-0000-0000-0000-000000000000");
            _inputTenant = CreateInput("common / consumers / 租户域名");
            _inputScopes = CreateInput("https://outlook.office.com/IMAP.AccessAsUser.All");
            RefreshScopesInput();

            FitButtons();
            RefreshState();
        }

        /// <summary>面板高度变化时通知宿主重新排布。</summary>
        public event Action LayoutChanged;

        /// <summary>提示信息（由宿主用系统通知展示）。</summary>
        public event Action<string> Message;

        /// <summary>需要用户确认的错误（由宿主用系统对话框展示）。</summary>
        public event Action<string> Error;

        /// <summary>登录成功。</summary>
        public event Action SignedIn;

        /// <summary>退出登录。</summary>
        public event Action SignedOut;

        public bool IsBusy { get { return _busy; } }

        private RoundedInputHost CreateInput(string placeholder)
        {
            var host = new RoundedInputHost { PlaceholderText = placeholder, Visible = false };
            Controls.Add(host);
            return host;
        }

        /// <summary>所有按钮都按内容定宽（避免中文按钮文字被裁切）。</summary>
        private void FitButtons()
        {
            foreach (MaterialButton btn in AllButtons()) btn.FitToContent();
        }

        private IEnumerable<MaterialButton> AllButtons()
        {
            yield return _btnBrowser;
            yield return _btnDevice;
            yield return _btnSignOut;
            yield return _btnCancel;
            yield return _btnCopy;
            yield return _btnAdvanced;
            yield return _btnSaveAdvanced;
            yield return _btnMailMode;
        }

        // ---------- 状态 ----------
        public void RefreshState()
        {
            _account = Settings.Account;
            UpdateStatusAsync();
        }

        private async void UpdateStatusAsync()
        {
            try
            {
                string cached = await _auth.GetCachedAccountNameAsync();
                if (!string.IsNullOrEmpty(cached)) _account = cached;
                if (!_busy)
                {
                    _statusMessage = string.IsNullOrEmpty(_account) ? "尚未登录" : "已登录";
                    _btnSignOut.Visible = !string.IsNullOrEmpty(_account);
                }
            }
            catch (Exception ex)
            {
                AppLog.Exception_("读取登录状态失败", ex);
            }
            LayoutChildren();
            Invalidate();
            NotifyLayoutChanged();
        }

        private void SetBusy(bool busy, string message)
        {
            _busy = busy;
            _statusMessage = message;
            _spinner.Visible = busy;
            _btnCancel.Visible = busy;
            _btnBrowser.Enabled = !busy;
            _btnDevice.Enabled = !busy;
            _btnSignOut.Enabled = !busy;
            _btnSaveAdvanced.Enabled = !busy;
            if (busy) _spinner.BringToFront();
            LayoutChildren();
            Invalidate();
            NotifyLayoutChanged();
        }

        private void NotifyLayoutChanged()
        {
            Action handler = LayoutChanged;
            if (handler != null) handler();
        }

        private void CancelCurrent()
        {
            try
            {
                if (_cts != null) _cts.Cancel();
            }
            catch (Exception) { }
            SetBusy(false, "已取消");
        }

        private void Notify(string message)
        {
            Action<string> handler = Message;
            if (handler != null) handler(message);
        }

        /// <summary>上报需要用户确认的错误（宿主会弹系统对话框）。</summary>
        private void NotifyError(string message)
        {
            Action<string> handler = Error;
            if (handler != null) handler(message);
        }

        private static string ShortMessage(Exception ex)
        {
            string m = ex.Message ?? string.Empty;
            return m.Length > 90 ? m.Substring(0, 90) + "…" : m;
        }

        // ---------- 登录流程 ----------
        public async void StartBrowserLogin()
        {
            if (_busy) return;
            _cts = new CancellationTokenSource();
            SetBusy(true, "正在打开系统浏览器 …");
            try
            {
                await _auth.LoginInteractiveAsync(m => SetBusy(true, m), _cts.Token);
                _statusMessage = "登录成功";
                Notify("登录成功，开始接收邮件");
                Action handler = SignedIn;
                if (handler != null) handler();
            }
            catch (OperationCanceledException)
            {
                _statusMessage = "已取消登录";
                Notify("已取消登录");
            }
            catch (Exception ex)
            {
                _statusMessage = "登录失败";
                AppLog.Exception_("浏览器登录失败", ex);
                NotifyError("登录失败：" + ShortMessage(ex));
            }
            finally
            {
                SetBusy(false, _statusMessage);
                RefreshState();
            }
        }

        public async void StartDeviceCodeLogin()
        {
            if (_busy) return;
            _cts = new CancellationTokenSource();
            SetBusy(true, "正在申请设备代码 …");
            try
            {
                await _auth.LoginDeviceCodeAsync(prompt =>
                {
                    _deviceCode = prompt.UserCode;
                    _deviceUrl = prompt.VerificationUrl;
                    _btnCopy.Visible = true;
                    SetBusy(true, "请在浏览器输入代码完成登录");
                }, _cts.Token);
                _statusMessage = "登录成功";
                Notify("登录成功，开始接收邮件");
                Action handler = SignedIn;
                if (handler != null) handler();
            }
            catch (OperationCanceledException)
            {
                _statusMessage = "已取消登录";
                Notify("已取消登录");
            }
            catch (Exception ex)
            {
                _statusMessage = "登录失败";
                AppLog.Exception_("设备代码登录失败", ex);
                NotifyError("登录失败：" + ShortMessage(ex));
            }
            finally
            {
                _deviceCode = string.Empty;
                _btnCopy.Visible = false;
                SetBusy(false, _statusMessage);
                RefreshState();
            }
        }

        private async void SignOut()
        {
            try
            {
                await _auth.LogoutAsync();
                _account = string.Empty;
                _statusMessage = "已退出登录";
                _btnSignOut.Visible = false;
                Notify("已退出登录");
                Action handler = SignedOut;
                if (handler != null) handler();
            }
            catch (Exception ex)
            {
                AppLog.Exception_("退出登录失败", ex);
            }
            LayoutChildren();
            Invalidate();
            NotifyLayoutChanged();
        }

        private void CopyDeviceCode()
        {
            try
            {
                Clipboard.SetText(_deviceCode);
                Notify("设备代码已复制：" + _deviceCode);
            }
            catch (Exception ex)
            {
                AppLog.Exception_("复制设备代码失败", ex);
                Notify("设备代码：" + _deviceCode + "（请在浏览器打开 " + _deviceUrl + "）");
            }
        }

        private void ToggleAdvanced()
        {
            _advancedOpen = !_advancedOpen;
            _inputClientId.Visible = _advancedOpen;
            _inputTenant.Visible = _advancedOpen;
            _inputScopes.Visible = _advancedOpen;
            _btnSaveAdvanced.Visible = _advancedOpen;
            _btnMailMode.Visible = _advancedOpen;

            if (_advancedOpen)
            {
                _inputClientId.Text = Settings.ClientId;
                _inputTenant.Text = Settings.Tenant;
                _btnMailMode.Text = MailModeButtonText();
                _btnMailMode.FitToContent();
                RefreshScopesInput();
            }

            Animator.Tween(_advancedReveal, _advancedOpen ? 1d : 0d, 300, Ease.EmphasizedDecelerate, v =>
            {
                _advancedReveal = v;
                LayoutChildren();
                NotifyLayoutChanged();
            });
        }

        private void SaveAdvanced()
        {
            Settings.ClientId = _inputClientId.Text.Trim();
            Settings.Tenant = string.IsNullOrWhiteSpace(_inputTenant.Text) ? "common" : _inputTenant.Text.Trim();
            // 权限按当前收信方式保存到对应的配置项（Graph 用 GraphScopes，IMAP 用 Scopes）
            if (Settings.MailMode == "imap") Settings.Scopes = _inputScopes.Text.Trim();
            else Settings.GraphScopes = _inputScopes.Text.Trim();
            Notify("已保存登录配置（收信方式：" + MailSourceFactory.ModeName + "）");
            AppLog.Info("更新 OAuth2 配置，tenant=" + Settings.Tenant + " mailMode=" + Settings.MailMode +
                        " scopes=" + _inputScopes.Text.Trim());
            ToggleAdvanced();
        }

        /// <summary>收信方式按钮文字。</summary>
        private static string MailModeButtonText()
        {
            return Settings.MailMode == "imap" ? "IMAP（需邮箱已开启）" : "Microsoft Graph（推荐）";
        }

        /// <summary>
        /// 在 Microsoft Graph 与 IMAP 之间切换收信方式：立即写入设置，
        /// 主窗口收到 Settings.Changed 后会就地替换收信实现并重连。
        /// </summary>
        private void ToggleMailMode()
        {
            Settings.MailMode = Settings.MailMode == "imap" ? "graph" : "imap";
            _btnMailMode.Text = MailModeButtonText();
            _btnMailMode.FitToContent();
            RefreshScopesInput();
            Notify(Settings.MailMode == "graph"
                ? "收信方式已切换为 Microsoft Graph；首次使用请点“保存并重新登录”同意 Mail.Read"
                : "收信方式已切换为 IMAP；需要邮箱侧已开启 IMAP");
            LayoutChildren();
            NotifyLayoutChanged();
        }

        /// <summary>权限输入框按当前收信方式显示对应 scope（避免 Graph 模式下误填 IMAP 权限）。</summary>
        private void RefreshScopesInput()
        {
            bool graph = Settings.MailMode != "imap";
            _inputScopes.PlaceholderText = graph
                ? "https://graph.microsoft.com/Mail.Read"
                : "https://outlook.office.com/IMAP.AccessAsUser.All";
            _inputScopes.Text = graph ? Settings.GraphScopes : Settings.Scopes;
        }

        // ---------- 布局（按钮自动定宽 + 自动换行；行高按字体度量） ----------
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutChildren();
        }

        protected override void OnThemeChanged()
        {
            FitButtons();
            LayoutChildren();
        }

        private void LayoutChildren()
        {
            LayoutCore(Width, true);
        }

        /// <summary>
        /// 统一的排布算法：apply=true 时真正设置控件位置，apply=false 时只计算所需高度。
        /// 因此 PreferredHeight 与实际布局永远一致。
        /// </summary>
        private int LayoutCore(int width, bool apply)
        {
            if (width <= Theme.S(80)) return 0;

            int pad = Theme.S(22);
            int usable = Math.Max(Theme.S(160), width - pad * 2 - Theme.S(4));
            float bodyLine = Theme.LineHeight(Theme.Body);
            int btnH = Math.Max(Theme.S(40), (int)Math.Ceiling(bodyLine) + Theme.S(12));
            float statusLine = Theme.LineHeight(Theme.Font(-0.3f, FontStyle.Regular));
            float labelLine = Theme.LineHeight(Theme.Font(-0.5f, FontStyle.Regular));
            int inputH = Math.Max(Theme.S(28), (int)Math.Ceiling(labelLine) + Theme.S(12));

            int y = Theme.S(74);

            // 状态行（加载圈 + 状态文字）
            if (apply)
            {
                _statusRect = new Rectangle(pad, y, usable, (int)Math.Ceiling(statusLine));
                _spinner.SetBounds(pad, y + (int)((statusLine - Theme.S(16)) / 2f), Theme.S(16), Theme.S(16));
                if (_spinner.Visible) _spinner.BringToFront();
            }
            y += (int)Math.Ceiling(statusLine) + Theme.S(12);

            // 第一行：浏览器登录 + 设备代码登录（放不下就换行）
            y = FlowButtons(new[] { _btnBrowser, _btnDevice }, pad, y, usable, btnH, apply);

            // 设备代码提示框（含“复制代码”按钮）
            bool hasDevice = !string.IsNullOrEmpty(_deviceCode) && _advancedReveal < 0.99d;
            if (hasDevice)
            {
                int deviceH = (int)Math.Ceiling(Theme.LineHeight(Theme.Font(-0.5f, FontStyle.Regular))
                              + Theme.LineHeight(Theme.Font(1.5f, FontStyle.Bold))) + Theme.S(24);
                if (apply)
                {
                    _deviceRect = new Rectangle(pad, y, usable, deviceH);
                    _btnCopy.SetBounds(pad + Theme.S(8), y + deviceH - _btnCopy.Height - Theme.S(6), _btnCopy.Width, _btnCopy.Height);
                }
                y += deviceH + Theme.S(10);
            }
            else if (apply)
            {
                _deviceRect = Rectangle.Empty;
            }

            // 第二行：退出登录 / 取消 + 高级设置（统一用较小的行高）
            int smallBtnH = Math.Max(Theme.S(32), (int)Math.Ceiling(bodyLine) + Theme.S(6));
            foreach (MaterialButton small in new[] { _btnSignOut, _btnCancel, _btnAdvanced, _btnCopy, _btnSaveAdvanced, _btnMailMode })
                small.MinHeight = smallBtnH;
            y = FlowButtons(new[] { _btnSignOut, _btnCancel, _btnAdvanced }, pad, y, usable, smallBtnH, apply);

            // 高级设置（展开动画按 _advancedReveal 插值）
            int advancedTop = y;
            if (_advancedReveal > 0.01d)
            {
                int labelWidth = (int)Math.Ceiling(Theme.MeasureLine("权限范围", Theme.Font(-1f, FontStyle.Regular))) + Theme.S(16);
                int rowGap = inputH + Theme.S(8);
                int blockTop = advancedTop;

                if (apply)
                {
                    _clientIdLabelRect = new Rectangle(pad, blockTop, labelWidth, inputH);
                    _inputClientId.SetBounds(pad + labelWidth, blockTop, Math.Max(Theme.S(120), width - pad * 2 - labelWidth), inputH);

                    _tenantLabelRect = new Rectangle(pad, blockTop + rowGap, labelWidth, inputH);
                    _inputTenant.SetBounds(pad + labelWidth, blockTop + rowGap, Math.Max(Theme.S(120), width - pad * 2 - labelWidth), inputH);

                    _scopesLabelRect = new Rectangle(pad, blockTop + rowGap * 2, labelWidth, inputH);
                    _inputScopes.SetBounds(pad + labelWidth, blockTop + rowGap * 2, Math.Max(Theme.S(120), width - pad * 2 - labelWidth), inputH);

                    // 收信方式（Graph / IMAP）：标签 + 可点击切换的按钮
                    _mailModeLabelRect = new Rectangle(pad, blockTop + rowGap * 3, labelWidth, inputH);
                    int modeTop = blockTop + rowGap * 3 + Math.Max(0, (inputH - _btnMailMode.Height) / 2);
                    _btnMailMode.SetBounds(pad + labelWidth, modeTop, _btnMailMode.Width, _btnMailMode.Height);

                    int saveTop = blockTop + rowGap * 4;
                    _btnSaveAdvanced.SetBounds(pad + labelWidth, saveTop, _btnSaveAdvanced.Width, _btnSaveAdvanced.Height);
                    _hintRect = new Rectangle(pad, saveTop + _btnSaveAdvanced.Height + Theme.S(8), usable, (int)Math.Ceiling(Theme.LineHeight(Theme.Font(-1.5f, FontStyle.Regular)) * 3f) + Theme.S(6));
                }
                int advancedHeight = rowGap * 4 + _btnSaveAdvanced.Height + Theme.S(8)
                                   + (int)Math.Ceiling(Theme.LineHeight(Theme.Font(-1.5f, FontStyle.Regular)) * 3f) + Theme.S(6);
                y = advancedTop + (int)Math.Round(_advancedReveal * advancedHeight);
            }
            else if (apply)
            {
                _clientIdLabelRect = Rectangle.Empty;
                _tenantLabelRect = Rectangle.Empty;
                _scopesLabelRect = Rectangle.Empty;
                _mailModeLabelRect = Rectangle.Empty;
                _hintRect = Rectangle.Empty;
            }

            return y + Theme.S(18);
        }

        /// <summary>把按钮按顺序排在多行里（放不下就换行），返回下一个可用 Y。</summary>
        private int FlowButtons(MaterialButton[] buttons, int x, int y, int usable, int minHeight, bool apply)
        {
            int gapX = Theme.S(10);
            int gapY = Theme.S(8);
            int cx = x;
            int cy = y;
            int rowHeight = 0;

            foreach (MaterialButton btn in buttons)
            {
                if (!btn.Visible) continue;
                if (minHeight > 0) btn.MinHeight = minHeight;
                btn.FitToContent();
                int w = Math.Min(btn.Width, usable);
                int h = btn.Height;
                if (cx > x && cx + w > x + usable)
                {
                    cy += rowHeight + gapY;
                    cx = x;
                    rowHeight = 0;
                }
                if (apply) btn.SetBounds(cx, cy, w, h);
                cx += w + gapX;
                rowHeight = Math.Max(rowHeight, h);
            }
            return cy + rowHeight;
        }

        /// <summary>面板所需高度（与 LayoutCore 完全一致，按可用宽度换行计算）。</summary>
        public int PreferredHeight
        {
            get { return LayoutCore(Width > 0 ? Width : Theme.S(760), false); }
        }

        // ---------- 绘制 ----------
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Gfx.Smooth(g);

            Font titleFont = Theme.Font(3f, FontStyle.Bold);
            float titleLine = Theme.LineHeight(titleFont);
            Gfx.DrawText(g, "邮箱登录", titleFont, Theme.TextPrimary,
                new RectangleF(Theme.S(22), Theme.S(14), Math.Max(Theme.S(80), Width - Theme.S(44)), titleLine), Typography.SingleLine);

            Font subFont = Theme.Font(-0.8f, FontStyle.Regular);
            float subLine = Theme.LineHeight(subFont);
            Gfx.DrawText(g, "outlook.office365.com · 993 · SSL/TLS · OAuth2 / Modern Auth", subFont, Theme.TextSecondary,
                new RectangleF(Theme.S(22), Theme.S(14) + titleLine, Math.Max(Theme.S(80), Width - Theme.S(44)), subLine), Typography.SingleLine);

            Color statusColor = string.IsNullOrEmpty(_account) ? Theme.Warning : Theme.Success;
            string status = string.IsNullOrEmpty(_account)
                ? _statusMessage
                : string.Format("{0} · {1}", _account, _statusMessage);
            float statusX = _statusRect.X + (_spinner.Visible ? Theme.S(24) : 0);
            Gfx.DrawText(g, status, Theme.Font(-0.3f, FontStyle.Regular), _busy ? Theme.TextSecondary : statusColor,
                new RectangleF(statusX, _statusRect.Y, Math.Max(Theme.S(60), _statusRect.Right - statusX), _statusRect.Height),
                Typography.SingleLine);

            if (!_deviceRect.IsEmpty)
            {
                Gfx.FillRounded(g, _deviceRect, Theme.RadiusSmall, Theme.AccentSoft);
                float line1 = Theme.LineHeight(Theme.Font(-0.5f, FontStyle.Regular));
                float line2 = Theme.LineHeight(Theme.Font(1.5f, FontStyle.Bold));
                Gfx.DrawText(g, "请在浏览器打开 " + (string.IsNullOrEmpty(_deviceUrl) ? "https://microsoft.com/devicelogin" : _deviceUrl),
                    Theme.Font(-0.5f, FontStyle.Regular), Theme.TextPrimary,
                    new RectangleF(_deviceRect.X + Theme.S(14), _deviceRect.Y + Theme.S(8), Math.Max(Theme.S(60), _deviceRect.Width - Theme.S(210)), line1),
                    Typography.SingleLine);
                Gfx.DrawText(g, "设备代码：" + _deviceCode, Theme.Font(1.5f, FontStyle.Bold), Theme.AccentText,
                    new RectangleF(_deviceRect.X + Theme.S(14), _deviceRect.Y + Theme.S(8) + line1, Math.Max(Theme.S(60), _deviceRect.Width - Theme.S(170)), line2),
                    Typography.SingleLine);
            }

            if (_advancedReveal > 0.01d)
            {
                Font labelFont = Theme.Font(-1f, FontStyle.Regular);
                Gfx.DrawText(g, "客户端 ID", labelFont, Theme.TextSecondary, _clientIdLabelRect, Typography.SingleLineMiddle);
                Gfx.DrawText(g, "租户", labelFont, Theme.TextSecondary, _tenantLabelRect, Typography.SingleLineMiddle);
                Gfx.DrawText(g, "权限范围", labelFont, Theme.TextSecondary, _scopesLabelRect, Typography.SingleLineMiddle);
                Gfx.DrawText(g, "收信方式", labelFont, Theme.TextSecondary, _mailModeLabelRect, Typography.SingleLineMiddle);
                Gfx.DrawText(g, "默认使用公共客户端 ID（可直接登录）；也可替换为你在 Entra ID 注册的应用（重定向 URI：http://localhost，勾选“允许公共客户端流”）。收信方式：Microsoft Graph 需先同意 Mail.Read；IMAP 需邮箱已开启 IMAP。",
                    Theme.Font(-1.5f, FontStyle.Regular), Theme.TextSecondary, _hintRect, Typography.Wrap);
            }
        }
    }
}
