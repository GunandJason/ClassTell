using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Net.Pop3;
using MailKit.Net.Smtp;
using MailKit.Security;

namespace ClassTell.SelfTest
{
    /// <summary>--mail-check 的命令行选项。</summary>
    internal sealed class DiagOptions
    {
        public string Tenant { get; set; }

        /// <summary>--interactive / --deep：允许为 POP/SMTP/Graph 这几个 scope 走一次设备代码交互同意。</summary>
        public bool Interactive { get; set; }

        /// <summary>--imap-basic：用应用密码做原生 LOGIN 探针（判断邮箱是否真的允许 IMAP 接入）。</summary>
        public bool BasicLogin { get; set; }

        public bool SkipPop { get; set; }
        public bool SkipSmtp { get; set; }
        public bool SkipGraph { get; set; }

        /// <summary>--imap-user &lt;地址&gt;：指定 XOAUTH2 用户名（可重复，优先于自动候选）。</summary>
        public List<string> ImapUsers { get; private set; }

        /// <summary>--out &lt;文件&gt;：把诊断输出同时写入文件，便于反馈。</summary>
        public string OutFile { get; set; }

        public DiagOptions()
        {
            ImapUsers = new List<string>();
        }

        /// <summary>解析命令行；mailCheck 表示是否请求了邮箱诊断。</summary>
        public static DiagOptions Parse(string[] args, out bool mailCheck)
        {
            var opt = new DiagOptions();
            mailCheck = false;
            if (args == null) return opt;

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i] ?? string.Empty;
                if (string.Equals(a, "--mail-check", StringComparison.OrdinalIgnoreCase))
                {
                    mailCheck = true;
                    if (i + 1 < args.Length && !string.IsNullOrEmpty(args[i + 1]) && !args[i + 1].StartsWith("-"))
                    {
                        opt.Tenant = args[i + 1];
                        i++;
                    }
                }
                else if (string.Equals(a, "--interactive", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(a, "--deep", StringComparison.OrdinalIgnoreCase))
                {
                    opt.Interactive = true;
                }
                else if (string.Equals(a, "--imap-basic", StringComparison.OrdinalIgnoreCase))
                {
                    opt.BasicLogin = true;
                }
                else if (a.StartsWith("--imap-user", StringComparison.OrdinalIgnoreCase))
                {
                    string value = a.IndexOf('=') > 0
                        ? a.Substring(a.IndexOf('=') + 1)
                        : (i + 1 < args.Length ? args[++i] : null);
                    if (!string.IsNullOrEmpty(value)) opt.ImapUsers.Add(value.Trim());
                }
                else if (string.Equals(a, "--out", StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 < args.Length) opt.OutFile = args[++i];
                }
                else if (string.Equals(a, "--skip", StringComparison.OrdinalIgnoreCase))
                {
                    string value = i + 1 < args.Length ? args[++i] : string.Empty;
                    foreach (string part in value.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (part.Equals("pop", StringComparison.OrdinalIgnoreCase)) opt.SkipPop = true;
                        if (part.Equals("smtp", StringComparison.OrdinalIgnoreCase)) opt.SkipSmtp = true;
                        if (part.Equals("graph", StringComparison.OrdinalIgnoreCase)) opt.SkipGraph = true;
                    }
                }
            }
            return opt;
        }
    }

    /// <summary>把诊断输出同时写到控制台与文件（--out），便于把完整结果反馈给开发者。</summary>
    internal sealed class TeeWriter : TextWriter
    {
        private readonly TextWriter _console;
        private readonly StreamWriter _file;

        public TeeWriter(TextWriter console, string path)
        {
            _console = console;
            _file = new StreamWriter(path, false, new UTF8Encoding(false));
            _file.AutoFlush = true;
        }

        public override Encoding Encoding { get { return _console.Encoding; } }

        public override void Write(char value)
        {
            _console.Write(value);
            _file.Write(value);
        }

        public override void Write(string value)
        {
            _console.Write(value);
            _file.Write(value);
        }

        public override void WriteLine(string value)
        {
            _console.WriteLine(value);
            _file.WriteLine(value);
        }

        public override void Flush()
        {
            _console.Flush();
            _file.Flush();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try { _file.Flush(); _file.Dispose(); }
                catch (Exception) { }
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// “决定性诊断包”：用同一账号、同一客户端的令牌分别测试 IMAP（多用户名 A/B）、
    /// POP3、SMTP 与 Graph，并按结果给出结论矩阵。
    /// 全部只读：不发送邮件、不取信、不修改邮箱状态。
    /// </summary>
    internal static class MailDiagProbes
    {
        internal const string ImapScope = "https://outlook.office.com/IMAP.AccessAsUser.All";
        internal const string PopScope = "https://outlook.office.com/POP.AccessAsUser.All";
        internal const string SmtpScope = "https://outlook.office.com/SMTP.Send";
        internal const string GraphUserScope = "https://graph.microsoft.com/User.Read";
        internal const string GraphMailScope = "https://graph.microsoft.com/Mail.Read";

        /// <summary>Graph 探针用的 scope（同一资源可在一次请求里一起申请，只需一次同意）。</summary>
        internal static readonly string[] GraphScopes = { GraphUserScope, GraphMailScope };

        /// <summary>
        /// 协议全集：一次交互同意即可覆盖 IMAP/POP/SMTP（Exchange 只校验令牌是否含对应协议权限），
        /// 避免逐个 scope 反复弹设备代码登录。与 Thunderbird 申请的 scope 集合一致。
        /// </summary>
        internal static readonly string[] ProtocolScopes = { ImapScope, PopScope, SmtpScope };

        private const int MaxImapAttempts = 3;
        private const int AttemptGapMs = 2000;

        /// <summary>探针结果汇总（供结论矩阵使用）。</summary>
        internal sealed class DiagResult
        {
            public bool? ImapOk;
            public string ImapWinner;
            public bool? PopOk;
            public bool? SmtpOk;
            public bool? GraphMailOk;
            public string GraphUpn;
            public string IdTokenUser;
            public bool BasicLoginTried;
            public bool? BasicLoginOk;
        }

        /// <summary>生成 XOAUTH2 用户名候选（去重，命令行指定的优先）。</summary>
        internal static List<string> BuildUserCandidates(DiagOptions opt, string idTokenUser, string graphUpn)
        {
            var list = new List<string>();
            if (opt != null)
            {
                foreach (string u in opt.ImapUsers) AddCandidate(list, u);
            }
            AddCandidate(list, Settings.Account);
            AddCandidate(list, idTokenUser);
            AddCandidate(list, graphUpn);
            return list;
        }

        private static void AddCandidate(List<string> list, string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            string v = value.Trim();
            if (v.Length == 0) return;
            foreach (string x in list)
                if (string.Equals(x, v, StringComparison.OrdinalIgnoreCase)) return;
            list.Add(v);
        }

        // ---------- IMAP：多用户名 A/B ----------
        /// <summary>
        /// 用候选用户名逐个尝试 XOAUTH2，返回首个成功的用户名（都失败则返回 false）。
        /// 最多尝试 MaxImapAttempts 次、每次间隔 AttemptGapMs，避免触发账号风控。
        /// </summary>
        internal static bool TryImapUsernames(List<string> candidates, string token, DiagResult result)
        {
            if (candidates == null || candidates.Count == 0)
            {
                Console.WriteLine("  (没有可用的用户名候选)");
                result.ImapOk = false;
                return false;
            }

            int attempts = 0;
            for (int idx = 0; idx < candidates.Count && attempts < MaxImapAttempts; idx++)
            {
                string user = candidates[idx];
                if (attempts > 0) Thread.Sleep(AttemptGapMs);
                attempts++;

                Console.WriteLine();
                Console.WriteLine("IMAP 第 " + attempts + " 次尝试：user=" + user);
                using (var client = new ImapClient())
                {
                    client.Timeout = 30000;
                    try
                    {
                        client.Connect(Settings.ImapHost, Settings.ImapPort, SecureSocketOptions.SslOnConnect);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("  连接失败: " + MailDiag.Describe(ex));
                        Console.WriteLine("→ 结论：网络/端口被拦截，无法到达 IMAP 服务器。");
                        result.ImapOk = false;
                        return false;
                    }

                    if (attempts == 1)
                    {
                        Console.WriteLine("  服务器声明的认证机制: " +
                            (client.AuthenticationMechanisms.Count == 0
                                ? "(未声明)"
                                : string.Join(", ", client.AuthenticationMechanisms)));
                    }

                    try
                    {
                        client.Authenticate(new SaslMechanismOAuth2(user, token));
                        Console.WriteLine("  ✓ IMAP 认证成功：该账号的 XOAUTH2 用户名应当是 " + user);
                        result.ImapOk = true;
                        result.ImapWinner = user;
                        try { client.Disconnect(true); } catch (Exception) { }
                        return true;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("  ✗ IMAP 认证失败: " + MailDiag.Describe(ex));
                    }

                    // 首次尝试时再抓一次服务器原始响应（Exchange 会回 JSON，含期望的 scope / 原因）
                    if (attempts == 1)
                    {
                        try
                        {
                            client.Authenticate(new MailDiag.RawOAuth2(user, token));
                            Console.WriteLine("  ✓ 第二次认证却成功了（首次失败可能是瞬时问题）。");
                            result.ImapOk = true;
                            result.ImapWinner = user;
                            try { client.Disconnect(true); } catch (Exception) { }
                            return true;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("  原始认证响应: " + MailDiag.Describe(ex));
                        }
                    }

                    try { client.Disconnect(false); } catch (Exception) { }
                }
            }

            result.ImapOk = false;
            Console.WriteLine();
            Console.WriteLine("  → 已试过的用户名全部被拒绝（这排除了“用户名用错别名”这一原因）。");
            return false;
        }

        // ---------- SMTP：只认证，不发送任何邮件 ----------
        internal static bool ProbeSmtp(string user, string token, DiagResult result)
        {
            string[] hosts = { "smtp-mail.outlook.com", "smtp.office365.com" };
            foreach (string host in hosts)
            {
                Console.WriteLine();
                Console.WriteLine("SMTP 探针 " + host + ":587 STARTTLS（只做认证，不发送邮件）…");
                using (var client = new SmtpClient())
                {
                    client.Timeout = 30000;
                    try
                    {
                        client.Connect(host, 587, SecureSocketOptions.StartTls);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("  连接失败: " + MailDiag.Describe(ex));
                        continue;
                    }

                    if (client.AuthenticationMechanisms.Count > 0)
                        Console.WriteLine("  服务器声明的认证机制: " + string.Join(", ", client.AuthenticationMechanisms));

                    try
                    {
                        client.Authenticate(new SaslMechanismOAuth2(user, token));
                        Console.WriteLine("  ✓ SMTP 认证成功 → 账号、令牌、客户端 ID、scope 全部正常");
                        result.SmtpOk = true;
                        try { client.Disconnect(true); } catch (Exception) { }
                        return true;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("  ✗ SMTP 认证失败: " + MailDiag.Describe(ex));
                        result.SmtpOk = false;
                    }

                    try { client.Disconnect(false); } catch (Exception) { }
                }
            }
            return false;
        }

        // ---------- POP3：只认证，不下载邮件 ----------
        internal static bool ProbePop3(string user, string token, DiagResult result)
        {
            Console.WriteLine();
            Console.WriteLine("POP3 探针 " + Settings.ImapHost + ":995 SSL（只做认证，不下载邮件）…");
            using (var client = new Pop3Client())
            {
                client.Timeout = 30000;
                try
                {
                    client.Connect(Settings.ImapHost, 995, SecureSocketOptions.SslOnConnect);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("  连接失败: " + MailDiag.Describe(ex));
                    result.PopOk = false;
                    return false;
                }

                if (client.AuthenticationMechanisms.Count > 0)
                    Console.WriteLine("  服务器声明的认证机制: " + string.Join(", ", client.AuthenticationMechanisms));

                try
                {
                    client.Authenticate(new SaslMechanismOAuth2(user, token));
                    Console.WriteLine("  ✓ POP3 认证成功 → 账号、令牌、客户端 ID、scope 全部正常");
                    result.PopOk = true;
                    try { client.Disconnect(true); } catch (Exception) { }
                    return true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("  ✗ POP3 认证失败: " + MailDiag.Describe(ex));
                    result.PopOk = false;
                }

                try { client.Disconnect(false); } catch (Exception) { }
            }
            return false;
        }

        // ---------- Graph /me：微软官方认定的账号标识 ----------
        internal static string ProbeGraphMe(string token)
        {
            const string url = "https://graph.microsoft.com/v1.0/me?$select=id,userPrincipalName,mail,otherMails,displayName";
            Console.WriteLine();
            Console.WriteLine("Graph 探针 " + url + " （取微软官方认定的账号标识）…");
            try
            {
                var req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
                req.Method = "GET";
                req.Timeout = 20000;
                req.UserAgent = "ClassTell-Diag/1.0";
                req.Headers["Authorization"] = "Bearer " + token;
                using (var resp = (System.Net.HttpWebResponse)req.GetResponse())
                using (var reader = new StreamReader(resp.GetResponseStream()))
                {
                    string body = reader.ReadToEnd();
                    Console.WriteLine("  HTTP " + (int)resp.StatusCode + " 响应体: " + Shorten(body, 400));
                    string upn = MailDiag.Extract(body, "userPrincipalName");
                    string mail = MailDiag.Extract(body, "mail");
                    Console.WriteLine("  userPrincipalName = " + (string.IsNullOrEmpty(upn) ? "(空)" : upn));
                    Console.WriteLine("  mail              = " + (string.IsNullOrEmpty(mail) ? "(空)" : mail));
                    Console.WriteLine("  → userPrincipalName 是微软侧认定的账号地址，优先用它做 XOAUTH2 用户名。");
                    return upn;
                }
            }
            catch (System.Net.WebException ex)
            {
                var resp = ex.Response as System.Net.HttpWebResponse;
                if (resp == null)
                {
                    Console.WriteLine("  请求未能完成: " + ex.Message);
                    Console.WriteLine("  （若是“需要同意/未授权”，请加 --interactive 重新运行以授予 Graph User.Read。）");
                    return null;
                }

                string body;
                using (var reader = new StreamReader(resp.GetResponseStream())) body = reader.ReadToEnd();
                Console.WriteLine("  HTTP " + (int)resp.StatusCode + " " + resp.StatusDescription);
                Console.WriteLine("  响应体: " + (body.Length == 0 ? "(空)" : Shorten(body, 400)));
                if ((int)resp.StatusCode == 401 || (int)resp.StatusCode == 403)
                    Console.WriteLine("  → 该 scope 尚未被授予或账号不允许：请用 --interactive 重新运行一次。");
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine("  探测失败: " + MailDiag.Describe(ex));
                return null;
            }
        }

        // ---------- Graph 邮件探针：确认该账号能否用 Graph 收信 ----------
        internal static bool ProbeGraphMail(string token, DiagResult result)
        {
            const string url = "https://graph.microsoft.com/v1.0/me/mailFolders/inbox/messages" +
                               "?$top=2&$select=id,subject,receivedDateTime,isRead";
            Console.WriteLine();
            Console.WriteLine("Graph 邮件探针 " + url + " …（只列元数据，不取正文、不改已读）");
            try
            {
                var req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
                req.Method = "GET";
                req.Timeout = 20000;
                req.UserAgent = "ClassTell-Diag/1.0";
                req.Headers["Authorization"] = "Bearer " + token;
                using (var resp = (System.Net.HttpWebResponse)req.GetResponse())
                using (var reader = new StreamReader(resp.GetResponseStream()))
                {
                    string body = reader.ReadToEnd();
                    Console.WriteLine("  HTTP " + (int)resp.StatusCode + "，返回 " +
                                      CountOccurrences(body, "\"id\":") + " 封（响应体 " + body.Length + " 字节）");
                    Console.WriteLine("  ✓ 该账号可以用 Microsoft Graph 收信（/me/messages 可用）。");
                    result.GraphMailOk = true;
                    return true;
                }
            }
            catch (System.Net.WebException ex)
            {
                var resp = ex.Response as System.Net.HttpWebResponse;
                if (resp == null)
                {
                    Console.WriteLine("  请求未能完成: " + ex.Message);
                    result.GraphMailOk = false;
                    return false;
                }

                string body;
                using (var reader = new StreamReader(resp.GetResponseStream())) body = reader.ReadToEnd();
                Console.WriteLine("  HTTP " + (int)resp.StatusCode + " " + resp.StatusDescription);
                Console.WriteLine("  响应体: " + Shorten(body, 500));
                Console.WriteLine("  → Graph 收信不可用；若错误码为 MailboxNotEnabledForRESTAPI / ErrorAccessDenied，");
                Console.WriteLine("     请确认 scope 含 https://graph.microsoft.com/Mail.Read（Mail.ReadBasic 不含正文）。");
                result.GraphMailOk = false;
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine("  探测失败: " + MailDiag.Describe(ex));
                result.GraphMailOk = false;
                return false;
            }
        }

        private static int CountOccurrences(string text, string needle)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(needle)) return 0;
            int count = 0;
            int i = 0;
            while ((i = text.IndexOf(needle, i, StringComparison.Ordinal)) >= 0)
            {
                count++;
                i += needle.Length;
            }
            return count;
        }

        private static string Shorten(string value, int max)
        {
            if (string.IsNullOrEmpty(value)) return "(空)";
            return value.Length <= max ? value : value.Substring(0, max) + "…";
        }

        // ---------- 原生 LOGIN 探针（应用密码；成功才有决定性） ----------
        /// <summary>
        /// 用应用密码做一次原生 LOGIN：只有在“成功”时才是决定性证据（说明邮箱确实允许 IMAP 接入）。
        /// 密码仅存在内存中，输入时不回显、不写入日志与 --out 文件。
        /// </summary>
        internal static bool ProbeBasicLogin(string user, DiagResult result)
        {
            Console.WriteLine();
            Console.WriteLine("原生 LOGIN 探针（应用密码，可选）");
            Console.WriteLine("  说明：密码不回显、不写日志；只有开启两步验证的账号才有“应用密码”入口");
            Console.WriteLine("        （https://account.live.com/proofs/manage → 应用密码）。");
            Console.Write("  请输入应用密码（直接回车跳过）: ");

            var sb = new StringBuilder();
            while (true)
            {
                ConsoleKeyInfo key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.Enter) break;
                if (key.Key == ConsoleKey.Backspace)
                {
                    if (sb.Length > 0) sb.Length--;
                    continue;
                }
                if (key.KeyChar != '\0' && !char.IsControl(key.KeyChar)) sb.Append(key.KeyChar);
            }
            Console.WriteLine(sb.Length == 0 ? "(已跳过)" : "(已输入 " + sb.Length + " 个字符)");

            result.BasicLoginTried = true;
            if (sb.Length == 0)
            {
                result.BasicLoginOk = null;
                return false;
            }

            string password = sb.ToString();
            sb.Length = 0;
            using (var client = new ImapClient())
            {
                client.Timeout = 30000;
                try
                {
                    client.Connect(Settings.ImapHost, Settings.ImapPort, SecureSocketOptions.SslOnConnect);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("  连接失败: " + MailDiag.Describe(ex));
                    password = null;
                    return false;
                }

                try
                {
                    client.Authenticate(user, password);
                    Console.WriteLine("  ✓ LOGIN 成功 → 该邮箱确实允许 IMAP 接入（基本认证未被停用）");
                    Console.WriteLine("     ⇒ 问题只可能在 OAuth2 侧（XOAUTH2 用户名或令牌），属程序可修复范围。");
                    result.BasicLoginOk = true;
                    password = null;
                    try { client.Disconnect(true); } catch (Exception) { }
                    return true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("  ✗ LOGIN 失败: " + MailDiag.Describe(ex));
                    Console.WriteLine("  （失败不能区分“邮箱未开启 IMAP”与“基本认证已停用/必须用应用密码”，只有成功才是决定性的。）");
                    result.BasicLoginOk = false;
                }
                password = null;
                try { client.Disconnect(false); } catch (Exception) { }
            }
            return false;
        }

        // ---------- 结论矩阵 ----------
        internal static void RenderVerdict(DiagResult r)
        {
            Console.WriteLine();
            Console.WriteLine("=== 结论矩阵（决定性判据）===");
            Console.WriteLine("  IMAP            : " + Tri(r.ImapOk) +
                (string.IsNullOrEmpty(r.ImapWinner) ? string.Empty : "   生效用户名 = " + r.ImapWinner));
            Console.WriteLine("  POP3            : " + Tri(r.PopOk));
            Console.WriteLine("  SMTP            : " + Tri(r.SmtpOk));
            Console.WriteLine("  Graph 邮件      : " + Tri(r.GraphMailOk));
            Console.WriteLine("  LOGIN(应用密码) : " + Tri(r.BasicLoginOk));
            Console.WriteLine("  登录名(id_token): " + Show(r.IdTokenUser));
            Console.WriteLine("  主地址(Graph)   : " + Show(r.GraphUpn));
            Console.WriteLine();

            if (r.ImapOk == true)
            {
                Console.WriteLine("→ 结论：IMAP + XOAUTH2 连接正常，邮箱设置与令牌都没有问题。");
                Console.WriteLine("   若主程序仍提示“认证被拒绝”，请把上面的“生效用户名”填到");
                Console.WriteLine("   “关于 → 邮箱登录 → 高级设置 → 登录用户名”，再点“立即收取”。");
                return;
            }

            if (r.GraphMailOk == true)
            {
                Console.WriteLine("→ 结论：该账号可以用 Microsoft Graph 收信（IMAP 被拒不影响收信能力）。");
                Console.WriteLine("   程序可切到 Graph 收信模式：「关于 → 邮箱登录 → 高级设置 → 收信方式 = Graph」；");
                Console.WriteLine("   首次需要一次浏览器授权同意 Mail.Read，之后静默刷新，无需重复登录。");
                Console.WriteLine();
            }

            if (r.BasicLoginOk == true)
            {
                Console.WriteLine("→ 结论（决定性）：该邮箱确实允许 IMAP 接入，问题在 OAuth2 侧的用户名。");
                Console.WriteLine("   请把本输出发给开发者；修复方向：XOAUTH2 用户名改用");
                Console.WriteLine("   " + (string.IsNullOrEmpty(r.GraphUpn) ? "主别名（https://account.live.com/names/manage）" : r.GraphUpn));
                return;
            }

            bool crossDone = r.SmtpOk.HasValue || r.PopOk.HasValue;
            if (crossDone && (r.SmtpOk == true || r.PopOk == true))
            {
                Console.WriteLine("→ 结论：账号、令牌、客户端 ID、scope 都正常（POP3/SMTP 能通过），");
                Console.WriteLine("   只有 IMAP 被拒绝 ⇒【该邮箱的 IMAP 没有生效】。");
                Console.WriteLine("   处理步骤：");
                Console.WriteLine("     1) 用【同一个账号】打开 https://outlook.live.com/mail/0/options/mail/accounts");
                Console.WriteLine("        （设置 → 邮件 → 同步电子邮件），勾选“允许设备和应用使用 POP 和 IMAP”，点保存；");
                Console.WriteLine("     2) 等 5~10 分钟（设置生效有延迟），再重跑本诊断；");
                Console.WriteLine("     3) 若开关确实已开启仍失败：到 https://account.live.com/Activity 看是否被要求验证身份。");
                return;
            }

            if (crossDone && r.SmtpOk == false && r.PopOk == false)
            {
                Console.WriteLine("→ 结论：三个协议全部被拒绝 ⇒ 不是 IMAP 单协议的问题，而是账号/权限映射问题。");
                Console.WriteLine("   按顺序排查：");
                Console.WriteLine("     1) 加 --interactive 再跑一次，确认 POP/SMTP/Graph 的 scope 确实已授予");
                Console.WriteLine("        （否则上面“IMAP 未生效”的判断不成立）；");
                Console.WriteLine("     2) 打开 https://account.live.com/names/manage 确认主别名，然后用主别名执行：");
                Console.WriteLine("        ClassTell.SelfTest.exe --mail-check --imap-user <主别名>");
                Console.WriteLine("     3) 仍然全失败：把本输出发给开发者（可能需要在“高级设置 → 客户端 ID”");
                Console.WriteLine("        换成自己注册的应用后重新登录）。");
                return;
            }

            Console.WriteLine("→ 结论：跨协议探针未完成（scope 未授予或已被跳过），暂时无法区分");
            Console.WriteLine("   “邮箱未开启 IMAP”与“XOAUTH2 用户名不对”。请执行：");
            Console.WriteLine("     ClassTell.SelfTest.exe --mail-check --interactive");
            Console.WriteLine("   （会打印设备代码，按提示在浏览器完成一次同意；本软件只做只读探测，不发信、不改邮箱。）");
        }

        private static string Tri(bool? value)
        {
            if (value == true) return "✓ 成功";
            if (value == false) return "✗ 失败";
            return "(未测)";
        }

        private static string Show(string value)
        {
            return string.IsNullOrEmpty(value) ? "(未知)" : value;
        }
    }
}
