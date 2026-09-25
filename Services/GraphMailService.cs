using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MimeKit;

namespace ClassTell
{
    /// <summary>Graph 邮件列表里的一条引用（用于排序、去重；正文另行按需取 MIME）。</summary>
    internal sealed class GraphMailRef
    {
        public string Id { get; set; }
        public string Subject { get; set; }
        public DateTime ReceivedLocal { get; set; }
    }

    /// <summary>Graph 返回的错误（带 HTTP 状态与 OData 错误码，便于分类提示）。</summary>
    internal sealed class GraphException : Exception
    {
        public int Status { get; private set; }
        public string Code { get; private set; }
        public string Body { get; private set; }

        public GraphException(int status, string code, string body)
            : base("Microsoft Graph HTTP " + status + (string.IsNullOrEmpty(code) ? string.Empty : "（" + code + "）"))
        {
            Status = status;
            Code = code ?? string.Empty;
            Body = body ?? string.Empty;
        }
    }

    /// <summary>
    /// Microsoft Graph 收信实现（推荐方式）：
    ///   · 登录沿用 MSAL（scope = https://graph.microsoft.com/Mail.Read，同意一次后静默刷新）；
    ///   · 收信 = 轮询 /me/mailFolders/inbox/messages 的未读邮件
    ///     （Graph 的“实时推送”需要公网 https 回调，桌面端不适用，因此用轮询 + “立即收取”手动唤醒）；
    ///   · 正文取 MIME：GET /me/messages/{id}/$value → MimeMessage.Load，
    ///     从而复用与 IMAP 完全相同的解析链路（CommandParser / HtmlText）；
    ///   · 去重按 Graph 消息 id；“启动只回溯最近 N 封未读”与 IMAP 行为一致（RecentScanCount）；
    ///   · 只读：除 MarkAsSeen=true 时把已处理邮件标记为已读外，不修改邮箱任何内容。
    /// </summary>
    internal sealed class GraphMailService : IMailSource
    {
        internal const string BaseUrl = "https://graph.microsoft.com/v1.0";

        private readonly AuthService _auth;
        private readonly HashSet<string> _processed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private CancellationTokenSource _cts;
        private Task _loop;
        private TaskCompletionSource<bool> _wakeSource;
        private bool _backlogDone;

        public GraphMailService(AuthService auth) { _auth = auth; }

        public event Action<MessageItem> MessageReceived;
        public event Action<MailStatusEventArgs> StatusChanged;

        public bool IsRunning { get { return _loop != null && !_loop.IsCompleted; } }

        public void Start()
        {
            if (IsRunning) return;
            _wakeSource = null;
            _cts = new CancellationTokenSource();
            CancellationToken token = _cts.Token;
            _loop = Task.Run(() => LoopAsync(token), token);
            AppLog.Info("启动 Graph 收信（收件箱未读轮询，间隔 " + Settings.PollSeconds + " 秒）");
        }

        public void Stop()
        {
            try { if (_cts != null) _cts.Cancel(); }
            catch (Exception) { }
            RequestRefresh();
            AppLog.Info("停止 Graph 收信");
        }

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

