using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using MimeKit;

namespace ClassTell.SelfTest
{
    /// <summary>
    /// ClassTell 逻辑自测：不依赖网络与 UI 线程，验证指令解析、正文拆分、截断、
    /// HTML 转换、设置读写、主题字号与缓动函数等核心逻辑。
    /// 运行： dotnet build ClassTell.sln &amp;&amp; tests\ClassTell.SelfTest\bin\Debug\net48\ClassTell.SelfTest.exe
    /// </summary>
    internal static class TestRunner
    {
        private static int _passed;
        private static readonly List<string> Failures = new List<string>();

        [STAThread]
        private static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            // 邮箱连接诊断：ClassTell.SelfTest.exe --mail-check [租户]
            if (args != null)
            {
                string tenant = null;
                bool mailCheck = false;
                for (int i = 0; i < args.Length; i++)
                {
                    if (string.Equals(args[i], "--mail-check", StringComparison.OrdinalIgnoreCase))
                    {
                        mailCheck = true;
                        if (i + 1 < args.Length && !args[i + 1].StartsWith("-")) tenant = args[i + 1];
                    }
                }
                if (mailCheck) return MailDiag.Run(tenant);
            }

            Console.WriteLine("=== ClassTell 自测开始 ===");

            TestCommandParsing();
            TestTitleBodySplit();
            TestTruncation();
            TestHtmlToText();
            TestFullMessageParsing();
            TestSettingsRoundTrip();
            TestThemeAndEasing();
            TestAuthHelpers();
            TestMailFailureHints();
            TestInfoFile();
            UiTests.Run(Check);

            Console.WriteLine();
            if (Failures.Count == 0)
            {
                Console.WriteLine("=== 全部通过：" + _passed + " 项 ===");
                return 0;
            }

            Console.WriteLine("=== 失败 " + Failures.Count + " 项 / 通过 " + _passed + " 项 ===");
            foreach (string f in Failures) Console.WriteLine("  ✗ " + f);
            return 1;
        }

        private static void Check(bool condition, string name)
        {
            if (condition)
            {
                _passed++;
                Console.WriteLine("  ✓ " + name);
            }
            else
            {
                Failures.Add(name);
                Console.WriteLine("  ✗ " + name);
            }
        }

        private static void CheckEqual(string expected, string actual, string name)
        {
            Check(string.Equals(expected, actual, StringComparison.Ordinal), name + "（期望 [" + Show(expected) + "]，实际 [" + Show(actual) + "]）");
        }

        private static string Show(string s)
        {
            if (s == null) return "<null>";
            return s.Replace("\r", "\\r").Replace("\n", "\\n");
        }

