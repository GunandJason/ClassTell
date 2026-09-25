using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ClassTell
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            AppOptions.Parse(args);
            bool createdNew;
            using (var mutex = new Mutex(true, "ClassTell.SingleInstance.Mutex", out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show("ClassTell 已经在运行中。", "ClassTell", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                AppPaths.EnsureDataDir();
                Startup.SyncWithSetting();
                HookGlobalExceptions();

                try
                {
                    Application.Run(new ShellForm());
                }
                catch (Exception ex)
                {
                    AppLog.Exception_("主窗口异常退出", ex);
                    MessageBox.Show("ClassTell 发生异常：" + ex.Message + Environment.NewLine + "详情见日志：" + AppLog.FilePath,
                        "ClassTell", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }

                GC.KeepAlive(mutex);
            }
        }

        private static void HookGlobalExceptions()
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) =>
            {
                AppLog.Exception_("UI 线程未处理异常", e.Exception);
                MessageBox.Show("发生未处理的错误：" + e.Exception.Message, "ClassTell", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            };

            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                AppLog.Exception_("应用域未处理异常", e.ExceptionObject as Exception);
            };

            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                AppLog.Exception_("未观察的任务异常", e.Exception);
                e.SetObserved();
            };
        }
    }
}