        // ---------- 主循环（未读轮询 + 手动唤醒） ----------
        private async Task LoopAsync(CancellationToken ct)
        {
            int backoff = 5;
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    Report(MailState.Connecting, "正在连接 Microsoft Graph…");
                    string token = await _auth.GetAccessTokenSilentAsync(ct);
                    Report(MailState.Syncing, "正在读取收件箱…");
                    PollOnce(token, ct);
                    Report(MailState.Listening, "已连接（Graph 轮询，间隔 " + Settings.PollSeconds + " 秒）");
                    backoff = 5;
                    await WaitForNextAsync(Settings.PollSeconds, ct);
                }
                catch (OperationCanceledException) { break; }
                catch (AuthRequiredException ex)
                {
                    AppLog.Warn("Graph 收信需要重新登录：" + ex.Message);
                    Report(MailState.AuthRequired, "需要重新登录（Graph）", AuthHint());
                    return;
                }
                catch (GraphException gex)
                {
                    AppLog.Warn("Graph 请求失败：" + gex.Message + " 响应=" + Shorten(gex.Body, 300));
                    if (gex.Status == 401)
                    {
                        Report(MailState.AuthRequired, "Graph 授权已失效（HTTP 401）", AuthHint());
                        return;
                    }

                    if (gex.Status == 403)
                        Report(MailState.Error, "Graph 拒绝访问（HTTP 403）", ForbiddenHint(gex.Code));
                    else
                        Report(MailState.Error, "收信失败：" + gex.Message, RetryHint());

                    await DelayQuietlyAsync(backoff, ct);
                    backoff = Math.Min(backoff * 2, 120);
                }
                catch (Exception ex)
                {
                    AppLog.Exception_("Graph 收信异常", ex);
                    Report(MailState.Error, "收信失败：" + ex.Message, RetryHint());
                    await DelayQuietlyAsync(backoff, ct);
                    backoff = Math.Min(backoff * 2, 120);
                }
            }
        }

        /// <summary>等待下一轮（可被“立即收取”打断）。</summary>
        private async Task WaitForNextAsync(int seconds, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<bool>();
            Volatile.Write(ref _wakeSource, tcs);
            try
            {
                Task delay = Task.Delay(TimeSpan.FromSeconds(seconds), ct);
                await Task.WhenAny(delay, tcs.Task);
            }
            finally
            {
                Volatile.Write(ref _wakeSource, null);
            }
            ct.ThrowIfCancellationRequested();
        }

        private static async Task DelayQuietlyAsync(int seconds, CancellationToken ct)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(seconds), ct); }
            catch (OperationCanceledException) { }
        }

        // ---------- 一轮收取 ----------
        private void PollOnce(string token, CancellationToken ct)
        {
            List<GraphMailRef> refs = ListUnread(token, ct);

            int keep = Settings.RecentScanCount;
            if (!_backlogDone)
            {
                _backlogDone = true;
                if (keep >= 0 && refs.Count > keep)
                {
                    int skip = refs.Count - keep;
                    for (int i = 0; i < skip; i++) _processed.Add(refs[i].Id);
                    AppLog.Info("Graph 启动扫描：忽略较早的 " + skip + " 封未读邮件");
                    refs = refs.GetRange(skip, refs.Count - skip);
                }
            }

            foreach (GraphMailRef r in refs)
            {
                if (ct.IsCancellationRequested) return;
                if (_processed.Contains(r.Id)) continue;
                _processed.Add(r.Id);

                MimeMessage message;
                try
                {
                    message = GetMime(token, r.Id, ct);
                }
                catch (Exception ex)
                {
                    AppLog.Exception_("Graph 读取邮件失败 id=" + r.Id, ex);
                    continue;
                }
                if (message == null) continue;

                string reason;
                MessageItem item = CommandParser.Parse(message, HashId(r.Id), out reason);
                if (item == null)
                {
                    AppLog.Info("跳过邮件 id=" + r.Id + "：" + reason);
                    continue;
                }

                AppLog.Info("收到指令邮件（Graph） id=" + r.Id + " 命令=" + item.Command + " 标题=" + item.Title);
                if (Settings.MarkAsSeen)
                {
                    try { MarkRead(token, r.Id, ct); }
                    catch (Exception ex) { AppLog.Exception_("标记已读失败 id=" + r.Id, ex); }
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

        // ---------- Graph HTTP（同步调用，运行在后台任务里） ----------
        private List<GraphMailRef> ListUnread(string token, CancellationToken ct)
        {
            string json = HttpGetString(UnreadListUrl(100, MailWindow.SinceUtc), token, ct);
            return ParseUnreadList(json);
        }

        private MimeMessage GetMime(string token, string id, CancellationToken ct)
        {
            using (HttpWebResponse resp = Send(MimeUrl(id), token, "GET", null, ct))
            using (Stream stream = resp.GetResponseStream())
            {
                return MimeMessage.Load(stream);
            }
        }

        private void MarkRead(string token, string id, CancellationToken ct)
        {
            using (HttpWebResponse resp = Send(MessageUrl(id), token, "PATCH", "{\"isRead\":true}", ct)) { }
        }

        private static string HttpGetString(string url, string token, CancellationToken ct)
        {
            using (HttpWebResponse resp = Send(url, token, "GET", null, ct))
            using (Stream stream = resp.GetResponseStream())
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                return reader.ReadToEnd();
            }
        }

        /// <summary>发一个 Graph 请求；非 2xx 一律抛出带状态/错误码的 GraphException。</summary>
        private static HttpWebResponse Send(string url, string token, string method, string jsonBody, CancellationToken ct)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = method;
            req.Timeout = 30000;
            req.ReadWriteTimeout = 30000;
            req.UserAgent = "ClassTell/" + Theme.VersionText;
            req.Headers["Authorization"] = "Bearer " + token;
            req.KeepAlive = false;
            if (jsonBody != null)
            {
                req.ContentType = "application/json";
                byte[] payload = Encoding.UTF8.GetBytes(jsonBody);
                req.ContentLength = payload.Length;
                using (Stream s = req.GetRequestStream()) s.Write(payload, 0, payload.Length);
            }

            using (ct.Register(delegate { try { req.Abort(); } catch (Exception) { } }))
            {
                try
                {
                    return (HttpWebResponse)req.GetResponse();
                }
                catch (WebException ex)
                {
                    var resp = ex.Response as HttpWebResponse;
                    if (resp == null) throw;
                    string body;
                    using (Stream s = resp.GetResponseStream())
                    using (var reader = new StreamReader(s, Encoding.UTF8))
                        body = reader.ReadToEnd();
                    int status = (int)resp.StatusCode;
                    resp.Dispose();
                    throw new GraphException(status, ExtractGraphCode(body), body);
                }
            }
        }

        // ---------- URL 构造与 JSON 解析（纯逻辑，可单测） ----------
        /// <summary>
        /// 未读列表 URL。Graph 对“$filter + $orderby 同用”有限制，因此不要求服务端排序，
        /// 排序在本地按 receivedDateTime 做（老邮件在前，与 IMAP 的顺序一致）。
        /// </summary>
        internal static string UnreadListUrl(int top, DateTime sinceUtc)
        {
            if (top < 1) top = 1;
            if (top > 1000) top = 1000;
            string since = sinceUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
            return BaseUrl + "/me/mailFolders/inbox/messages" +
                   "?$filter=isRead%20eq%20false%20and%20receivedDateTime%20ge%20" + since +
                   "&$top=" + top.ToString(CultureInfo.InvariantCulture) +
                   "&$select=id,subject,receivedDateTime";
        }

        /// <summary>默认查询：从“过期窗口”起点开始（打开软件前的旧邮件不会无限回溯）。</summary>
        internal static string UnreadListUrl(int top)
        {
            return UnreadListUrl(top, MailWindow.SinceUtc);
        }

        internal static string MessageUrl(string id)
        {
            return BaseUrl + "/me/messages/" + Uri.EscapeDataString(id ?? string.Empty);
        }

        /// <summary>取 MIME 原文的地址（message/rfc822，可直接交给 MimeMessage.Load）。</summary>
        internal static string MimeUrl(string id)
        {
            return MessageUrl(id) + "/$value";
        }

        /// <summary>解析 Graph 列表 JSON，返回按接收时间升序（老 → 新）的消息引用。</summary>
        internal static List<GraphMailRef> ParseUnreadList(string json)
        {
            var list = new List<GraphMailRef>();
            if (string.IsNullOrEmpty(json)) return list;

            foreach (string obj in SplitValueObjects(json))
            {
                string id = ReadJsonString(obj, "id");
                if (string.IsNullOrEmpty(id)) continue;
                var item = new GraphMailRef();
                item.Id = id;
                item.Subject = ReadJsonString(obj, "subject");
                item.ReceivedLocal = ParseGraphTime(ReadJsonString(obj, "receivedDateTime"));
                list.Add(item);
            }

            list.Sort(delegate (GraphMailRef a, GraphMailRef b)
            {
                return a.ReceivedLocal.CompareTo(b.ReceivedLocal);
            });
            return list;
        }

        /// <summary>
        /// 把 "value":[ ... ] 里的每个对象切出来。正确处理字符串内的花括号、转义引号与嵌套对象，
        /// 避免标题里出现 { } 时解析串位。
        /// </summary>
        internal static List<string> SplitValueObjects(string json)
        {
            var objects = new List<string>();
            if (string.IsNullOrEmpty(json)) return objects;

            int start = json.IndexOf("\"value\"", StringComparison.Ordinal);
            if (start < 0) return objects;
            int bracket = json.IndexOf('[', start);
            if (bracket < 0) return objects;

            int depth = 0;
            int objStart = -1;
            bool inString = false;
            bool escaped = false;

            for (int i = bracket + 1; i < json.Length; i++)
            {
                char c = json[i];
                if (inString)
                {
                    if (escaped) escaped = false;
                    else if (c == '\\') escaped = true;
                    else if (c == '"') inString = false;
                    continue;
                }

                if (c == '"') { inString = true; continue; }
                if (c == '{')
                {
                    if (depth == 0) objStart = i;
                    depth++;
                }
                else if (c == '}')
                {
                    depth--;
                    if (depth <= 0 && objStart >= 0)
                    {
                        objects.Add(json.Substring(objStart, i - objStart + 1));
                        objStart = -1;
                        depth = 0;
                    }
                }
                else if (c == ']' && depth == 0)
                {
                    break;
                }
            }
            return objects;
        }

        /// <summary>取一个 JSON 对象里的字符串字段（支持 \" \\ \n \uXXXX 等转义；键不存在或为 null 时返回 null）。</summary>
        internal static string ReadJsonString(string obj, string key)
        {
            if (string.IsNullOrEmpty(obj)) return null;
            string needle = "\"" + key + "\"";
            int i = obj.IndexOf(needle, StringComparison.Ordinal);
            if (i < 0) return null;
            int colon = obj.IndexOf(':', i + needle.Length);
            if (colon < 0) return null;

            int s = colon + 1;
            while (s < obj.Length && char.IsWhiteSpace(obj[s])) s++;
            if (s >= obj.Length) return null;
            if (obj[s] != '"') return null;   // null / 数字 / 对象：本方法只处理字符串

            var sb = new StringBuilder();
            for (int p = s + 1; p < obj.Length; p++)
            {
                char c = obj[p];
                if (c == '\\')
                {
                    if (p + 1 >= obj.Length) break;
                    char n = obj[++p];
                    if (n == 'n') sb.Append('\n');
                    else if (n == 'r') sb.Append('\r');
                    else if (n == 't') sb.Append('\t');
                    else if (n == 'b') sb.Append('\b');
                    else if (n == 'f') sb.Append('\f');
                    else if (n == 'u' && p + 4 < obj.Length)
                    {
                        int code;
                        if (int.TryParse(obj.Substring(p + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                        {
                            sb.Append((char)code);
                            p += 4;
                        }
                    }
                    else sb.Append(n);
                    continue;
                }
                if (c == '"') break;
                sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>Graph 的 ISO8601 时间（UTC）→ 本地时间。</summary>
        internal static DateTime ParseGraphTime(string value)
        {
            if (string.IsNullOrEmpty(value)) return DateTime.MinValue;
            DateTimeOffset dto;
            if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out dto))
                return dto.ToLocalTime().DateTime;
            return DateTime.MinValue;
        }

        /// <summary>Graph 消息 id → 展示用 uint（FNV-1a；去重仍按完整字符串 id，不会漏信）。</summary>
        internal static uint HashId(string id)
        {
            if (string.IsNullOrEmpty(id)) return 0;
            unchecked
            {
                uint hash = 2166136261;
                for (int i = 0; i < id.Length; i++)
                {
                    hash ^= id[i];
                    hash *= 16777619;
                }
                return hash == 0 ? 1 : hash;
            }
        }

        /// <summary>从 Graph 错误响应里取 OData 错误码（如 ErrorAccessDenied）。</summary>
        internal static string ExtractGraphCode(string body)
        {
            return ReadJsonString(body, "code");
        }

        private static string Shorten(string value, int max)
        {
            if (string.IsNullOrEmpty(value)) return "(空)";
            return value.Length <= max ? value : value.Substring(0, max) + "…";
        }

        /// <summary>需要用户处理时的可执行提示（由宿主用系统对话框展示一次）。</summary>
        private static string AuthHint()
        {
            return "Graph 收信需要一次授权同意：" + Environment.NewLine +
                   "1) 打开「关于 → 邮箱登录」，点“登录”，在浏览器里同意 Mail.Read；" + Environment.NewLine +
                   "2) 回到「消息」页点“立即收取”即可；" + Environment.NewLine +
                   "3) 若账号策略禁止第三方应用，请到「高级设置 → 收信方式」改回 IMAP 再登录。" + Environment.NewLine +
                   "只读诊断：ClassTell.SelfTest.exe --mail-check --interactive";
        }

        private static string ForbiddenHint(string code)
        {
            string head = string.IsNullOrEmpty(code) ? "（HTTP 403）" : "（HTTP 403 " + code + "）";
            return "Microsoft Graph 拒绝访问" + head + "：" + Environment.NewLine +
                   "· ErrorAccessDenied / MailboxNotEnabledForRESTAPI：该邮箱不允许 REST/Graph 访问；" + Environment.NewLine +
                   "· 请确认权限是 https://graph.microsoft.com/Mail.Read（Mail.ReadBasic 不含正文）；" + Environment.NewLine +
                   "· 也可以到「高级设置 → 收信方式」改回 IMAP 后重试。";
        }

        private static string RetryHint()
        {
            return "程序会按退避自动重试；若持续失败，请查看日志 %APPDATA%\\ClassTell\\log.txt，" + Environment.NewLine +
                   "或运行 ClassTell.SelfTest.exe --mail-check --interactive 做只读诊断。";
        }
    }
}
