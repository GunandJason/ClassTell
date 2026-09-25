using System;
using System.IO;
using System.Text;

namespace ClassTell
{
    /// <summary>简单文件日志：%APPDATA%\ClassTell\log.txt（便于排查登录 / 收信问题）。</summary>
    internal static class AppLog
    {
        private static readonly object Gate = new object();
        private const long MaxBytes = 512 * 1024;

        public static string Dir { get { return AppPaths.DataDir; } }
        public static string FilePath { get { return Path.Combine(AppPaths.DataDir, "log.txt"); } }

        public static void Info(string message) { Write("INFO ", message); }
        public static void Warn(string message) { Write("WARN ", message); }
        public static void Error(string message) { Write("ERROR", message); }

        public static void Exception_(string context, Exception ex)
        {
            if (ex == null) { Warn(context); return; }
            Error(context + " -> " + ex.GetType().Name + ": " + ex.Message + Environment.NewLine + ex.StackTrace);
            if (ex.InnerException != null) Error("  inner: " + ex.InnerException.Message);
        }

        private static void Write(string level, string message)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(AppPaths.DataDir);
                    string path = FilePath;
                    if (File.Exists(path) && new FileInfo(path).Length > MaxBytes)
                    {
                        string tail = ReadTail(path, 128 * 1024);
                        File.WriteAllText(path, "[日志已截断]" + Environment.NewLine + tail, Encoding.UTF8);
                    }
                    string line = string.Format("[{0:yyyy-MM-dd HH:mm:ss}] {1} {2}{3}",
                        DateTime.Now, level, message, Environment.NewLine);
                    File.AppendAllText(path, line, Encoding.UTF8);
                }
            }
            catch (Exception)
            {
                // 日志失败不影响主流程
            }
        }

        private static string ReadTail(string path, int bytes)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                long start = Math.Max(0, fs.Length - bytes);
                fs.Seek(start, SeekOrigin.Begin);
                var buffer = new byte[fs.Length - start];
                int read = fs.Read(buffer, 0, buffer.Length);
                return Encoding.UTF8.GetString(buffer, 0, read);
            }
        }
    }

    /// <summary>应用的固定路径。</summary>
    internal static class AppPaths
    {
        private static string _dataDir;

        public static string DataDir
        {
            get
            {
                if (_dataDir == null)
                {
                    string root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                    _dataDir = Path.Combine(root, "ClassTell");
                }
                return _dataDir;
            }
        }

        public static string SettingsFile { get { return Path.Combine(DataDir, "settings.ini"); } }
        public static string TokenCacheFile { get { return Path.Combine(DataDir, "msal_token_cache.bin"); } }

        /// <summary>程序所在目录（info.txt 与之同级）。</summary>
        public static string ExeDir
        {
            get { return AppDomain.CurrentDomain.BaseDirectory; }
        }

        public static string InfoFile { get { return Path.Combine(ExeDir, "info.txt"); } }

        public static void EnsureDataDir()
        {
            try { System.IO.Directory.CreateDirectory(DataDir); }
            catch (Exception) { }
        }
    }
}
