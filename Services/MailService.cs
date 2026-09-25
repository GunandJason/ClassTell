using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using MimeKit;

namespace ClassTell
{
    internal enum MailState
    {
        Stopped,
        Connecting,
        Authenticating,
        Syncing,
        Listening,
        AuthRequired,
        Error
    }

    internal sealed class MailStatusEventArgs : EventArgs
    {
        public MailStatusEventArgs(MailState state, string message) : this(state, message, null) { }

        public MailStatusEventArgs(MailState state, string message, string hint)
        {
            State = state;
            Message = message;
            Hint = hint;
        }

        public MailState State { get; private set; }
        public string Message { get; private set; }

        /// <summary>需要用户处理的故障给出的可执行提示（宿主用系统对话框呈现一次）；无需处理时为 null。</summary>
        public string Hint { get; private set; }
    }

    /// <summary>
    /// 邮件接收服务：outlook.office365.com:993 + SSL/TLS + OAuth2。
    /// 打开 INBOX 后优先使用 IMAP IDLE 实时推送，不支持时退化为轮询；断线自动重连（指数退避）。
    /// </summary>
    internal sealed class MailService : IDisposable
    {
        private readonly AuthService _auth;
        private readonly HashSet<uint> _processed = new HashSet<uint>();
        private CancellationTokenSource _cts;
        private Task _loop;
        private TaskCompletionSource<bool> _wakeSource;
        private bool _backlogDone;
        private UniqueId _nextUid = UniqueId.Invalid;

        public MailService(AuthService auth) { _auth = auth; }

        /// <summary>新消息（在后台线程触发，界面需自行切回 UI 线程）。</summary>
        public event Action<MessageItem> MessageReceived;

        /// <summary>状态变化（在后台线程触发）。</summary>
        public event Action<MailStatusEventArgs> StatusChanged;

        public bool IsRunning { get { return _loop != null && !_loop.IsCompleted; } }

        public void Start()
        {
            if (IsRunning) return;
            _wakeSource = null;
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _loop = Task.Run(() => LoopAsync(token), token);
            AppLog.Info("启动邮件接收服务");
        }

        public void Stop()
        {
            try { if (_cts != null) _cts.Cancel(); }
            catch (Exception) { }
            RequestRefresh();
            AppLog.Info("停止邮件接收服务");
        }

        /// <summary>请求立即检查一次新邮件（会打断 IDLE）。</summary>
        public void RequestRefresh()
        {
            TaskCompletionSource<bool> tcs = Volatile.Read(ref _wakeSource);
            if (tcs != null) tcs.TrySetResult(true);
        }

        public void Dispose()
        {
            Stop();
            try
            {
                if (_cts != null) _cts.Dispose();
            }
            catch (Exception) { }
        }

        private void Report(MailState state, string message)
        {
            Report(state, message, null);
        }

        private void Report(MailState state, string message, string hint)
        {
            Action<MailStatusEventArgs> handler = StatusChanged;
            if (handler != null)
            {
                try { handler(new MailStatusEventArgs(state, message, hint)); }
                catch (Exception ex) { AppLog.Exception_("状态回调异常", ex); }
            }
        }

        private async Task LoopAsync(CancellationToken ct)
        {
            int failures = 0;
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await RunSessionAsync(ct);
                    failures = 0;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (AuthRequiredException ex)
                {
                    AppLog.Warn("需要重新登录：" + ex.Message);
                    Report(MailState.AuthRequired, ex.Message);
                    break;
                }
                catch (Exception ex)
                {
                    failures++;
                    AppLog.Exception_("IMAP 会话中断", ex);
                    Report(MailState.Error, Friendly(ex), HintFor(ex));
                    int wait = Math.Min(60, 5 * Math.Max(1, failures));
                    await Task.Delay(wait * 1000, ct).ContinueWith(t => { });
                    if (ct.IsCancellationRequested) break;
                }
            }
            Report(MailState.Stopped, "已停止接收邮件");
        }

        /// <summary>把异常转换成界面上可读的一句话（Outlook 的认证失败会指向“未开启 IMAP”）。</summary>
        internal static string Friendly(Exception ex)
        {
            if (ex is AuthenticationException)
                return IsOutlookHost() ? "认证被拒绝：邮箱需开启 IMAP" : "认证失败：" + ex.Message;
            if (ex is ImapCommandException) return "服务器拒绝了请求：" + ex.Message;
            if (ex is ImapProtocolException) return "协议错误：" + ex.Message;
            if (ex is System.Net.Sockets.SocketException) return "无法连接服务器：" + ex.Message;
            if (ex is IOException) return "网络异常：" + ex.Message;
            return ex.GetType().Name + "：" + ex.Message;
        }

        /// <summary>需要用户处理的故障给出可执行提示；无需用户处理时返回 null。</summary>
        internal static string HintFor(Exception ex)
        {
            if (ex is AuthenticationException && IsOutlookHost()) return AuthRejectedHint();
            return null;
        }

