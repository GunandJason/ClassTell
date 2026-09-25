using System;
using System.IO;
using Microsoft.Win32;

namespace ClassTell
{
    /// <summary>
    /// 开机自启动（仅当前用户，无需管理员）：
    /// 在 HKCU\Software\Microsoft\Windows\CurrentVersion\Run 下写一个名为 ClassTell 的值，
    /// 命令行带 --tray，启动后直接驻留托盘继续收信。
    /// </summary>
    internal static class Startup
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

        /// <summary>注册表值名（卸载/关闭自启动时按此名删除）。</summary>
        internal const string ValueName = "ClassTell";

        /// <summary>自启动命令行：带引号的 exe 路径 + --tray。</summary>
        public static string CommandLine()
        {
            string exe = Path.Combine(AppPaths.ExeDir, "ClassTell.exe");
            return "\"" + exe + "\" " + AppOptions.TrayArgument;
        }

        /// <summary>当前是否已登记自启动。</summary>
        public static bool IsEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                {
                    if (key == null) return false;
                    string value = key.GetValue(ValueName) as string;
                    return !string.IsNullOrEmpty(value);
                }
            }
            catch (Exception ex)
            {
                AppLog.Exception_("读取开机自启动状态失败", ex);
                return false;
            }
        }

        /// <summary>开关自启动；返回 false 表示写入失败（权限/策略限制），界面应提示用户。</summary>
        public static bool Apply(bool enabled)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
                {
                    if (key == null) return false;
                    if (enabled)
                    {
                        key.SetValue(ValueName, CommandLine(), RegistryValueKind.String);
                        AppLog.Info("已开启开机自启动：" + CommandLine());
                    }
                    else
                    {
                        if (key.GetValue(ValueName) != null) key.DeleteValue(ValueName, false);
                        AppLog.Info("已关闭开机自启动");
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                AppLog.Exception_("设置开机自启动失败", ex);
                return false;
            }
        }

        /// <summary>启动时校正：让注册表状态与设置项一致。</summary>
        public static void SyncWithSetting()
        {
            try
            {
                if (Settings.RunAtStartup != IsEnabled()) Apply(Settings.RunAtStartup);
            }
            catch (Exception ex)
            {
                AppLog.Exception_("校正开机自启动失败", ex);
            }
        }

        /// <summary>设置项变化时应用（供界面调用）。</summary>
        public static bool ApplyFromSetting()
        {
            return Apply(Settings.RunAtStartup);
        }
    }
}
