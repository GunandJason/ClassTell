using System;
using System.Drawing;
using System.Windows.Forms;

namespace ClassTell
{
    /// <summary>
    /// 通知器只负责两件事：
    ///   · 系统通知（托盘气泡 = Shell_NotifyIcon 系统通知 API），用于“呼叫”这类必须提醒的事件；
    ///   · 托盘图标与右键菜单（显示主界面 / 立即收取 / 退出），配合“关闭时驻留托盘”。
    /// 运行状态类信息（正在检查、已登录、已连接、出错…）**不走系统通知**，
    /// 由主窗口用标题栏状态提示（ShellForm.ShowInlineStatus）。
    /// 软件自身不创建任何浮层窗口；需要用户确认的错误由宿主用系统对话框呈现。
    /// </summary>
    internal sealed class Notifier : IDisposable
    {
        private readonly NotifyIcon _icon;
        private readonly Control _uiOwner;
        private readonly ContextMenuStrip _menu;

        public Notifier(Control uiOwner)
        {
            _uiOwner = uiOwner;

            _menu = new ContextMenuStrip();
            _menu.Items.Add("显示主界面", null, (s, e) => Raise(ShowRequested));
            _menu.Items.Add("立即收取", null, (s, e) => Raise(RefreshRequested));
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add("退出 ClassTell", null, (s, e) => Raise(ExitRequested));

            _icon = new NotifyIcon
            {
                Icon = IconFactory.AppIcon,
                Text = "ClassTell · 邮件指令提醒",
                Visible = true,
                ContextMenuStrip = _menu
            };
            _icon.BalloonTipClicked += (s, e) => RaiseClicked(null);
            _icon.MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left) RaiseClicked(null);
            };
        }

        /// <summary>用户点击通知 / 托盘图标时触发（可能来自任意线程）。</summary>
        public event Action<MessageItem> NotificationClicked;

        /// <summary>托盘菜单：显示主界面（从托盘恢复）。</summary>
        public event Action ShowRequested;

        /// <summary>托盘菜单：立即收取一次。</summary>
        public event Action RefreshRequested;

        /// <summary>托盘菜单：退出程序（真正退出，不驻留托盘）。</summary>
        public event Action ExitRequested;

        /// <summary>通知里携带的最后一条消息（用于点击后展示详情）。</summary>
        public MessageItem LastNotified { get; private set; }

        /// <summary>C / call：只用系统通知（托盘气泡），内容为 标题 / 正文（超长省略）/ 来源于。</summary>
        public void NotifyCall(MessageItem item)
        {
            if (item == null) return;
            LastNotified = item;

            string bodyText = Typography.Truncate(item.Body, 120);
            string tipText = Typography.Truncate(bodyText + Environment.NewLine + "来源于：" + item.Source, 240);
            string tipTitle = Typography.Truncate(string.IsNullOrEmpty(item.Title) ? "ClassTell 呼叫" : item.Title, 60);

            Dispatch(() =>
            {
                try
                {
                    _icon.BalloonTipTitle = tipTitle;
                    _icon.BalloonTipText = tipText;
                    _icon.BalloonTipIcon = ToolTipIcon.Info;
                    _icon.ShowBalloonTip(10000);
                    AppLog.Info("已发送系统通知（呼叫）：" + tipTitle);
                }
                catch (Exception ex) { AppLog.Exception_("系统通知失败", ex); }
            });
        }

        /// <summary>配色切换后刷新托盘图标。</summary>
        public void RefreshIcon()
        {
            Dispatch(() =>
            {
                try
                {
                    IconFactory.InvalidateAppIcon();
                    _icon.Icon = IconFactory.AppIcon;
                }
                catch (Exception ex) { AppLog.Exception_("刷新托盘图标失败", ex); }
            });
        }

        private void RaiseClicked(MessageItem item)
        {
            Action<MessageItem> handler = NotificationClicked;
            if (handler != null) handler(item);
        }

        private void Raise(Action handler)
        {
            if (handler == null) return;
            try { handler(); }
            catch (Exception ex) { AppLog.Exception_("托盘菜单动作失败", ex); }
        }

        private void Dispatch(Action action)
        {
            if (action == null) return;
            try
            {
                if (_uiOwner != null && _uiOwner.IsHandleCreated && _uiOwner.InvokeRequired)
                {
                    _uiOwner.BeginInvoke(action);
                    return;
                }
            }
            catch (Exception) { }
            action();
        }

        public void Dispose()
        {
            try
            {
                _icon.Visible = false;
                _icon.Dispose();
                _menu.Dispose();
            }
            catch (Exception) { }
        }
    }
}
