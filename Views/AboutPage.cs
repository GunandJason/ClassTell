using System;
using System.Drawing;
using System.Windows.Forms;

namespace ClassTell
{

    /// <summary>
    /// 关于页：邮箱登录（OAuth2）、界面设置（字体大小滑块 / 深色模式）、开发者信息（版本 + info.txt）。
    /// 内容放在平滑滚动容器中，卡片间距与配色遵循 Material 规范。
    /// </summary>
    internal sealed class AboutPage : MotionControl
    {
        private readonly SmoothScrollPanel _scroll;
        private readonly CardPanel _loginCard;
        private readonly LoginPanel _login;
        private readonly SettingsCard _settings;
        private readonly DevInfoCard _devInfo;

        public AboutPage(AuthService auth)
        {
            BackColor = Theme.Window;

            _scroll = new SmoothScrollPanel();
            Controls.Add(_scroll);

            _loginCard = new CardPanel { Shadow = true };
            _login = new LoginPanel(auth) { Visible = true };
            _login.LayoutChanged += () => LayoutChildren();
            _loginCard.Controls.Add(_login);
            _scroll.Host.Controls.Add(_loginCard);

            _settings = new SettingsCard();
            _scroll.Host.Controls.Add(_settings);

            _devInfo = new DevInfoCard();
            _scroll.Host.Controls.Add(_devInfo);

            LayoutChildren();
        }

        public LoginPanel Login { get { return _login; } }

        /// <summary>页面自带底色，需要跟随深浅模式。</summary>
        protected override Color ThemeBackColor { get { return Theme.Window; } }

        public void RefreshLoginState()
        {
            _login.RefreshState();
        }

        public void ReloadInfo()
        {
            _devInfo.ReloadInfo();
            LayoutChildren();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            _scroll.SetBounds(0, 0, ClientSize.Width, ClientSize.Height);
            LayoutChildren();
        }

        protected override void OnThemeChanged()
        {
            LayoutChildren();
        }

        private void LayoutChildren()
        {
            int w = ClientSize.Width;
            if (w <= Theme.S(160)) return;

            int pad = Theme.PagePadding;
            int gap = Theme.Gap;
            int contentW = Math.Max(Theme.S(200), w - pad * 2 - _scroll.BarWidth - Theme.S(4));

            int y = pad;
            int loginHeight = _login.PreferredHeight + Theme.S(18);
            _loginCard.SetBounds(pad, y, contentW, loginHeight);
            _login.SetBounds(Theme.S(10), Theme.S(8), Math.Max(Theme.S(120), contentW - Theme.S(20)), Math.Max(Theme.S(80), loginHeight - Theme.S(16)));
            y += loginHeight + gap;

            int settingsHeight = _settings.PreferredHeight;
            _settings.SetBounds(pad, y, contentW, settingsHeight);
            y += settingsHeight + gap;

            int devHeight = _devInfo.PreferredHeight;
            _devInfo.SetBounds(pad, y, contentW, devHeight);
            y += devHeight + pad;

            _scroll.ContentHeight = y;
        }
    }
}