        private static MimeMessage BuildMail(string subject, string textBody, string htmlBody = null, string sender = "teacher@example.com", string senderName = "王老师")
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(senderName, sender));
            message.To.Add(new MailboxAddress("我", "student@example.com"));
            message.Subject = subject;
            message.Date = new DateTimeOffset(2026, 9, 24, 10, 30, 0, TimeSpan.FromHours(8));

            var body = new BodyBuilder();
            if (textBody != null) body.TextBody = textBody;
            if (htmlBody != null) body.HtmlBody = htmlBody;
            message.Body = body.ToMessageBody();
            return message;
        }

        // ---------- 用例 ----------
        private static void TestCommandParsing()
        {
            Console.WriteLine("[1] 标题命令字");
            CheckEqual("Call", CommandParser.ParseCommand("C").ToString(), "C → Call");
            CheckEqual("Call", CommandParser.ParseCommand("c").ToString(), "c → Call");
            CheckEqual("Call", CommandParser.ParseCommand("CALL").ToString(), "CALL → Call");
            CheckEqual("Call", CommandParser.ParseCommand("  call  ").ToString(), "带空格 call → Call");
            CheckEqual("Tell", CommandParser.ParseCommand("T").ToString(), "T → Tell");
            CheckEqual("Tell", CommandParser.ParseCommand("tell").ToString(), "tell → Tell");
            CheckEqual("Unknown", CommandParser.ParseCommand("X").ToString(), "X → Unknown");
            CheckEqual("Unknown", CommandParser.ParseCommand("call me").ToString(), "call me → Unknown");
            CheckEqual("Unknown", CommandParser.ParseCommand("").ToString(), "空标题 → Unknown");
            CheckEqual("Unknown", CommandParser.ParseCommand(null).ToString(), "null 标题 → Unknown");
        }

        private static void TestTitleBodySplit()
        {
            Console.WriteLine("[2] 正文拆分（第一行标题 + 其余正文）");
            string title, body;
            CommandParser.SplitTitleBody("会议通知\n今天下午三点开会\n地点 A101", out title, out body);
            CheckEqual("会议通知", title, "标题取第一行");
            CheckEqual("今天下午三点开会\n地点 A101", body, "正文保留换行");

            CommandParser.SplitTitleBody("标题：值日表\r\n第 1 组\r\n第 2 组", out title, out body);
            CheckEqual("值日表", title, "去掉“标题：”前缀");
            CheckEqual("第 1 组\n第 2 组", body, "CRLF 统一为 LF");

            CommandParser.SplitTitleBody("\n\n  紧急通知  \n\n内容第一行\n内容第二行\n", out title, out body);
            CheckEqual("紧急通知", title, "跳过前置空行并去首尾空白");
            CheckEqual("内容第一行\n内容第二行", body, "裁掉尾部空行");

            CommandParser.SplitTitleBody("只有标题", out title, out body);
            CheckEqual("只有标题", title, "只有一行时取为标题");
            CheckEqual(string.Empty, body, "只有一行时正文为空");

            CommandParser.SplitTitleBody("", out title, out body);
            CheckEqual(string.Empty, title, "空正文标题为空");
            CheckEqual(string.Empty, body, "空正文正文为空");

            CommandParser.SplitTitleBody("第一行\n\n段落一\n\n段落二", out title, out body);
            CheckEqual("第一行", title, "带空行时标题正确");
            CheckEqual("段落一\n\n段落二", body, "正文中的空行保留");
        }

        private static void TestTruncation()
        {
            Console.WriteLine("[3] 通知文本截断");
            CheckEqual("短文本", Typography.Truncate("短文本", 20), "未超长不截断");
            string longText = new string('长', 30);
            string cut = Typography.Truncate(longText, 10);
            CheckEqual("11", cut.Length.ToString(), "超长截断为 10 字 + 省略号");
            Check(cut.EndsWith("…", StringComparison.Ordinal), "以省略号结尾");
            string multi = Typography.Truncate("第一行\n第二行\n第三行", 5);
            Check(multi.Contains("\n"), "截断时保留换行");
        }

        private static void TestHtmlToText()
        {
            Console.WriteLine("[4] HTML 正文转换");
            string text = HtmlText.ToPlainText("<html><body><p>数学作业：</p><p>第 10 页 1-5 题<br/>明天交</p><b>注意</b>&amp;签名</body></html>");
            Check(text.Contains("数学作业："), "保留中文段落");
            Check(text.Contains("第 10 页 1-5 题"), "br 前内容保留");
            Check(text.Contains("\n"), "块级标签转换为换行");
            Check(text.Contains("注意&签名"), "实体 & 正确解码");
            Check(!text.Contains("<"), "标签已清除");
        }

        private static void TestFullMessageParsing()
        {
            Console.WriteLine("[5] 完整邮件解析");
            string reason;
            MessageItem item = CommandParser.Parse(BuildMail("C", "家长会通知\n本周五 18:00 在教室召开家长会\n请准时参加"), 101, out reason);
            Check(item != null, "C 邮件被识别");
            if (item != null)
            {
                CheckEqual("Call", item.Command.ToString(), "命令为 Call（触发系统通知）");
                CheckEqual("家长会通知", item.Title, "标题 = 正文第一行");
                CheckEqual("本周五 18:00 在教室召开家长会\n请准时参加", item.Body, "正文 = 第一行之后的内容");
                CheckEqual("teacher@example.com", item.Source, "来源 = 发件人地址");
                CheckEqual("王老师 <teacher@example.com>", item.SourceText, "来源文本含姓名");
                Check(item.IsCall, "IsCall 为真");
            }

            item = CommandParser.Parse(BuildMail("tell", "作业提醒\n数学卷子 3 张"), 102, out reason);
            Check(item != null && item.Command == MailCommand.Tell, "tell 邮件被识别为 Tell");
            Check(item != null && !item.IsCall, "Tell 不触发通知");
            CheckEqual("作业提醒", item == null ? null : item.Title, "tell 标题正确");

            item = CommandParser.Parse(BuildMail("只发通知", "正文"), 103, out reason);
            Check(item == null, "普通邮件被忽略");
            Check(!string.IsNullOrEmpty(reason), "忽略原因有记录");

            item = CommandParser.Parse(BuildMail("T", null, "<div>值日安排</div><div>周一：张三<br>周二：李四</div>"), 104, out reason);
            Check(item != null, "纯 HTML 邮件也能解析");
            if (item != null)
            {
                CheckEqual("值日安排", item.Title, "HTML 第一行作标题");
                CheckEqual("周一：张三\n周二：李四", item.Body, "HTML 转为多行正文");
            }

            item = CommandParser.Parse(BuildMail("  call  ", "\n\n  临时通知  \n今天提前放学"), 105, out reason);
            Check(item != null && item.Command == MailCommand.Call, "带空格的 CALL 也可识别");
            CheckEqual("临时通知", item == null ? null : item.Title, "带前置空行的标题正确");
            CheckEqual("今天提前放学", item == null ? null : item.Body, "带前置空行的正文正确");
        }

        private static void TestSettingsRoundTrip()
        {
            Console.WriteLine("[6] 设置读写");
            AppPaths.EnsureDataDir();
            Settings.EnsureDefaults();
            Check(Settings.ImapHost == "outlook.office365.com", "默认 IMAP 服务器为 outlook.office365.com");
            Check(Settings.ImapPort == 993, "默认端口 993");
            Check(!string.IsNullOrEmpty(Settings.ClientId), "默认 ClientId 非空");
            Check(Settings.Tenant == "common", "默认租户 common");

            float original = Settings.FontSize;
            Settings.FontSize = 13.5f;
            Check(Math.Abs(Settings.FontSize - 13.5f) < 0.001f, "字号写入后可读回");
            Check(File.Exists(AppPaths.SettingsFile), "settings.ini 已落盘");
            string content = File.ReadAllText(AppPaths.SettingsFile, Encoding.UTF8);
            Check(content.Contains("FontSize=13.5"), "配置文件包含 FontSize");
            Settings.FontSize = original;
            Check(Math.Abs(Settings.FontSize - original) < 0.001f, "字号已还原");

            // 已废弃的旧配置项（应用内浮层提示 ToastInApp）不会再被写回
            File.AppendAllText(AppPaths.SettingsFile, "ToastInApp=true" + Environment.NewLine, Encoding.UTF8);
            Check(File.ReadAllText(AppPaths.SettingsFile, Encoding.UTF8).Contains("ToastInApp"),
                "测试用旧配置项已写入文件");
            Settings.Save();
            Check(!File.ReadAllText(AppPaths.SettingsFile, Encoding.UTF8).Contains("ToastInApp"),
                "保存设置时不会再写回已废弃的 ToastInApp");
        }

        private static void TestThemeAndEasing()
        {
            Console.WriteLine("[7] 主题与动效");
            Theme.UpdateDpi(1f, new Size(1920, 1080));
            Theme.Apply(12f, true);
            Check(Math.Abs(Theme.FontSize - 12f) < 0.001f, "字号应用成功");
            Check(Theme.Dark, "当前为深色模式");
            Check(Math.Abs(Theme.Font(0f, System.Drawing.FontStyle.Regular).SizeInPoints - 12f) < 0.6f, "字体磅值与设定字号一致（未重复放大）");
            Check(Theme.Accent.ToArgb() == System.Drawing.ColorTranslator.FromHtml("#7AB5D6").ToArgb(), "强调色为 #7AB5D6");
            Check(Theme.S(10) == 10, "DPI=100% 时不缩放");
            Theme.UpdateDpi(1.5f, new Size(3840, 2160));
            Check(Theme.S(10) == 15, "DPI=150% 时按比例缩放");
            Theme.UpdateDpi(1f, new Size(1920, 1080));

            Check(Math.Abs(Bezier.Eval(Ease.Standard, 0d)) < 1e-6, "缓动起点为 0");
            Check(Math.Abs(Bezier.Eval(Ease.Standard, 1d) - 1d) < 1e-6, "缓动终点为 1");
            Check(Math.Abs(Bezier.Eval(Ease.Linear, 0.5d) - 0.5d) < 1e-6, "线性缓动中点固定为 0.5");

            // 与独立实现的 cubic-bezier 采样结果比对（校验求解器数值精度）
            double[] samples = { 0.1d, 0.25d, 0.5d, 0.75d, 0.9d };
            double maxError = 0d;
            foreach (double x in samples)
            {
                maxError = Math.Max(maxError, Math.Abs(Bezier.Eval(Ease.Standard, x) - ReferenceEase(0.4, 0.0, 0.2, 1.0, x)));
                maxError = Math.Max(maxError, Math.Abs(Bezier.Eval(Ease.Decelerate, x) - ReferenceEase(0.0, 0.0, 0.2, 1.0, x)));
            }
            Check(maxError < 0.01d, "贝塞尔求解与参考实现一致（最大误差 " + maxError.ToString("0.0000") + "）");
            double mid = Bezier.Eval(Ease.Standard, 0.5d);
            Check(mid > 0.70d && mid < 0.85d, "标准缓动前段加速（x=0.5 → " + mid.ToString("0.000") + "，理论值 ≈0.78）");
            Check(Bezier.Eval(Ease.Decelerate, 0.5d) > Bezier.Eval(Ease.Standard, 0.5d), "进场缓动比标准缓动更早到位");
            bool monotonic = true;
            double last = -1d;
            for (int i = 0; i <= 20; i++)
            {
                double v = Bezier.Eval(Ease.EmphasizedDecelerate, i / 20d);
                if (v < last - 1e-9) monotonic = false;
                last = v;
            }
            Check(monotonic, "缓动曲线单调递增");
        }

        /// <summary>独立实现（暴力采样）的 cubic-bezier 求值，仅用于交叉校验。</summary>
        private static double ReferenceEase(double x1, double y1, double x2, double y2, double x)
        {
            double bestT = 0d;
            double bestErr = double.MaxValue;
            const int steps = 20000;
            for (int i = 0; i <= steps; i++)
            {
                double t = i / (double)steps;
                double u = 1d - t;
                double xs = 3d * u * u * t * x1 + 3d * u * t * t * x2 + t * t * t;
                double err = Math.Abs(xs - x);
                if (err < bestErr) { bestErr = err; bestT = t; }
            }
            double u2 = 1d - bestT;
            return 3d * u2 * u2 * bestT * y1 + 3d * u2 * bestT * bestT * y2 + bestT * bestT * bestT;
        }

        private static void TestAuthHelpers()
        {
            Console.WriteLine("[8] OAuth2 配置辅助");
            string[] scopes = AuthService.ParseScopes("https://outlook.office.com/IMAP.AccessAsUser.All offline_access openid profile");
            Check(scopes.Length == 1, "保留 scope 被过滤");
            CheckEqual("https://outlook.office.com/IMAP.AccessAsUser.All", scopes[0], "IMAP scope 保留");
            string[] fallback = AuthService.ParseScopes("");
            Check(fallback.Length == 1 && fallback[0].Contains("IMAP.AccessAsUser.All"), "空配置时使用默认 IMAP scope");
            CheckEqual("https://login.microsoftonline.com/common", AuthService.AuthorityUrl("common"), "租户名转完整 authority");
            CheckEqual("https://login.microsoftonline.com/consumers", AuthService.AuthorityUrl("https://login.microsoftonline.com/consumers"), "已是 URL 时原样返回");
        }

        /// <summary>
        /// IMAP 认证被拒时的提示：Outlook 上“令牌有效但 AUTHENTICATE 失败”通常意味着邮箱未开启 IMAP，
        /// 这时必须给出可执行的处理提示（而不是只显示 Authentication failed）。
        /// </summary>
        private static void TestMailFailureHints()
        {
            Console.WriteLine("[9] 收信失败提示");
            var authError = new MailKit.Security.AuthenticationException("Authentication failed.");

            Check(MailService.IsOutlookHost(), "默认 outlook.office365.com 被识别为 Outlook 邮箱");
            CheckEqual("认证被拒绝：邮箱需开启 IMAP", MailService.Friendly(authError), "Outlook 认证失败给出可读结论");
            Check(MailService.HintFor(authError) != null && MailService.HintFor(authError).Contains("IMAP"),
                "Outlook 认证失败附带“开启 IMAP”的可执行提示");

            MailStatusEventArgs args = new MailStatusEventArgs(MailState.Error, "认证被拒绝", MailService.HintFor(authError));
            Check(args.Hint != null && args.Hint.Length > 0, "状态事件可携带提示（供系统对话框呈现）");
            Check(new MailStatusEventArgs(MailState.Listening, "已连接").Hint == null, "普通状态不带提示");

            Check(MailService.HintFor(new System.Net.Sockets.SocketException(10061)) == null,
                "网络类错误不打扰用户（无提示）");

            string originalHost = Settings.ImapHost;
            try
            {
                Settings.ImapHost = "imap.example.com";
                Check(!MailService.IsOutlookHost(), "非 Outlook 服务器不被误判");
                Check(MailService.HintFor(authError) == null, "非 Outlook 服务器认证失败不提示 IMAP 开关");
            }
            finally
            {
                Settings.ImapHost = originalHost;
            }
        }

        private static void TestInfoFile()
        {
            Console.WriteLine("[10] info.txt 读取");
            string text = InfoText.Read();
            Check(!string.IsNullOrEmpty(text), "info.txt 有内容（或在缺失时返回约定文本）");
            if (InfoText.Exists) Check(!string.Equals(text, InfoText.Missing, StringComparison.Ordinal), "存在 info.txt 时不显示 Err：Not_Exist");
            else CheckEqual(InfoText.Missing, text, "缺失时显示 Err：Not_Exist");
        }
    }
}