        /// <summary>是否为 Outlook / Microsoft 365（XOAUTH2 被拒且令牌有效时基本都指向“邮箱未开启 IMAP”）。</summary>
        internal static bool IsOutlookHost()
        {
            string host = Settings.ImapHost ?? string.Empty;
            return host.IndexOf("outlook", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>Outlook 邮箱“令牌有效但 IMAP 认证被拒”的可执行排查提示。</summary>
        internal static string AuthRejectedHint()
        {
            return "登录已经成功（访问令牌有效），但服务器拒绝用该令牌登录 IMAP。最常见的原因是邮箱未开启 IMAP："
                + Environment.NewLine + Environment.NewLine
                + "· Outlook.com 个人账号：打开“设置 → 邮件 → 同步电子邮件”，"
                + "勾选“允许设备和应用使用 POP 和 IMAP”并保存；然后回到本软件点“立即收取”（或按 F5）。"
                + Environment.NewLine
                + "· 工作 / 学校账号：请管理员在 Exchange 管理中心为该邮箱启用 IMAP。"
                + Environment.NewLine
                + "· 已确认开启仍失败：在“关于 → 邮箱登录 → 高级设置”里换成自己注册的应用（客户端 ID），"
                + "并确认登录使用的是账号主邮箱地址。"
                + Environment.NewLine + Environment.NewLine
                + "详细诊断：tests\\ClassTell.SelfTest\\bin\\Release\\net48\\ClassTell.SelfTest.exe --mail-check";
        }

        // ---------- 一次完整会话：连接 → 认证 → 首次扫描 → 监听 ----------
        private async Task RunSessionAsync(CancellationToken ct)
        {
            Report(MailState.Connecting, "正在连接 " + Settings.ImapHost + ":" + Settings.ImapPort + " …");
            string token = await _auth.GetAccessTokenSilentAsync(ct);

            string account = Settings.Account;
            if (string.IsNullOrEmpty(account)) account = await _auth.GetCachedAccountNameAsync();
            if (string.IsNullOrEmpty(account)) throw new AuthRequiredException("未取到账号信息，请重新登录邮箱");

            using (var client = new ImapClient())
            {
                client.Timeout = 30000;
                await client.ConnectAsync(Settings.ImapHost, Settings.ImapPort, SecureSocketOptions.SslOnConnect, ct);

                Report(MailState.Authenticating, "正在使用 OAuth2 (Modern Auth) 认证 …");
                try
                {
                    await client.AuthenticateAsync(new SaslMechanismOAuth2(account, token), ct);
                }
                catch (AuthenticationException ex)
                {
                    // 令牌已经取到（说明登录本身成功），此处的失败通常与邮箱侧设置有关
                    AppLog.Warn("IMAP 认证被拒绝：账号=" + account + " 服务器=" + Settings.ImapHost +
                                " 权限=" + Settings.Scopes + " 令牌长度=" + (token == null ? 0 : token.Length) +
                                " 服务器返回=\"" + ex.Message + "\"");
                    throw;
                }
                AppLog.Info("IMAP 连接成功：" + account);

                IMailFolder inbox = client.Inbox;
                await inbox.OpenAsync(Settings.MarkAsSeen ? FolderAccess.ReadWrite : FolderAccess.ReadOnly, ct);

                Report(MailState.Syncing, "正在收取邮件 …");
                await ScanAsync(client, inbox, ct);

                bool idle = client.Capabilities.HasFlag(ImapCapabilities.Idle);
                Report(MailState.Listening, idle ? "已连接 · 实时接收中" : "已连接 · 定时收取中");
                await ListenAsync(client, inbox, idle, ct);
            }
        }

        private async Task ListenAsync(ImapClient client, IMailFolder inbox, bool idle, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                if (!client.IsConnected) throw new IOException("IMAP 连接已断开");

                if (idle)
                {
                    using (var done = CancellationTokenSource.CreateLinkedTokenSource(ct))
                    {
                        Task idleTask = client.IdleAsync(done.Token, ct);
                        await Task.WhenAny(idleTask, WaitForWakeOrTimeoutAsync(ct));
                        done.Cancel();
                        try { await idleTask; }
                        catch (OperationCanceledException) { }
                        catch (Exception ex) { if (!ct.IsCancellationRequested) AppLog.Exception_("退出 IDLE 时出错", ex); }
                    }
                }
                else
                {
                    await WaitForWakeOrTimeoutAsync(ct);
                }

                if (ct.IsCancellationRequested) break;
                if (!client.IsConnected) throw new IOException("IMAP 连接已断开");

                Report(MailState.Syncing, "正在检查新邮件 …");
                await ScanAsync(client, inbox, ct);
                Report(MailState.Listening, idle ? "已连接 · 实时接收中" : "已连接 · 定时收取中");
            }
        }

        /// <summary>等待外部手动刷新或轮询间隔到达。</summary>
        private async Task WaitForWakeOrTimeoutAsync(CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<bool>();
            Volatile.Write(ref _wakeSource, tcs);
            try
            {
                Task delay = Task.Delay(TimeSpan.FromSeconds(Settings.PollSeconds));
                await Task.WhenAny(tcs.Task, delay);
            }
            finally
            {
                if (ReferenceEquals(Volatile.Read(ref _wakeSource), tcs)) Volatile.Write(ref _wakeSource, null);
            }
        }

        // ---------- 扫描 ----------
        private async Task ScanAsync(ImapClient client, IMailFolder inbox, CancellationToken ct)
        {
            if (!_backlogDone)
            {
                _backlogDone = true;
                try
                {
                    UniqueId? nextUid = inbox.UidNext;
                    _nextUid = nextUid.HasValue ? nextUid.Value : UniqueId.Invalid;
                }
                catch (Exception ex) { AppLog.Exception_("读取 UIDNEXT 失败", ex); _nextUid = UniqueId.Invalid; }
                await ScanUnseenBacklogAsync(inbox, ct);
                return;
            }
            await ScanNewUidsAsync(inbox, ct);
        }

        /// <summary>启动时扫描最近的未读邮件（最多 RecentScanCount 封）。</summary>
        private async Task ScanUnseenBacklogAsync(IMailFolder inbox, CancellationToken ct)
        {
            IList<UniqueId> uids;
            try { uids = await inbox.SearchAsync(SearchQuery.NotSeen, ct); }
            catch (Exception ex) { AppLog.Exception_("搜索未读邮件失败", ex); return; }
            if (uids.Count == 0) return;

            var pending = uids.Where(u => !_processed.Contains(u.Id)).ToList();
            int keep = Settings.RecentScanCount;
            if (keep >= 0 && pending.Count > keep)
            {
                int skip = pending.Count - keep;
                foreach (UniqueId u in pending.Take(skip)) _processed.Add(u.Id);
                pending = pending.Skip(skip).ToList();
                AppLog.Info("启动扫描：忽略较早的 " + skip + " 封未读邮件");
            }
            await FetchAsync(inbox, pending, ct);
        }

        /// <summary>监听过程中按 UID 抓取新邮件（即使已被其它客户端读掉也不会漏）。</summary>
        private async Task ScanNewUidsAsync(IMailFolder inbox, CancellationToken ct)
        {
            if (_nextUid == UniqueId.Invalid)
            {
                await ScanUnseenBacklogAsync(inbox, ct);
                return;
            }

            IList<UniqueId> uids;
            try
            {
                uids = await inbox.SearchAsync(SearchQuery.Uids(new UniqueIdRange(_nextUid, UniqueId.MaxValue)), ct);
            }
            catch (Exception ex)
            {
                AppLog.Exception_("按 UID 搜索新邮件失败，改用未读搜索", ex);
                await ScanUnseenBacklogAsync(inbox, ct);
                return;
            }
            if (uids.Count == 0) return;

            uint next = _nextUid.Id;
            var pending = new List<UniqueId>();
            foreach (UniqueId uid in uids)
            {
                if (uid.Id >= next) next = uid.Id + 1;
                if (_processed.Contains(uid.Id)) continue;
                pending.Add(uid);
            }
            _nextUid = new UniqueId(next);
            await FetchAsync(inbox, pending, ct);
        }

        private async Task FetchAsync(IMailFolder inbox, List<UniqueId> pending, CancellationToken ct)
        {
            foreach (UniqueId uid in pending)
            {
                if (ct.IsCancellationRequested) return;
                _processed.Add(uid.Id);

                MimeMessage message;
                try
                {
                    message = await inbox.GetMessageAsync(uid, ct);
                }
                catch (Exception ex)
                {
                    AppLog.Exception_("读取邮件失败 uid=" + uid.Id, ex);
                    continue;
                }

                string reason;
                MessageItem item = CommandParser.Parse(message, uid.Id, out reason);
                if (item == null)
                {
                    AppLog.Info("跳过邮件 uid=" + uid.Id + "：" + reason);
                    continue;
                }

                AppLog.Info("收到指令邮件 uid=" + uid.Id + " 命令=" + item.Command + " 标题=" + item.Title);
                if (Settings.MarkAsSeen)
                {
                    try { await inbox.AddFlagsAsync(uid, MessageFlags.Seen, true, ct); }
                    catch (Exception ex) { AppLog.Exception_("标记已读失败 uid=" + uid.Id, ex); }
                }

                Action<MessageItem> handler = MessageReceived;
                if (handler != null)
                {
                    try { handler(item); }
                    catch (Exception ex) { AppLog.Exception_("消息回调异常", ex); }
                }
            }
            if (_processed.Count > 5000) _processed.Clear();
        }
    }
}


