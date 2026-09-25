namespace ClassTell
{
    /// <summary>启动参数（诊断 / 测试用）。</summary>
    internal static class AppOptions
    {
        /// <summary>命令行包含 --no-login 时不自动弹出登录窗口（便于离屏测试或离线运行）。</summary>
        public static bool NoAutoLogin { get; private set; }

        /// <summary>命令行包含 --demo 时注入示例消息，便于在没有邮箱的情况下预览界面。</summary>
        public static bool Demo { get; private set; }

        public static void Parse(string[] args)
        {
            if (args == null) return;
            foreach (string raw in args)
            {
                string a = (raw ?? string.Empty).Trim().TrimStart('-', '/').ToLowerInvariant();
                if (a == "no-login" || a == "nologin") NoAutoLogin = true;
                if (a == "demo") Demo = true;
            }
        }
    }
}
