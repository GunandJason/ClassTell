using System;
using System.Drawing;
using System.Windows.Forms;

namespace ClassTell
{
    /// <summary>
    /// 通知器：只使用系统通知（托盘气泡 = Shell_NotifyIcon 系统通知 API），
    /// 不再弹出任何软件自有的浮层窗口；需要用户确认的错误由宿主用系统对话框呈现。
    /// </summary>
    internal sealed class Notifier : IDisposable
    {
        private readonly NotifyIcon _icon;
        private readonly Control _uiOwner;

        public Notifier(Control uiOwner)
        {
            _uiOwner = uiOwner;
            _icon = new NotifyIcon
            {
                Icon = IconFactory.AppIcon,
                Text = "ClassTell · 邮件指令提醒",
                Visible = true
            };
            _icon.BalloonTipClicked += (s, e) => RaiseClicked(null);
            _icon.MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left) RaiseClicked(null);
            };
        }

        /// <summary>用户点击通知 / 托盘图标时触发（可能来自任意线程）。</summary>
        public event Action<MessageItem> NotificationClicked;

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
                    _icon.BalloonTipIcon = ToolTipIcon.None;
                    _icon.ShowBalloonTip(10000);
                    AppLog.Info("已发送系统通知：" + tipTitle);
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

        /// <summary>非呼叫类信息提示（连接状态等），只使用系统通知区。</summary>
        public void NotifyInfo(string title, string message)
        {
            Dispatch(() =>
            {
                try
                {
                    _icon.BalloonTipTitle = Typography.Truncate(title, 60);
                    _icon.BalloonTipText = Typography.Truncate(message, 240);
                    _icon.ShowBalloonTip(6000);
                }
                catch (Exception ex) { AppLog.Exception_("系统通知失败", ex); }
            });
        }

        private void RaiseClicked(MessageItem item)
        {
            Action<MessageItem> handler = NotificationClicked;
            if (handler != null) handler(item);
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
            }
            catch (Exception) { }
        }
    }
}
