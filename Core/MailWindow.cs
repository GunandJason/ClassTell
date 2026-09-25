using System;

namespace ClassTell
{
    /// <summary>
    /// 收信时间窗口（“过期 / 当前”的判定依据）：
    ///   · 打开软件**之前**收到、且在 StaleWindowHours（默认 24 小时）内的未读邮件
    ///     → 显示在「过期」分项里，**只显示、不提醒**（避免开机后被旧邮件刷屏）；
    ///   · 打开软件**之后**收到的邮件 → 显示在「消息」里，按命令字（C / T）正常提醒。
    /// 启动时间以进程启动为准（静态初始化），窗口起点用于 IMAP 的 INTERNALDATE 过滤
    /// 与 Graph 的 receivedDateTime 过滤。
    /// </summary>
    internal static class MailWindow
    {
        private static DateTime _appStartedLocal = DateTime.Now;

        /// <summary>本次启动时间（本地时间）。</summary>
        public static DateTime AppStartedLocal { get { return _appStartedLocal; } }

        /// <summary>回填窗口起点（本地时间）；StaleWindowHours = 0 时等于启动时间（即不回填）。</summary>
        public static DateTime SinceLocal
        {
            get
            {
                int hours = Settings.StaleWindowHours;
                return hours <= 0 ? _appStartedLocal : _appStartedLocal.AddHours(-hours);
            }
        }

        /// <summary>回填窗口起点（UTC，用于 Graph 的 receivedDateTime 过滤）。</summary>
        public static DateTime SinceUtc { get { return SinceLocal.ToUniversalTime(); } }

        /// <summary>是否为“打开软件之前收到的邮件”（→ 归入「过期」，不提醒）。</summary>
        public static bool IsStale(DateTime receivedLocal)
        {
            return receivedLocal < _appStartedLocal;
        }

        /// <summary>是否落在需要回填的窗口内（启动前 StaleWindowHours 小时内）。</summary>
        public static bool InBackfillWindow(DateTime receivedLocal)
        {
            return Settings.StaleWindowHours > 0 && receivedLocal >= SinceLocal && receivedLocal < _appStartedLocal;
        }

        /// <summary>「过期」分项的说明文字。</summary>
        public static string StaleSubtitle()
        {
            int hours = Settings.StaleWindowHours;
            return hours <= 0
                ? "已关闭过期邮件回填（settings.ini 中 StaleWindowHours=0）"
                : "打开软件前 " + hours + " 小时内未读的指令邮件（只显示，不提醒）";
        }

        /// <summary>「过期」为空时的提示。</summary>
        public static string StaleEmptyHint()
        {
            int hours = Settings.StaleWindowHours;
            return hours <= 0 ? "未开启过期邮件回填" : "打开软件前 " + hours + " 小时内没有未读的指令邮件";
        }

        /// <summary>供自测注入启动时间。</summary>
        internal static void SetAppStarted(DateTime localTime)
        {
            _appStartedLocal = localTime;
        }
    }
}
