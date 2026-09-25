using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Identity.Client;

namespace ClassTell
{
    /// <summary>需要重新登录（没有可用账号或令牌已失效）。</summary>
    internal sealed class AuthRequiredException : Exception
    {
        public AuthRequiredException(string message, Exception inner = null) : base(message, inner) { }
    }

    /// <summary>设备代码登录时展示给用户的信息。</summary>
    internal sealed class DeviceCodePrompt
    {
        public string UserCode { get; set; }
        public string VerificationUrl { get; set; }
        public string Message { get; set; }
    }

    /// <summary>
    /// Outlook / Microsoft 365 邮箱登录（OAuth2 / Modern Auth，基于 MSAL）。
    /// 方式一：系统浏览器交互登录；方式二：设备代码登录（无浏览器 / 远程桌面场景）。
    /// 令牌缓存：%APPDATA%\ClassTell\msal_token_cache.bin，用于免密静默刷新。
    /// </summary>
    internal sealed class AuthService
    {
        private readonly object _gate = new object();
        private IPublicClientApplication _pca;
        private string _pcaKey;

        private static readonly string[] ReservedScopes = { "openid", "profile", "offline_access" };

        public bool IsConfigured { get { return !string.IsNullOrWhiteSpace(Settings.ClientId); } }

        public static string AuthorityUrl(string tenant)
        {
            string t = string.IsNullOrWhiteSpace(tenant) ? "common" : tenant.Trim();
            if (t.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return t;
            return "https://login.microsoftonline.com/" + t;
        }

        public static string[] ParseScopes(string raw)
        {
            var list = new List<string>();
            if (!string.IsNullOrEmpty(raw))
            {
                foreach (string part in raw.Split(new[] { ' ', ',', ';', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string s = part.Trim();
                    if (s.Length == 0) continue;
                    if (ReservedScopes.Any(r => string.Equals(r, s, StringComparison.OrdinalIgnoreCase))) continue;
                    if (!list.Any(x => string.Equals(x, s, StringComparison.OrdinalIgnoreCase))) list.Add(s);
                }
            }
            if (list.Count == 0) list.Add("https://outlook.office.com/IMAP.AccessAsUser.All");
            return list.ToArray();
        }

        private IPublicClientApplication GetApp()
        {
            string key = Settings.ClientId + "|" + Settings.Tenant + "|" + string.Join(" ", ParseScopes(Settings.Scopes));
            lock (_gate)
            {
                if (_pca != null && _pcaKey == key) return _pca;

                IPublicClientApplication app = PublicClientApplicationBuilder
                    .Create(Settings.ClientId.Trim())
                    .WithAuthority(AuthorityUrl(Settings.Tenant))
                    .WithRedirectUri("http://localhost")
                    .WithClientName("ClassTell")
                    .WithClientVersion(Theme.VersionText)
                    .Build();

                RegisterTokenCache(app);
                _pca = app;
                _pcaKey = key;
                AppLog.Info("初始化 MSAL authority=" + AuthorityUrl(Settings.Tenant) + " scopes=" + Settings.Scopes);
                return _pca;
            }
        }

        private static void RegisterTokenCache(IPublicClientApplication app)
        {
            app.UserTokenCache.SetBeforeAccess(args =>
            {
                try
                {
                    string file = AppPaths.TokenCacheFile;
                    if (File.Exists(file)) args.TokenCache.DeserializeMsalV3(File.ReadAllBytes(file));
                }
                catch (Exception ex)
                {
                    AppLog.Exception_("读取令牌缓存失败（忽略缓存继续）", ex);
                }
            });
            app.UserTokenCache.SetAfterAccess(args =>
            {
                if (!args.HasStateChanged) return;
                try
                {
                    AppPaths.EnsureDataDir();
                    File.WriteAllBytes(AppPaths.TokenCacheFile, args.TokenCache.SerializeMsalV3());
                }
                catch (Exception ex)
                {
                    AppLog.Exception_("写入令牌缓存失败", ex);
                }
            });
        }

        // ---------- 登录 ----------
        /// <summary>系统浏览器交互登录（兼容个人 Outlook 与工作/学校账号）。</summary>
        public async Task<string> LoginInteractiveAsync(Action<string> progress, CancellationToken ct)
        {
            if (!IsConfigured) throw new InvalidOperationException("未配置 ClientId");
            IPublicClientApplication app = GetApp();
            if (progress != null) progress("正在打开系统浏览器完成登录…");

            AuthenticationResult result = await app.AcquireTokenInteractive(ParseScopes(Settings.Scopes))
                .WithPrompt(Prompt.SelectAccount)
                .WithUseEmbeddedWebView(false)
                .ExecuteAsync(ct);

            RememberAccount(result);
            return result.AccessToken;
        }

        /// <summary>设备代码登录：适合无法打开浏览器或远程桌面环境。</summary>
        public async Task<string> LoginDeviceCodeAsync(Action<DeviceCodePrompt> onPrompt, CancellationToken ct)
        {
            if (!IsConfigured) throw new InvalidOperationException("未配置 ClientId");
            IPublicClientApplication app = GetApp();

            AuthenticationResult result = await app.AcquireTokenWithDeviceCode(ParseScopes(Settings.Scopes), code =>
            {
                if (onPrompt != null)
                {
                    onPrompt(new DeviceCodePrompt
                    {
                        UserCode = code.UserCode,
                        VerificationUrl = code.VerificationUrl,
                        Message = code.Message
                    });
                }
                return Task.FromResult(0);
            }).ExecuteAsync(ct);

            RememberAccount(result);
            return result.AccessToken;
        }

        private static void RememberAccount(AuthenticationResult result)
        {
            if (result == null) return;
            string user = result.Account != null ? result.Account.Username : null;
            if (!string.IsNullOrEmpty(user)) Settings.Account = user;
            AppLog.Info("登录成功：" + (user ?? "(未知账号)") + "，令牌到期 " +
                        result.ExpiresOn.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") + "（本地时间）");
        }

        // ---------- 静默刷新 ----------
        /// <summary>取访问令牌（自动用缓存静默刷新）；失败抛出 AuthRequiredException。</summary>
        public async Task<string> GetAccessTokenSilentAsync(CancellationToken ct)
        {
            if (!IsConfigured) throw new AuthRequiredException("尚未配置 OAuth2 客户端 ID");
            IPublicClientApplication app = GetApp();
            IAccount account = null;
            try
            {
                IEnumerable<IAccount> accounts = await app.GetAccountsAsync();
                string saved = Settings.Account;
                if (!string.IsNullOrEmpty(saved))
                    account = accounts.FirstOrDefault(a => string.Equals(a.Username, saved, StringComparison.OrdinalIgnoreCase));
                if (account == null) account = accounts.FirstOrDefault();
            }
            catch (Exception ex)
            {
                AppLog.Exception_("枚举缓存账号失败", ex);
            }

            if (account == null) throw new AuthRequiredException("尚未登录邮箱，请先登录");

            try
            {
                AuthenticationResult result = await app.AcquireTokenSilent(ParseScopes(Settings.Scopes), account).ExecuteAsync(ct);
                return result.AccessToken;
            }
            catch (MsalUiRequiredException ex)
            {
                throw new AuthRequiredException("登录状态已过期，请重新登录（" + ex.ErrorCode + "）", ex);
            }
        }

        public async Task<string> GetCachedAccountNameAsync()
        {
            try
            {
                IAccount account = (await GetApp().GetAccountsAsync()).FirstOrDefault();
                return account == null ? null : account.Username;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public async Task<bool> HasCachedAccountAsync()
        {
            return !string.IsNullOrEmpty(await GetCachedAccountNameAsync());
        }

        public async Task LogoutAsync()
        {
            try
            {
                IPublicClientApplication app = GetApp();
                foreach (IAccount acc in await app.GetAccountsAsync()) await app.RemoveAsync(acc);
            }
            catch (Exception ex)
            {
                AppLog.Exception_("退出登录失败", ex);
            }
            Settings.Account = string.Empty;
            try
            {
                if (File.Exists(AppPaths.TokenCacheFile)) File.Delete(AppPaths.TokenCacheFile);
            }
            catch (Exception ex)
            {
                AppLog.Exception_("删除令牌缓存失败", ex);
            }
        }
    }
}

