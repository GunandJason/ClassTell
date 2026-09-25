using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace ClassTell
{
    /// <summary>窗口相关的少量 Win32 调用（圆角、图标句柄释放）。</summary>
    internal static class Native
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWCP_ROUND = 2;

        public static void DestroyIconHandle(IntPtr hIcon)
        {
            if (hIcon == IntPtr.Zero) return;
            try { DestroyIcon(hIcon); }
            catch (Exception) { }
        }

        /// <summary>Windows 11 下让无边框窗口拥有系统圆角（旧系统静默忽略）。</summary>
        public static void TryEnableRoundedCorners(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return;
            try
            {
                int value = DWMWCP_ROUND;
                DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref value, sizeof(int));
            }
            catch (Exception) { }
        }

        /// <summary>告知 DWM 使用深色系统标题栏（仍然使用系统窗口组件，不改自绘）。</summary>
        public static void SetSystemTitleBarDark(IntPtr hwnd, bool dark)
        {
            if (hwnd == IntPtr.Zero) return;
            try
            {
                int value = dark ? 1 : 0;
                DwmSetWindowAttribute(hwnd, 20, ref value, sizeof(int)); // DWMWA_USE_IMMERSIVE_DARK_MODE
                DwmSetWindowAttribute(hwnd, 19, ref value, sizeof(int)); // 旧版本兼容
            }
            catch (Exception) { }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FLASHWINFO
        {
            public uint cbSize;
            public IntPtr hwnd;
            public uint dwFlags;
            public uint uCount;
            public uint dwTimeout;
        }

        [DllImport("user32.dll")]
        private static extern bool FlashWindowEx(ref FLASHWINFO pwfi);

        private const uint FLASHW_STOP = 0;
        private const uint FLASHW_CAPTION = 1;
        private const uint FLASHW_TRAY = 2;
        private const uint FLASHW_ALL = 3;
        private const uint FLASHW_TIMERNOFG = 12;

        /// <summary>
        /// 闪烁窗口与任务栏按钮直到窗口被激活（呼叫通知的“看得见”兜底：
        /// 系统通知气泡可能被 Windows 的通知/专注助手设置抑制）。
        /// </summary>
        public static void FlashWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return;
            try
            {
                var info = new FLASHWINFO();
                info.cbSize = (uint)Marshal.SizeOf(typeof(FLASHWINFO));
                info.hwnd = hwnd;
                info.dwFlags = FLASHW_ALL | FLASHW_TIMERNOFG;
                info.uCount = 0;
                info.dwTimeout = 0;
                FlashWindowEx(ref info);
            }
            catch (Exception) { }
        }

        /// <summary>停止闪烁并恢复标题栏外观。</summary>
        public static void StopFlash(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return;
            try
            {
                var info = new FLASHWINFO();
                info.cbSize = (uint)Marshal.SizeOf(typeof(FLASHWINFO));
                info.hwnd = hwnd;
                info.dwFlags = FLASHW_STOP;
                FlashWindowEx(ref info);
            }
            catch (Exception) { }
        }

        private const int WM_NCHITTEST = 0x0084;
        private const int WM_LBUTTONDBLCLK = 0x0203;

        public const int HTCLIENT = 1;
        public const int HTCAPTION = 2;
        public const int HTLEFT = 10;
        public const int HTRIGHT = 11;
        public const int HTTOP = 12;
        public const int HTTOPLEFT = 13;
        public const int HTTOPRIGHT = 14;
        public const int HTBOTTOM = 15;
        public const int HTBOTTOMLEFT = 16;
        public const int HTBOTTOMRIGHT = 17;

        public const int WmNcHitTest = WM_NCHITTEST;
        public const int WmLButtonDblClk = WM_LBUTTONDBLCLK;
    }
}
