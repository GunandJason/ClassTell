using System;
using System.Text;
using System.Threading;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Security;

namespace ClassTell.SelfTest
{
    /// <summary>
    /// 邮箱连接诊断（ClassTell.SelfTest.exe --mail-check）：
    /// 静默取令牌 → 打印令牌声明（aud / scp / upn / tid，不含令牌本体）→
    /// 连接 IMAP 并打印服务器原始响应，用于定位“登录成功但认证失败”的具体原因。
    /// </summary>
    internal static class MailDiag
    {
        public static int Run(string tenantOverride)
        {
            string originalTenant = Settings.Tenant;
            if (!string.IsNullOrEmpty(tenantOverride))
            {
                Console.WriteLine("（临时把租户覆盖为 " + tenantOverride + "，诊断结束会自动还原为 " + originalTenant + "）");
                Settings.Tenant = tenantOverride;
            }
            try
            {
                return RunCore();
            }
            finally
            {
                Settings.Tenant = originalTenant;
            }
        }

        private static int RunCore()
        {
            Console.WriteLine("=== 邮箱连接诊断（只读，不会修改邮箱内容）===");
            Console.WriteLine("ClientId     : " + Mask(Settings.ClientId));
            Console.WriteLine("Tenant       : " + Settings.Tenant);
            Console.WriteLine("Authority    : " + AuthService.AuthorityUrl(Settings.Tenant));
            Console.WriteLine("Scopes(配置) : " + Settings.Scopes);
            Console.WriteLine("Scopes(解析) : " + string.Join(" ", AuthService.ParseScopes(Settings.Scopes)));
            Console.WriteLine("IMAP         : " + Settings.ImapHost + ":" + Settings.ImapPort + " SSL/TLS");
            Console.WriteLine("SASL 机制    : XOAUTH2（user=<账号>^Aauth=Bearer <令牌>^A^A）");
            Console.WriteLine("账号(设置)   : " + Show(Settings.Account));

            var auth = new AuthService();
            string cached = null;
            try
            {
                cached = auth.GetCachedAccountNameAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Console.WriteLine("枚举缓存账号失败: " + Describe(ex));
            }
            Console.WriteLine("账号(令牌缓存): " + Show(cached));

            string token;
            try
            {
                token = auth.GetAccessTokenSilentAsync(CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Console.WriteLine("取令牌失败: " + Describe(ex));
                Console.WriteLine("→ 结论：令牌换取失败，需要重新登录（关于 → 邮箱登录）。");
                return 2;
            }

            Console.WriteLine("访问令牌     : 已获取（长度 " + token.Length + "，内容不回显）");
            Console.WriteLine("令牌格式     : " + (token.IndexOf('.') > 0 ? "JWT（Entra v2，IMAP 可接受）" : "不透明令牌（旧版 MSA/MSSTS，IMAP 会拒绝）"));
            PrintClaims(token);
            PrintTokenCache();
            ProbeRestApi(token);

            string account = !string.IsNullOrEmpty(Settings.Account) ? Settings.Account : cached;
            return TryImap(account, token);
        }

        private static int TryImap(string account, string token)
        {
            if (string.IsNullOrEmpty(account))
            {
                Console.WriteLine("→ 结论：没有可用账号，请先登录。");
                return 3;
            }

            using (var client = new ImapClient())
            {
                client.Timeout = 30000;
                Console.WriteLine();
                Console.WriteLine("连接 " + Settings.ImapHost + ":" + Settings.ImapPort + " …");
                try
                {
                    client.Connect(Settings.ImapHost, Settings.ImapPort, SecureSocketOptions.SslOnConnect);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("  连接失败: " + Describe(ex));
                    Console.WriteLine("→ 结论：网络/端口被拦截，无法到达 IMAP 服务器。");
                    return 4;
                }

                Console.WriteLine("  已连接；服务器声明的认证机制: " +
                    (client.AuthenticationMechanisms.Count == 0 ? "(未声明)" : string.Join(", ", client.AuthenticationMechanisms)));
                Console.WriteLine("  使用账号 " + account + " 进行 XOAUTH2 认证 …");

                try
                {
                    client.Authenticate(new SaslMechanismOAuth2(account, token));
                    Console.WriteLine("  ✓ IMAP OAuth2 认证成功。");
                    try { client.Disconnect(true); } catch (Exception) { }
                    Console.WriteLine("→ 结论：账号与令牌都正常，可正常收信。");
                    return 0;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("  ✗ IMAP 认证失败: " + Describe(ex));
                }

                // 再试一次并捕获服务器发回的原始 XOAUTH2 错误文本（Exchange 会回 JSON，含期望的 scope）
                try
                {
                    var raw = new RawOAuth2(account, token);
                    client.Authenticate(raw);
                    Console.WriteLine("  ✓ 第二次认证却成功了（首次失败可能是瞬时问题）。");
                    try { client.Disconnect(true); } catch (Exception) { }
                    return 0;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("  原始认证响应: " + Describe(ex));
                }

                try { client.Disconnect(false); } catch (Exception) { }
            }

            Console.WriteLine("→ 结论：登录与令牌都正常（令牌已成功获取），是 IMAP 服务器拒绝了该令牌。");
            Console.WriteLine("   对 outlook.office365.com 而言，“令牌有效 + 只有一句 AUTHENTICATE failed”");
            Console.WriteLine("   基本都是邮箱未开启 IMAP（新注册的个人 Outlook.com 账号默认关闭）：");
            Console.WriteLine("   1) Outlook.com 个人账号：https://outlook.live.com → 设置 → 邮件 → 同步电子邮件 →");
            Console.WriteLine("      打开“允许设备和应用使用 POP 和 IMAP”→ 保存 → 回软件点“立即收取”。");
            Console.WriteLine("   2) Microsoft 365：管理员在 Exchange 管理中心启用 IMAP");
            Console.WriteLine("      （Get-CASMailbox -Identity <邮箱> | fl ImapEnabled 应为 True）。");
            Console.WriteLine("   3) 若已开启仍失败：确认登录用的是账号主邮箱（别名地址会被 IMAP 拒绝），");
            Console.WriteLine("      并在“高级设置 → 客户端 ID”里换成自己注册的应用后重新登录。");
            Console.WriteLine("   4) 说明：个人账号取到的 outlook.office.com 令牌是不透明的 MSA(MSSTS) 令牌，属正常形态；");
            Console.WriteLine("      若 1~3 全部确认无误，请把本次完整输出发给开发者进一步定位。");
            return 5;
        }

        private static void PrintClaims(string jwt)
        {
            string payload = DecodeJwtPayload(jwt);
            if (payload == null)
            {
                Console.WriteLine("令牌声明     : 不是 JWT（无法解析），请把上面的失败信息反馈给开发者。");
                return;
            }

            Console.WriteLine("令牌声明     :");
            string[] keys = { "aud", "scp", "roles", "upn", "preferred_username", "unique_name", "tid", "ver", "appid", "iss" };
            foreach (string key in keys)
            {
                string value = Extract(payload, key);
                if (!string.IsNullOrEmpty(value)) Console.WriteLine("  " + key.PadRight(19) + ": " + value);
            }

            string exp = Extract(payload, "exp");
            long seconds;
            if (!string.IsNullOrEmpty(exp) && long.TryParse(exp, out seconds))
            {
                DateTime local = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(seconds).ToLocalTime();
                Console.WriteLine("  exp(本地时间)      : " + local.ToString("yyyy-MM-dd HH:mm:ss") +
                                  (local < DateTime.Now ? "  ← 已过期" : ""));
            }
        }

        private static string DecodeJwtPayload(string jwt)
        {
            string[] parts = jwt.Split('.');
            if (parts.Length < 2) return null;
            string s = parts[1].Replace('-', '+').Replace('_', '/');
            if (s.Length % 4 == 2) s += "==";
            else if (s.Length % 4 == 3) s += "=";
            try { return Encoding.UTF8.GetString(Convert.FromBase64String(s)); }
            catch (FormatException) { return null; }
        }

        /// <summary>从 JSON 里取一个简单键值（字符串/数组/数字），只用于诊断回显。</summary>
        private static string Extract(string json, string key)
        {
            string needle = "\"" + key + "\"";
            int i = json.IndexOf(needle, StringComparison.Ordinal);
            if (i < 0) return null;
            int colon = json.IndexOf(':', i + needle.Length);
            if (colon < 0) return null;
            int s = colon + 1;
            while (s < json.Length && char.IsWhiteSpace(json[s])) s++;
            if (s >= json.Length) return null;
            if (json[s] == '"')
            {
                int e = json.IndexOf('"', s + 1);
                return e < 0 ? null : json.Substring(s + 1, e - s - 1);
            }
            if (json[s] == '[')
            {
                int e = json.IndexOf(']', s);
                return e < 0 ? null : json.Substring(s, e - s + 1);
            }
            int end = s;
            while (end < json.Length && json[end] != ',' && json[end] != '}') end++;
            return json.Substring(s, end - s).Trim();
        }

        private static string Describe(Exception ex)
        {
            var sb = new StringBuilder();
            int depth = 0;
            for (Exception e = ex; e != null && depth < 5; e = e.InnerException, depth++)
            {
                if (depth > 0) sb.Append("  ←  ");
                sb.Append(e.GetType().Name).Append(": ").Append(e.Message);
                ImapCommandException ice = e as ImapCommandException;
                if (ice != null)
                    sb.Append("  [Response=").Append(ice.Response).Append(", 服务器原文=").Append(ice.ResponseText).Append("]");
            }
            return sb.ToString();
        }

        private static string Show(string value)
        {
            return string.IsNullOrEmpty(value) ? "(空)" : value;
        }

        /// <summary>回显 MSAL 缓存里的令牌种类（只打印 scope 与令牌格式，不打印令牌本体）。</summary>
        private static void PrintTokenCache()
        {
            Console.WriteLine();
            Console.WriteLine("MSAL 缓存 " + AppPaths.TokenCacheFile);
            try
            {
                if (!System.IO.File.Exists(AppPaths.TokenCacheFile))
                {
                    Console.WriteLine("  (没有缓存文件)");
                    return;
                }

                string txt = Encoding.UTF8.GetString(System.IO.File.ReadAllBytes(AppPaths.TokenCacheFile));
                foreach (System.Text.RegularExpressions.Match m in
                         System.Text.RegularExpressions.Regex.Matches(txt, "\"authority_type\":\"([^\"]*)\""))
                    Console.WriteLine("  账号类型 authority_type = " + m.Groups[1].Value + "（MSSTS = 个人微软账号）");

                foreach (System.Text.RegularExpressions.Match m in
                         System.Text.RegularExpressions.Regex.Matches(txt, "\"environment\":\"([^\"]*)\""))
                {
                    Console.WriteLine("  令牌服务 environment   = " + m.Groups[1].Value);
                    break;
                }

                foreach (System.Text.RegularExpressions.Match m in
                         System.Text.RegularExpressions.Regex.Matches(
                             txt, "\"secret\":\"([^\"]{20,})\",\"credential_type\":\"AccessToken\""))
                {
                    string secret = m.Groups[1].Value;
                    Console.WriteLine("  已缓存访问令牌格式     = " +
                        (secret.IndexOf('.') > 0 ? "JWT（Entra v2）" : "不透明令牌（旧版 MSA/MSSTS）"));
                }

                foreach (System.Text.RegularExpressions.Match m in
                         System.Text.RegularExpressions.Regex.Matches(txt, "\"target\":\"([^\"]*)\""))
                    Console.WriteLine("  已缓存权限 scope       = " + m.Groups[1].Value);
            }
            catch (Exception ex)
            {
                Console.WriteLine("  读取缓存失败: " + ex.Message);
            }
        }

        /// <summary>
        /// 用同一个令牌访问 outlook.office.com 资源（Outlook REST v2.0），
        /// 用于区分“令牌/受众不对”和“邮箱未开启 IMAP”两类失败原因。
        /// </summary>
        private static void ProbeRestApi(string token)
        {
            const string url = "https://outlook.office.com/api/v2.0/me";
            Console.WriteLine();
            Console.WriteLine("探测 " + url + " （判断令牌本身是否被 outlook.office.com 接受）…");
            try
            {
                var req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
                req.Method = "GET";
                req.Timeout = 20000;
                req.UserAgent = "ClassTell-Diag/1.0";
                req.Headers["Authorization"] = "Bearer " + token;
                using (var resp = (System.Net.HttpWebResponse)req.GetResponse())
                {
                    Console.WriteLine("  HTTP " + (int)resp.StatusCode + " " + resp.StatusDescription +
                                      " → 令牌有效，受众正确（问题在 IMAP 侧：邮箱未开启 IMAP 或账号不是主别名）");
                }
            }
            catch (System.Net.WebException ex)
            {
                var resp = ex.Response as System.Net.HttpWebResponse;
                if (resp == null)
                {
                    Console.WriteLine("  请求未能完成: " + ex.Message);
                    return;
                }
                string body;
                using (var reader = new System.IO.StreamReader(resp.GetResponseStream()))
                    body = reader.ReadToEnd();
                Console.WriteLine("  HTTP " + (int)resp.StatusCode + " " + resp.StatusDescription);
                for (int i = 0; i < resp.Headers.Count; i++)
                    Console.WriteLine("  响应头 " + resp.Headers.GetKey(i) + ": " + resp.Headers.Get(i));
                Console.WriteLine("  响应体: " + (body.Length == 0 ? "(空)" : (body.Length > 500 ? body.Substring(0, 500) + "…" : body)));
                Console.WriteLine("  → 若为“Invalid audience / InvalidAuthenticationToken”，说明令牌受众不对（scope 配置问题）；");
                Console.WriteLine("     若为“401 Unauthorized（无详细错误）”，说明令牌有效但该账号/资源被拒绝。");
            }
        }

        /// <summary>
        /// 自定义 XOAUTH2 机制：把服务器发回的错误原文（通常是 JSON）原样打印出来，
        /// Exchange 会在其中给出期望的 scope / 错误原因。
        /// </summary>
        private sealed class RawOAuth2 : SaslMechanism
        {
            private readonly string _user;
            private readonly string _token;

            public RawOAuth2(string user, string token) : base(user, token)
            {
                _user = user;
                _token = token;
            }

            public override string MechanismName { get { return "XOAUTH2"; } }

            public string RawText { get; private set; }

            protected override byte[] Challenge(byte[] token, int startIndex, int length, System.Threading.CancellationToken cancellationToken)
            {
                if (token == null)
                    return Encoding.UTF8.GetBytes("user=" + _user + "\x01auth=Bearer " + _token + "\x01\x01");

                RawText = Encoding.UTF8.GetString(token, startIndex, length);
                Console.WriteLine("  服务器原始错误: " + RawText);
                return new byte[0];
            }
        }

        private static string Mask(string value)
        {
            if (string.IsNullOrEmpty(value)) return "(未配置)";
            return value.Length <= 8 ? value : value.Substring(0, 8) + "…";
        }
    }
}
