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
            //   可选开关： --interactive | --deep   （允许为 POP/SMTP/Graph 走一次交互同意）
            //              --imap-user <地址>       （指定 XOAUTH2 用户名，可重复）
            //              --imap-basic             （用应用密码做原生 LOGIN 探针）
            //              --skip pop,smtp,graph    （跳过某些探针）
            //              --out <文件>             （输出同时写入文件，便于反馈）
            if (args != null && args.Length > 0)
            {
                // 导出应用图标（多尺寸 ICO，供 exe 与安装包使用）：--make-icon <文件>
                for (int i = 0; i < args.Length; i++)
                {
                    if (string.Equals(args[i], "--make-icon", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    {
                        SaveAppIcon(args[i + 1]);
                        Console.WriteLine("应用图标已导出：" + Path.GetFullPath(args[i + 1]));
                        return 0;
                    }
                }

                // 界面快照：ClassTell.SelfTest.exe --snapshot <目录>
                for (int i = 0; i < args.Length; i++)
                {
                    if (string.Equals(args[i], "--snapshot", StringComparison.OrdinalIgnoreCase))
                    {
                        string dir = i + 1 < args.Length && !args[i + 1].StartsWith("-") ? args[i + 1] : "snapshots";
                        UiTests.SaveSnapshots(dir);
                        Console.WriteLine("界面快照已写入 " + System.IO.Path.GetFullPath(dir));
                        return 0;
                    }
                }

                bool mailCheck;
                DiagOptions options = DiagOptions.Parse(args, out mailCheck);
                if (mailCheck) return MailDiag.Run(options);
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
            TestMailDiagHelpers();
            TestGraphMailHelpers();
            TestRunModeHelpers();
            TestStaleWindow();
            UiTests.Run(Check);

            // 命令行开关放在所有界面测试之后（避免影响窗口行为）
            AppOptions.Parse(new[] { "--tray" });
            Check(AppOptions.StartInTray, "--tray 被识别为“启动后驻留托盘”");
            Check(AppOptions.TrayArgument == "--tray", "自启动参数常量与解析一致（" + AppOptions.TrayArgument + "）");

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

        /// <summary>
        /// 导出多尺寸应用图标（ICO，含 16/24/32/48/64/128/256 的 PNG 图像），
        /// 图形由 IconFactory 直接绘制，保证与界面上的品牌图标完全一致。
        /// </summary>
        private static void SaveAppIcon(string path)
        {
            int[] sizes = { 256, 128, 64, 48, 32, 24, 16 };
            Theme.Apply(Theme.DefaultFontSize, true, Theme.DefaultPaletteKey);   // 设置默认配色/DPI，保证品牌色一致
            var images = new List<byte[]>();
            foreach (int size in sizes)
            {
                using (Bitmap bmp = IconFactory.CreateBrandBitmap(size, true))
                using (var ms = new MemoryStream())
                {
                    bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                    images.Add(ms.ToArray());
                }
            }

            string dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            int headerBytes = 6 + 16 * sizes.Length;
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var writer = new BinaryWriter(fs))
            {
                writer.Write((ushort)0);                       // reserved
                writer.Write((ushort)1);                       // type = icon
                writer.Write((ushort)sizes.Length);            // image count

                int offset = headerBytes;
                for (int i = 0; i < sizes.Length; i++)
                {
                    byte dimension = sizes[i] >= 256 ? (byte)0 : (byte)sizes[i];   // 256 记作 0
                    writer.Write(dimension);                       // width
                    writer.Write(dimension);                       // height
                    writer.Write((byte)0);                         // palette count
                    writer.Write((byte)0);                         // reserved
                    writer.Write((ushort)1);                       // color planes
                    writer.Write((ushort)32);                      // bits per pixel
                    writer.Write(images[i].Length);                // size in bytes
                    writer.Write(offset);                          // offset
                    offset += images[i].Length;
                }

                foreach (byte[] image in images) writer.Write(image);
            }
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

        // ---------- 邮箱诊断辅助逻辑 ----------
        private static void TestMailDiagHelpers()
        {
            Console.WriteLine("[11] 邮箱诊断辅助逻辑（--mail-check）");

            bool mailCheck;
            DiagOptions opt = DiagOptions.Parse(new[]
            {
                "--mail-check", "consumers",
                "--interactive",
                "--imap-basic",
                "--imap-user", "a@outlook.com",
                "--imap-user=b@outlook.com",
                "--skip", "pop,smtp",
                "--out", "diag.txt"
            }, out mailCheck);

            Check(mailCheck, "--mail-check 被识别");
            CheckEqual("consumers", opt.Tenant, "租户参数解析");
            Check(opt.Interactive, "--interactive 解析");
            Check(opt.BasicLogin, "--imap-basic 解析");
            Check(opt.SkipPop && opt.SkipSmtp && !opt.SkipGraph, "--skip pop,smtp 解析（graph 未跳过）");
            CheckEqual("diag.txt", opt.OutFile, "--out 解析");
            CheckEqual("2", opt.ImapUsers.Count.ToString(), "--imap-user 两种写法都支持");
            CheckEqual("a@outlook.com", opt.ImapUsers[0], "--imap-user 顺序保留");
            CheckEqual("b@outlook.com", opt.ImapUsers[1], "--imap-user= 等号写法");

            DiagOptions plain = DiagOptions.Parse(new[] { "whatever" }, out mailCheck);
            Check(!mailCheck, "普通参数不触发诊断");
            Check(plain.Tenant == null && !plain.Interactive && !plain.BasicLogin && plain.ImapUsers.Count == 0,
                "无开关时全部默认关闭");

            DiagOptions bare = DiagOptions.Parse(new[] { "--mail-check" }, out mailCheck);
            Check(mailCheck && bare.Tenant == null, "--mail-check 不带租户也可用");

            Check(MailDiagProbes.ProtocolScopes.Length == 3 &&
                  MailDiagProbes.ProtocolScopes[0] == MailDiagProbes.ImapScope &&
                  MailDiagProbes.ProtocolScopes[1] == MailDiagProbes.PopScope &&
                  MailDiagProbes.ProtocolScopes[2] == MailDiagProbes.SmtpScope,
                "协议全集 = IMAP + POP + SMTP（一次同意即可覆盖）");

            // 用户名候选：命令行 > 设置里的账号 > id_token 登录名 > Graph 主地址（忽略大小写去重）
            string savedAccount = Settings.Account;
            try
            {
                Settings.Account = "主账号@outlook.com";
                List<string> c = MailDiagProbes.BuildUserCandidates(opt, "id@outlook.com", "graph@outlook.com");
                CheckEqual("5", c.Count.ToString(), "候选数量 = 命令行 2 + 账号 + id_token + Graph");
                CheckEqual("a@outlook.com", c[0], "命令行指定的用户名排最前");
                CheckEqual("主账号@outlook.com", c[2], "设置里的账号优先于令牌声明");

                List<string> dup = MailDiagProbes.BuildUserCandidates(opt, "A@OUTLOOK.COM", "主账号@outlook.com");
                CheckEqual("3", dup.Count.ToString(), "忽略大小写去重（a@ 与 主账号@ 各只保留一次）");

                Settings.Account = string.Empty;
                List<string> none = MailDiagProbes.BuildUserCandidates(null, null, null);
                CheckEqual("0", none.Count.ToString(), "无候选时返回空列表（不会误发认证请求）");
            }
            finally
            {
                Settings.Account = savedAccount;
            }
        }

        // ---------- Graph 收信辅助逻辑 ----------
        private static void TestGraphMailHelpers()
        {
            Console.WriteLine("[12] Microsoft Graph 收信（URL / JSON / 解析链路）");

            string listUrl = GraphMailService.UnreadListUrl(20);
            Check(listUrl.StartsWith("https://graph.microsoft.com/v1.0/me/mailFolders/inbox/messages?", StringComparison.Ordinal),
                "未读列表 URL 指向收件箱");
            Check(listUrl.Contains("isRead%20eq%20false"), "只取未读（isRead eq false）");
            Check(listUrl.Contains("$top=20") && listUrl.Contains("receivedDateTime"), "$top 与 $select 均带上");
            Check(GraphMailService.UnreadListUrl(0).Contains("$top=1"), "top 下限被钳制为 1");
            Check(GraphMailService.UnreadListUrl(99999).Contains("$top=1000"), "top 上限被钳制为 1000");

            string mimeUrl = GraphMailService.MimeUrl("AAMkAGUAAAwTW09AAA=");
            Check(mimeUrl.EndsWith("/$value", StringComparison.Ordinal), "MIME 取原文用 /$value");
            Check(mimeUrl.Contains("%3D") && !mimeUrl.Contains("="), "消息 id 里的 = 被 URL 编码");

            const string json = "{\"@odata.context\":\"https://graph.microsoft.com/v1.0/$metadata#x\",\"value\":[" +
                "{\"id\":\"B\",\"subject\":\"C\",\"receivedDateTime\":\"2026-09-25T04:00:00Z\"}," +
                "{\"id\":\"A\",\"subject\":\"tell {值日} \\\"A\\\"\",\"receivedDateTime\":\"2026-09-25T02:00:00Z\"," +
                "\"from\":{\"emailAddress\":{\"name\":\"王老师\",\"address\":\"t@example.com\"}}}]}";

            List<GraphMailRef> refs = GraphMailService.ParseUnreadList(json);
            CheckEqual("2", refs.Count.ToString(), "解析出 2 封未读邮件");
            CheckEqual("A", refs[0].Id, "按接收时间升序（老邮件在前）");
            CheckEqual("B", refs[1].Id, "较新的邮件在后");
            CheckEqual("tell {值日} \"A\"", refs[0].Subject, "标题里的 {} 与转义引号都正确还原");
            CheckEqual(DateTimeOffset.Parse("2026-09-25T02:00:00Z").ToLocalTime().DateTime.ToString("yyyy-MM-dd HH:mm:ss"),
                refs[0].ReceivedLocal.ToString("yyyy-MM-dd HH:mm:ss"), "UTC 时间换算为本地时间");

            CheckEqual("2", GraphMailService.SplitValueObjects(json).Count.ToString(), "切分出 2 个对象（嵌套对象不误切）");
            CheckEqual("0", GraphMailService.ParseUnreadList("{}").Count.ToString(), "没有 value 时返回空列表");
            CheckEqual("0", GraphMailService.ParseUnreadList(null).Count.ToString(), "null 输入不抛异常");
            CheckEqual("中文", GraphMailService.ReadJsonString("{\"subject\":\"\\u4e2d\\u6587\"}", "subject"), "\\uXXXX 转义被解码");
            Check(GraphMailService.ReadJsonString("{\"subject\":null}", "subject") == null, "null 字段返回 null");
            Check(GraphMailService.ReadJsonString("{\"a\":1}", "missing") == null, "缺失字段返回 null");
            CheckEqual("ErrorAccessDenied",
                GraphMailService.ExtractGraphCode("{\"error\":{\"code\":\"ErrorAccessDenied\",\"message\":\"x\"}}"),
                "取 OData 错误码");

            Check(GraphMailService.HashId("AAMkAGUAAAwTW09AAA=") != 0, "id 哈希非 0");
            Check(GraphMailService.HashId("a") != GraphMailService.HashId("b"), "不同 id 哈希不同");
            CheckEqual("0", GraphMailService.HashId(null).ToString(), "空 id 哈希为 0");

            // MIME（Graph /$value 的返回）→ 与 IMAP 完全相同的解析链路
            var message = BuildMail("C", "家长会通知\n本周五 18:00 家长会");
            string reason;
            MessageItem item;
            using (var ms = new MemoryStream())
            {
                message.WriteTo(ms);
                ms.Position = 0;
                MimeMessage roundTrip = MimeMessage.Load(ms);
                item = CommandParser.Parse(roundTrip, GraphMailService.HashId("id-1"), out reason);
            }
            Check(item != null && item.Command == MailCommand.Call, "Graph 取回的 MIME 能走同一套指令解析");
            CheckEqual("家长会通知", item == null ? null : item.Title, "MIME 往返后标题不变");

            // 收信方式 → scope / 名称
            string savedMode = Settings.MailMode;
            try
            {
                Settings.MailMode = "graph";
                Check(AuthService.CurrentScopes()[0].StartsWith("https://graph.microsoft.com/", StringComparison.Ordinal),
                    "Graph 模式使用 Graph 权限");
                CheckEqual("Microsoft Graph", MailSourceFactory.ModeName, "Graph 模式名称");
                Settings.MailMode = "imap";
                Check(AuthService.CurrentScopes()[0].StartsWith("https://outlook.office.com/", StringComparison.Ordinal),
                    "IMAP 模式使用 IMAP 权限");
                CheckEqual("IMAP", MailSourceFactory.ModeName, "IMAP 模式名称");
                Settings.MailMode = "IMAP";
                CheckEqual("imap", Settings.MailMode, "配置值大小写归一化");
            }
            finally
            {
                Settings.MailMode = savedMode;
            }
        }

        // ---------- 运行方式：托盘驻留 / 开机自启动 ----------
        private static void TestRunModeHelpers()
        {
            Console.WriteLine("[13] 运行方式（托盘驻留 / 开机自启动）");

            bool savedTray = Settings.CloseToTray;
            bool savedStartup = Settings.RunAtStartup;
            Console.WriteLine("     （当前用户配置：CloseToTray=" + savedTray + "，RunAtStartup=" + savedStartup + "）");
            try
            {
                // 只校验读写往返，不断言具体取值（这两个都是用户可改的配置项）
                Settings.CloseToTray = false;
                Check(!Settings.CloseToTray, "驻留托盘开关可写入设置");
                Settings.CloseToTray = true;
                Check(Settings.CloseToTray, "驻留托盘开关可切回");

                Settings.RunAtStartup = !savedStartup;
                Check(Settings.RunAtStartup == !savedStartup, "开机自启动开关可写入设置");
                Settings.RunAtStartup = savedStartup;
                Check(Settings.RunAtStartup == savedStartup, "开机自启动开关可还原");
            }
            finally
            {
                Settings.CloseToTray = savedTray;
                Settings.RunAtStartup = savedStartup;
            }

            string cmd = Startup.CommandLine();
            Check(cmd.StartsWith("\"", StringComparison.Ordinal) &&
                  cmd.EndsWith(AppOptions.TrayArgument, StringComparison.Ordinal),
                "自启动命令行 = 带引号的 exe 路径 + " + AppOptions.TrayArgument + "（" + cmd + "）");
            Check(cmd.IndexOf("ClassTell.exe\"", StringComparison.OrdinalIgnoreCase) > 0, "自启动命令指向 ClassTell.exe");
            Check(Startup.ValueName == "ClassTell", "自启动注册表值名固定为 " + Startup.ValueName + "（便于关闭/卸载时删除）");
            bool enabled = Startup.IsEnabled();
            Check(true, "读取开机自启动状态不抛异常（当前：" + (enabled ? "已开启" : "未开启") + "）");
        }

        // ---------- 过期邮件窗口（打开软件前 24 小时） ----------
        private static void TestStaleWindow()
        {
            Console.WriteLine("[14] 过期邮件窗口（打开软件前 24 小时内：只显示、不提醒）");

            int savedHours = Settings.StaleWindowHours;
            DateTime now = DateTime.Now;
            try
            {
                MailWindow.SetAppStarted(now);
                Settings.StaleWindowHours = 24;

                Check(MailWindow.AppStartedLocal == now, "可注入启动时间用于判定");
                Check(MailWindow.IsStale(now.AddMinutes(-1)), "启动前 1 分钟收到的邮件算“过期”");
                Check(!MailWindow.IsStale(now.AddMinutes(1)), "启动后收到的邮件不算“过期”");
                Check(MailWindow.InBackfillWindow(now.AddHours(-2)), "启动前 2 小时在 24 小时回填窗口内");
                Check(!MailWindow.InBackfillWindow(now.AddHours(-30)), "启动前 30 小时超出回填窗口");
                CheckEqual(now.AddHours(-24).ToString("yyyy-MM-dd HH:mm"), MailWindow.SinceLocal.ToString("yyyy-MM-dd HH:mm"),
                    "回填起点 = 启动时间 - 24 小时");
                Check(MailWindow.SinceUtc.Kind == DateTimeKind.Utc, "Graph 过滤使用 UTC 时间");
                Check(MailWindow.StaleSubtitle().Contains("24"), "过期页说明包含窗口小时数");
                Check(MailWindow.StaleEmptyHint().Contains("24"), "过期页空状态提示包含窗口小时数");

                Settings.StaleWindowHours = 0;
                Check(!MailWindow.InBackfillWindow(now.AddHours(-2)), "窗口设为 0 时不再回填过期邮件");
                Check(MailWindow.SinceLocal == now, "窗口设为 0 时起点等于启动时间");

                Settings.StaleWindowHours = 24;
                string url = GraphMailService.UnreadListUrl(100, MailWindow.SinceUtc);
                Check(url.Contains("isRead%20eq%20false%20and%20receivedDateTime%20ge%20"),
                    "Graph 过滤 = 未读 and 时间窗口");
                string dated = GraphMailService.UnreadListUrl(50, new DateTime(2026, 9, 24, 4, 0, 0, DateTimeKind.Utc));
                Check(dated.Contains("2026-09-24T04:00:00Z"), "时间过滤按 ISO8601 UTC 输出（" + dated + "）");
                Check(dated.Contains("$top=50"), "$top 与时间窗口同时生效");
            }
            finally
            {
                Settings.StaleWindowHours = savedHours;
                MailWindow.SetAppStarted(DateTime.Now);
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
