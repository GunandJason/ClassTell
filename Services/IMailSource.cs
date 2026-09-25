using System;

namespace ClassTell
{
    /// <summary>
    /// 收信源抽象：IMAP（<see cref="MailService"/>）与 Microsoft Graph（<see cref="GraphMailService"/>）
    /// 实现同一套事件与生命周期，界面层只依赖本接口。
    /// </summary>
    internal interface IMailSource : IDisposable
    {
        /// <summary>新消息（在后台线程触发，界面需自行切回 UI 线程）。</summary>
        event Action<MessageItem> MessageReceived;

        /// <summary>状态变化（在后台线程触发）。</summary>
        event Action<MailStatusEventArgs> StatusChanged;

        bool IsRunning { get; }
        void Start();
        void Stop();

        /// <summary>请求立即检查一次新邮件。</summary>
        void RequestRefresh();
    }

    /// <summary>按设置里的收信方式（Settings.MailMode）创建收信源。</summary>
    internal static class MailSourceFactory
    {
        public static IMailSource Create(AuthService auth)
        {
            if (Settings.MailMode == "imap") return new MailService(auth);
            return new GraphMailService(auth);
        }

        /// <summary>当前收信方式的中文名（界面与日志用）。</summary>
        public static string ModeName
        {
            get { return Settings.MailMode == "imap" ? "IMAP" : "Microsoft Graph"; }
        }
    }
}
