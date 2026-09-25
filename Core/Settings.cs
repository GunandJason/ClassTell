using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace ClassTell
{
    /// <summary>
    /// 应用设置，保存在 %APPDATA%\ClassTell\settings.ini（key=value 文本，便于手工修改）。
    /// </summary>
    internal static class Settings
    {
        private const string DefaultClientId = "9e5f94bc-e8a4-4e73-b8be-63364c29d753";
        private const string DefaultScopes = "https://outlook.office.com/IMAP.AccessAsUser.All";

        private static readonly Dictionary<string, string> Map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static bool _loaded;

        /// <summary>已废弃的旧配置项：读取时丢弃，并在下次保存时从文件里清除。</summary>
        private static readonly string[] RemovedKeys = { "ToastInApp" };

        public static event Action Changed;

        // ---------- 邮箱 / OAuth2 ----------
        /// <summary>公共客户端 ID（可在“关于 → 邮箱登录 → 高级设置”改成自己注册的应用）。</summary>
        public static string ClientId
        {
            get { return Get("ClientId", DefaultClientId); }
            set { Set("ClientId", value); }
        }

        public static string Tenant
        {
            get { return Get("Tenant", "common"); }
            set { Set("Tenant", value); }
        }

        public static string Scopes
        {
            get { return Get("Scopes", DefaultScopes); }
            set { Set("Scopes", value); }
        }

        public static string ImapHost
        {
            get { return Get("ImapHost", "outlook.office365.com"); }
            set { Set("ImapHost", value); }
        }

        public static int ImapPort
        {
            get { return GetInt("ImapPort", 993); }
            set { Set("ImapPort", value.ToString(CultureInfo.InvariantCulture)); }
        }

        /// <summary>登录成功后记住的邮箱地址（用于展示与静默刷新）。</summary>
        public static string Account
        {
            get { return Get("Account", string.Empty); }
            set { Set("Account", value); }
        }

        /// <summary>轮询间隔（秒）。使用 IMAP IDLE 时只是兜底心跳。</summary>
        public static int PollSeconds
        {
            get { return Math.Max(5, Math.Min(600, GetInt("PollSeconds", 20))); }
            set { Set("PollSeconds", value.ToString(CultureInfo.InvariantCulture)); }
        }

        /// <summary>启动时最多回溯处理的最近未读邮件数量。</summary>
        public static int RecentScanCount
        {
            get { return Math.Max(0, Math.Min(200, GetInt("RecentScanCount", 10))); }
            set { Set("RecentScanCount", value.ToString(CultureInfo.InvariantCulture)); }
        }

        /// <summary>处理完成后是否把邮件标记为已读（默认 false，不改变邮箱状态）。</summary>
        public static bool MarkAsSeen
        {
            get { return GetBool("MarkAsSeen", false); }
            set { Set("MarkAsSeen", value ? "true" : "false"); }
        }

        // ---------- 通知 / 界面 ----------
        public static bool NotifyOnCall
        {
            get { return GetBool("NotifyOnCall", true); }
            set { Set("NotifyOnCall", value ? "true" : "false"); }
        }

        public static float FontSize
        {
            get
            {
                float v;
                if (!float.TryParse(Get("FontSize", "10"), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) v = Theme.DefaultFontSize;
                return Math.Max(Theme.MinFontSize, Math.Min(Theme.MaxFontSize, v));
            }
            set { Set("FontSize", value.ToString("0.##", CultureInfo.InvariantCulture)); }
        }

        /// <summary>当前主题模式（深色 / 浅色），可在“关于 → 界面设置”里切换。</summary>
        public static bool DarkMode
        {
            get { return GetBool("DarkMode", true); }
            set { Set("DarkMode", value ? "true" : "false"); }
        }

        /// <summary>强调色配色方案（water / teal / lilac / apricot）。</summary>
        public static string AccentKey
        {
            get { return Get("AccentKey", Theme.DefaultPaletteKey); }
            set { Set("AccentKey", value); }
        }

        // ---------- 读取工具 ----------
        public static string Get(string key, string fallback)
        {
            EnsureLoaded();
            string v;
            return Map.TryGetValue(key, out v) ? v : fallback;
        }

        /// <summary>判断某个键是否已经存在（用于“首次运行”判断）。</summary>
        public static bool HasSetting(string key)
        {
            EnsureLoaded();
            return Map.ContainsKey(key);
        }

        private static int GetInt(string key, int fallback)
        {
            string raw = Get(key, null);
            int v;
            if (raw != null && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) return v;
            return fallback;
        }

        private static bool GetBool(string key, bool fallback)
        {
            string raw = Get(key, null);
            if (raw == null) return fallback;
            bool b;
            if (bool.TryParse(raw, out b)) return b;
            return fallback;
        }

        private static void Set(string key, string value)
        {
            EnsureLoaded();
            if (value == null) value = string.Empty;
            string old;
            if (Map.TryGetValue(key, out old) && string.Equals(old, value, StringComparison.Ordinal)) return;
            Map[key] = value;
            Save();
            RaiseChanged();
        }

        public static void RaiseChanged()
        {
            Action h = Changed;
            if (h != null) h();
        }

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                if (!File.Exists(AppPaths.SettingsFile)) return;
                foreach (string rawLine in File.ReadAllLines(AppPaths.SettingsFile, Encoding.UTF8))
                {
                    string line = rawLine.Trim();
                    if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;
                    int idx = line.IndexOf('=');
                    if (idx <= 0) continue;
                    Map[line.Substring(0, idx).Trim()] = line.Substring(idx + 1).Trim();
                }
                // 丢弃旧版本遗留的配置项（例如已取消的“应用内浮层提示”ToastInApp）
                foreach (string legacy in RemovedKeys) Map.Remove(legacy);
            }
            catch (Exception ex)
            {
                AppLog.Exception_("读取设置失败", ex);
            }
        }

        public static void Save()
        {
            try
            {
                AppPaths.EnsureDataDir();
                var sb = new StringBuilder();
                sb.AppendLine("# ClassTell 配置文件（可手工编辑，程序退出时也会自动写回）");
                sb.AppendLine("# ClientId 可替换为自己在 Entra ID 注册的公共客户端应用。");
                sb.AppendLine("# IMAP: outlook.office365.com  端口: 993  加密: SSL/TLS  认证: OAuth2 (Modern Auth)");
                sb.AppendLine();
                foreach (KeyValuePair<string, string> kv in Map)
                    sb.AppendLine(kv.Key + "=" + kv.Value);
                File.WriteAllText(AppPaths.SettingsFile, sb.ToString(), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                AppLog.Exception_("保存设置失败", ex);
            }
        }

        /// <summary>把默认值写入磁盘（首次运行时调用）。</summary>
        public static void EnsureDefaults()
        {
            EnsureLoaded();
            string unused;
            if (!Map.TryGetValue("ClientId", out unused)) Map["ClientId"] = DefaultClientId;
            if (!Map.TryGetValue("Tenant", out unused)) Map["Tenant"] = "common";
            if (!Map.TryGetValue("Scopes", out unused)) Map["Scopes"] = DefaultScopes;
            if (!Map.TryGetValue("ImapHost", out unused)) Map["ImapHost"] = "outlook.office365.com";
            if (!Map.TryGetValue("ImapPort", out unused)) Map["ImapPort"] = "993";
            if (!Map.TryGetValue("PollSeconds", out unused)) Map["PollSeconds"] = "20";
            if (!Map.TryGetValue("RecentScanCount", out unused)) Map["RecentScanCount"] = "10";
            if (!Map.TryGetValue("MarkAsSeen", out unused)) Map["MarkAsSeen"] = "false";
            if (!Map.TryGetValue("NotifyOnCall", out unused)) Map["NotifyOnCall"] = "true";
            if (Map.TryGetValue("FontSize", out unused)) { }
            else Map["FontSize"] = Theme.DefaultFontSize.ToString("0.##", CultureInfo.InvariantCulture);
            if (!Map.TryGetValue("DarkMode", out unused)) Map["DarkMode"] = "true";
            if (!Map.TryGetValue("AccentKey", out unused)) Map["AccentKey"] = Theme.DefaultPaletteKey;
            Save();
        }
    }
}

