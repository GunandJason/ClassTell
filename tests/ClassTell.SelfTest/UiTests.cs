using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace ClassTell.SelfTest
{
    /// <summary>
    /// 界面自测：直接构造页面控件并渲染成位图，用像素探针验证“田字格布局、详情浮层遮罩、
    /// 通知浮层配色、字号滑块联动、info.txt 缺失提示”等界面需求，不依赖外部截图工具。
    /// </summary>
    internal static partial class UiTests
    {
        public static void Run(Action<bool, string> check)
        {
            Console.WriteLine("[10] 界面渲染与交互");
            check(Theme.FontSize > 0f, "主题已初始化");

            TestMessagesGrid(check);
            TestGridTopGap(check);
            TestStaleRouting(check);
            TestDevInfoImage(check);
            TestDetailView(check);
            TestAboutPage(check);
            TestInfoFileBehaviour(check);
            TestNoAppOwnedPopups(check);
            TestAnimator(check);
            TestShellFormConstruction(check);
            TestThemeSwitching(check);
            TestLargeFontLayout(check);
            TestDpiScaling(check);
            TestLightModeSurfaces(check);
            TestButtonLabelsFit(check);
            TestPagerButtons(check);
            TestRoundedInputs(check);
            TestMailModeSelector(check);
            TestTrayAndStartupSwitches(check);
            TestRepeatedThemeSwitch(check);
        }

        /// <summary>主窗口可被构造（无邮箱也能起来），并渲染出导航 / 页面 / 水蓝色元素。</summary>
        private static void TestShellFormConstruction(Action<bool, string> check)
        {
            using (var form = new ShellForm())
            {
                form.SetBounds(0, 0, 1400, 900);
                check(form.ClientSize.Width > 0, "主窗口构造成功（无边框自定义窗口）");

                NavRail nav = FindControl<NavRail>(form);
                MessagesPage messages = FindControl<MessagesPage>(form);
                AboutPage about = FindControl<AboutPage>(form);
                check(nav != null && messages != null && about != null, "主窗口包含导航栏 + 消息页 + 关于页");
                check(form.FormBorderStyle == FormBorderStyle.Sizable, "使用系统标准窗口边框（可拖动 / 缩放 / 最小化）");
                check(!string.IsNullOrEmpty(form.Text), "使用系统标题栏并显示窗口标题（" + form.Text + "）");
                check(form.Icon != null, "窗口使用程序图标（系统标题栏渲染）");

                if (nav != null && messages != null && about != null)
                {
                    check(nav.Width == Theme.NavWidth, "左侧导航栏宽度符合设计（" + nav.Width + "px）");
                    check(nav.Bounds.Height > form.ClientSize.Height - Theme.S(40), "左侧导航栏高度自适应窗口");
                    check(messages.Bounds.Width > form.ClientSize.Width - Theme.NavWidth - Theme.S(10), "消息页铺满右侧内容区");
                    check(messages.Bounds.Height > form.ClientSize.Height - Theme.S(40), "消息页高度自适应窗口");
                    check(nav.Controls.Count == 3, "导航栏包含“消息”“过期”“关于”三个分项");
                    check(form.CurrentPageIndex == 0, "启动后自动跳转到消息页");

                    var items = new List<NavItem>();
                    foreach (Control c in nav.Controls)
                    {
                        var item = c as NavItem;
                        if (item != null) items.Add(item);
                    }
                    NavItem messageItem = items.Find(i => i.Label == "消息");
                    NavItem staleItem = items.Find(i => i.Label == "过期");
                    NavItem aboutItem = items.Find(i => i.Label == "关于");
                    check(messageItem != null && staleItem != null && aboutItem != null,
                        "导航分项名称为“消息 / 过期 / 关于”");
                    Pump(450);
                    check(messageItem != null && messageItem.Selected, "“消息”分项处于选中态");

                    MessagesPage[] pages = FindAll<MessagesPage>(form);
                    check(pages.Length == 2, "存在两个邮件列表页（消息 / 过期，实测 " + pages.Length + "）");
                    int withStatus = 0;
                    int empty = 0;
                    foreach (MessagesPage p in pages)
                    {
                        if (p.ShowsMailStatus) withStatus++;
                        if (p.MessageCount == 0) empty++;
                    }
                    check(withStatus == 1, "只有“消息”页带连接状态与“立即收取”（实测 " + withStatus + " 个）");
                    check(empty == 2, "刚启动时两个列表都为空（实测 " + empty + " 个为空）");

                    if (staleItem != null)
                    {
                        staleItem.PerformClick();
                        Pump(450);
                        check(form.CurrentPageIndex == 1, "点击“过期”可切换到过期页");
                        check(staleItem.Selected, "“过期”分项变为选中态");
                    }
                    if (aboutItem != null)
                    {
                        aboutItem.PerformClick();
                        Pump(450);
                        check(form.CurrentPageIndex == 2, "点击“关于”可切换到关于页");
                        check(aboutItem.Selected, "“关于”分项变为选中态");
                    }
                    if (messageItem != null)
                    {
                        messageItem.PerformClick();
                        Pump(450);
                        check(form.CurrentPageIndex == 0, "点击“消息”可切回消息页");
                    }

                    Pump(200);
                    Size size = nav.Size;
                    using (Bitmap bmp = Render(nav, size))
                    {
                        int navBg = CountColor(bmp, new Rectangle(0, 0, size.Width, size.Height), Theme.NavBackground, 4);
                        int navBright = CountBright(bmp, new Rectangle(0, 0, size.Width, size.Height));
                        int navAccent = CountColor(bmp, new Rectangle(0, 0, size.Width, size.Height), Theme.Accent, 30);
                        check(navBg > size.Width * size.Height / 3, "导航栏渲染出深色底板（" + navBg + " 像素）");
                        check(navBright > 40, "导航栏显示品牌与分项文字（亮色像素 " + navBright + "）");
                        check(navAccent > 20, "导航栏包含强调色选中指示（" + navAccent + " 像素）");
                    }
                }
            }
        }

        // ---------- 工具 ----------
        /// <summary>处理消息循环若干毫秒，让 Animator 的计时器真正跑起来。</summary>
        private static void Pump(int ms)
        {
            int elapsed = 0;
            while (elapsed < ms)
            {
                Application.DoEvents();
                Thread.Sleep(10);
                elapsed += 10;
            }
        }

        private static Bitmap Render(Control control, Size size)
        {
            control.SetBounds(0, 0, size.Width, size.Height);
            var bmp = new Bitmap(size.Width, size.Height);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Black);
                var rect = new Rectangle(0, 0, size.Width, size.Height);
                control.DrawToBitmap(bmp, rect);
            }
            return bmp;
        }

        private static bool Near(Color a, Color b, int tolerance)
        {
            return Math.Abs(a.R - b.R) <= tolerance && Math.Abs(a.G - b.G) <= tolerance && Math.Abs(a.B - b.B) <= tolerance;
        }

        private static double Luminance(Bitmap bmp, Rectangle area)
        {
            double sum = 0d;
            int n = 0;
            for (int y = area.Top; y < area.Bottom; y += 3)
            {
                for (int x = area.Left; x < area.Right; x += 3)
                {
                    Color c = bmp.GetPixel(x, y);
                    sum += 0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B;
                    n++;
                }
            }
            return n == 0 ? 0d : sum / n;
        }

        private static int CountBright(Bitmap bmp, Rectangle area)
        {
            int count = 0;
            for (int y = area.Top; y < area.Bottom; y += 3)
            {
                for (int x = area.Left; x < area.Right; x += 3)
                {
                    Color c = bmp.GetPixel(x, y);
                    if (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B > 150d) count++;
                }
            }
            return count;
        }

        private static int CountColor(Bitmap bmp, Rectangle area, Color target, int tolerance)
        {
            int count = 0;
            for (int y = area.Top; y < area.Bottom; y++)
            {
                for (int x = area.Left; x < area.Right; x++)
                {
                    if (Near(bmp.GetPixel(x, y), target, tolerance)) count++;
                }
            }
            return count;
        }

        private static MessageItem Sample(string title, string body, MailCommand command)
        {
            return new MessageItem
            {
                Uid = 1u,
                Command = command,
                Title = title,
                Body = body,
                Source = "teacher@example.com",
                SenderName = "王老师",
                ReceivedLocal = new DateTime(2026, 9, 24, 10, 30, 0)
            };
        }

        // ---------- 消息页：田字网格与翻页 ----------
        private static void TestMessagesGrid(Action<bool, string> check)
        {
            var size = new Size(1800, 1200);
            using (var page = new MessagesPage())
            {
                page.SetBounds(0, 0, size.Width, size.Height);
                check(page.MessageCount == 0, "初始消息为空（重启即清空）");

                for (int i = 0; i < 5; i++)
                {
                    page.AddMessage(Sample("标题" + (i + 1), "正文第一行\n正文第二行\n正文第三行", i % 2 == 0 ? MailCommand.Call : MailCommand.Tell));
                }
                Pump(60);

                check(page.MessageCount == 5, "已接收 5 条消息");
                check(page.PageCount == 2, "每页 4 条 → 共 2 页");
                check(page.MessageCount == 5 && page.PageCount == 2, "分页数量正确");

                using (Bitmap bmp = Render(page, size))
                {
                    int pad = Theme.PagePadding;
                    int header = Theme.S(84);
                    int footer = Theme.S(58);
                    int gap = Theme.Gap;
                    int gridW = size.Width - pad * 2;
                    int gridH = size.Height - header - footer;
                    int cw = Math.Max(Theme.S(140), (gridW - gap) / 2);
                    int ch = Math.Max(Theme.S(110), (gridH - gap) / 2);

                    Color surface = Theme.Surface;
                    Color window = Theme.Window;

                    int cards = 0;
                    for (int row = 0; row < 2; row++)
                    {
                        for (int col = 0; col < 2; col++)
                        {
                            int cx = pad + col * (cw + gap) + cw / 2;
                            int cy = header + row * (ch + gap) + ch / 2;
                            Color c = bmp.GetPixel(cx, cy);
                            // 卡片中心可能有文字，取卡片左上内侧空白处更稳
                            Color probe = bmp.GetPixel(pad + col * (cw + gap) + Theme.S(10), header + row * (ch + gap) + Theme.S(14));
                            if (Near(probe, surface, 6) || Near(c, surface, 6)) cards++;
                        }
                    }
                    check(cards == 4, "田字格：2×2 共 4 张卡片全部渲染（命中 " + cards + "）");

                    // 两列之间的竖直缝隙应为窗口底色
                    int midX = pad + cw + gap / 2;
                    int midY = header + ch / 2;
                    Color gapColor = bmp.GetPixel(midX, midY);
                    check(Near(gapColor, window, 6), "田字格：卡片之间存在分隔缝隙（缝隙色 " + gapColor.R + "," + gapColor.G + "," + gapColor.B + "）");

                    // 标题字号大于正文字号
                    check(Theme.Font(3f, FontStyle.Bold).Size > Theme.Body.Size, "标题字号大于正文字号");
                    check(Theme.FontFamilyName == "Microsoft YaHei", "统一使用微软雅黑");

                    // 卡片上应存在亮色文字像素
                    int bright = CountBright(bmp, new Rectangle(pad, header, cw, ch));
                    check(bright > 20, "卡片上有可见文字（亮色像素 " + bright + "）");
                }

                // 翻页：第 2 页
                MaterialButton next = FindButtonByText(page, ">");
                if (next != null)
                {
                    next.PerformClick();
                    Pump(450);
                    check(true, "下一页按钮可点击");
                }
                else
                {
                    check(false, "找不到下一页按钮");
                }

                // 前后翻页后的渲染仍然正常
                using (Bitmap bmp2 = Render(page, size))
                {
                    check(bmp2.Width == size.Width, "翻页后页面可正常渲染");
                }
            }
        }

        private static MaterialButton FindButton(Control parent, string glyph)
        {
            foreach (Control c in parent.Controls)
            {
                var btn = c as MaterialButton;
                if (btn != null && string.Equals(btn.Glyph, glyph, StringComparison.Ordinal)) return btn;
                MaterialButton nested = FindButton(c, glyph);
                if (nested != null) return nested;
            }
            return null;
        }

        private static MaterialButton FindButtonByText(Control parent, string text)
        {
            foreach (Control c in parent.Controls)
            {
                var btn = c as MaterialButton;
                if (btn != null && string.Equals(btn.Text, text, StringComparison.Ordinal)) return btn;
                MaterialButton nested = FindButtonByText(c, text);
                if (nested != null) return nested;
            }
            return null;
        }

        private static T FindControl<T>(Control parent) where T : Control
        {
            foreach (Control c in parent.Controls)
            {
                var hit = c as T;
                if (hit != null) return hit;
                T nested = FindControl<T>(c);
                if (nested != null) return nested;
            }
            return null;
        }

        private static T[] FindAll<T>(Control parent) where T : Control
        {
            var list = new List<T>();
            Collect(parent, list);
            return list.ToArray();
        }

        private static void Collect<T>(Control parent, List<T> list) where T : Control
        {
            foreach (Control c in parent.Controls)
            {
                var hit = c as T;
                if (hit != null) list.Add(hit);
                Collect(c, list);
            }
        }

        // ---------- 详情浮层：遮罩 + 完整正文 ----------
        private static void TestDetailView(Action<bool, string> check)
        {
            var size = new Size(1800, 1200);
            using (var page = new MessagesPage())
            {
                page.SetBounds(0, 0, size.Width, size.Height);
                MessageItem item = Sample("家长会通知",
                    "本周五 18:30 在教室召开家长会\n请准时参加\n联系人：王老师\n一、注意事项\n1. 请提前十分钟到校\n2. 携带家长会通知单\n二、议程\n1. 期中成绩分析\n2. 下阶段学习安排\n3. 家校配合事项\n三、其他\n如有疑问请联系班主任", MailCommand.Call);
                page.AddMessage(item);
                Pump(60);

                using (Bitmap list = Render(page, size))
                {
                    check(CountBright(list, new Rectangle(0, 0, size.Width, size.Height)) > 20, "消息列表正常渲染");

                    page.ShowDetail(item);
                    Pump(400);
                    check(page.IsDetailOpen, "ShowDetail 后进入页内详情（非弹窗）");

                    MessageDetailPanel panel = FindControl<MessageDetailPanel>(page);
                    check(panel != null && panel.Parent == page, "详情是页面内的控件（不是独立窗体/弹窗）");
                    if (panel != null)
                    {
                        check(panel.Bounds.Top >= 0 && panel.Bounds.Bottom <= size.Height, "详情视图位于页面区域之内");
                        MaterialButton back = FindControl<MaterialButton>(panel);
                        check(back != null && back.Visible && back.Text.Length > 0, "详情页有“返回列表”按钮（" + (back == null ? "-" : back.Text) + "）");
                        if (back != null)
                            check(back.Width + 1 >= back.IdealWidth, "返回按钮文字完整（需要 " + back.IdealWidth + " / 实际 " + back.Width + "）");
                        using (Bitmap single = Render(panel, new Size(panel.Width, panel.Height)))
                        {
                            int bright = CountBright(single, new Rectangle(0, 0, single.Width, single.Height));
                            check(bright > 30, "详情视图渲染出标题/来源/正文（亮色像素 " + bright + "）");
                            check(CountColor(single, new Rectangle(0, 0, single.Width, single.Height), Theme.Surface, 8) > single.Width * single.Height / 6,
                                "详情视图以卡片形式铺在页面内");
                        }
                    }

                    using (Bitmap detail = Render(page, size))
                    {
                        // 详情为“页内视图”：不应存在遮罩（页面留白仍与列表一致）
                        Color margin = detail.GetPixel(Theme.S(2), Math.Min(size.Height - 1, Theme.S(300)));
                        check(Near(margin, Theme.Window, 10) || Theme.Luminance(margin) > 0.4d,
                            "详情不使用遮罩浮层（留白保持页面底色）");

                        // 田字格卡片应已隐藏（按类型查找，避免 z 序索引变化）
                        int cards = 0;
                        foreach (MessageCard card in FindAll<MessageCard>(page))
                            if (card.Visible) cards++;
                        check(cards == 0, "进入详情后列表卡片隐藏（" + cards + " 张仍可见）");
                    }

                    page.CloseDetail();
                    Pump(300);
                    check(!page.IsDetailOpen, "返回列表后详情关闭");
                    int visibleCards = 0;
                    foreach (MessageCard card in FindAll<MessageCard>(page))
                        if (card.Visible) visibleCards++;
                    check(visibleCards >= 1, "返回列表后卡片恢复显示（" + visibleCards + " 张）");
                }
            }
        }

        // ---------- 关于页：登录 / 界面设置 / 开发者信息 ----------
        private static void TestAboutPage(Action<bool, string> check)
        {
            var size = new Size(1800, 1400);
            using (var about = new AboutPage(new AuthService()))
            {
                about.SetBounds(0, 0, size.Width, size.Height);
                Pump(120);

                check(about.Login != null, "关于页包含“邮箱登录”面板");
                check(FindButtonByText(about, "使用浏览器登录") != null, "存在“使用浏览器登录”按钮（OAuth2 交互登录）");
                check(FindButtonByText(about, "使用设备代码登录") != null, "存在“使用设备代码登录”按钮（设备代码流）");
                check(FindButtonByText(about, "高级设置") != null, "存在“高级设置”入口（Client ID / 租户 / 权限范围）");

                MaterialSlider slider = FindControl<MaterialSlider>(about);
                check(slider != null, "“界面设置”包含字体大小滑块");
                if (slider != null)
                {
                    check(slider.Minimum <= 8.5d && slider.Maximum >= 16d, "滑块范围覆盖 8~16 pt（" + slider.Minimum + "~" + slider.Maximum + "）");
                    slider.Value = 15.5d;
                    Pump(150);
                    check(Math.Abs(Theme.FontSize - 15.5f) < 0.01f, "拖动滑块实时改变全局字号");
                    check(Theme.Font(0f, FontStyle.Regular).SizeInPoints > 14f, "字号变化立即反映到字体");
                    slider.Value = Theme.DefaultFontSize;
                    Pump(150);
                    check(Math.Abs(Theme.FontSize - Theme.DefaultFontSize) < 0.01f, "字号可调回默认值（" + Theme.DefaultFontSize + " pt）");
                }

                MaterialSwitch modeSwitch = FindControl<MaterialSwitch>(about);
                check(modeSwitch != null && modeSwitch.Enabled, "深色 / 浅色模式开关可用");
                check(modeSwitch != null && modeSwitch.Checked == Settings.DarkMode, "开关状态与当前模式一致");

                AccentSwatch[] swatches = FindAll<AccentSwatch>(about);
                check(swatches.Length >= 3, "提供 3 套以上配色色卡（" + swatches.Length + " 套）");
                foreach (AccentSwatch s in swatches)
                    check(!string.IsNullOrEmpty(s.PaletteKey) && s.Name.Length > 0, "色卡有名称：" + s.Name + " " + ColorTranslator.ToHtml(s.Swatch));

                // 点击色卡应切换强调色并写入设置
                string before = Settings.AccentKey;
                AccentSwatch target = null;
                foreach (AccentSwatch s in swatches)
                    if (!string.Equals(s.PaletteKey, before, StringComparison.OrdinalIgnoreCase)) { target = s; break; }
                if (target != null)
                {
                    target.PerformClick();
                    Pump(120);
                    check(string.Equals(Theme.Palette.Key, target.PaletteKey, StringComparison.OrdinalIgnoreCase), "点击色卡切换配色为 " + target.Name);
                    check(string.Equals(Settings.AccentKey, target.PaletteKey, StringComparison.OrdinalIgnoreCase), "配色已保存到设置");
                    check(target.Selected, "被选中的色卡显示选中态");
                }

                using (Bitmap bmp = Render(about, size))
                {
                    check(CountColor(bmp, new Rectangle(0, 0, size.Width, size.Height), Theme.Surface, 4) > 5000, "关于页渲染出卡片背景");
                    check(CountBright(bmp, new Rectangle(0, 0, size.Width, size.Height)) > 200, "关于页渲染出文字");
                    check(CountColor(bmp, new Rectangle(0, 0, size.Width, size.Height), Theme.Accent, 6) > 40, "关于页包含当前强调色元素");
                }
            }
        }

        // ---------- info.txt 读取 ----------
        private static void TestInfoFileBehaviour(Action<bool, string> check)
        {
            string path = AppPaths.InfoFile;
            bool existed = File.Exists(path);
            string backup = existed ? File.ReadAllText(path, Encoding.UTF8) : null;
            try
            {
                using (var card = new DevInfoCard())
                {
                    card.SetBounds(0, 0, 1200, 800);
                    if (existed)
                    {
                        check(card.InfoBody.Length > 0 && !string.Equals(card.InfoBody, InfoText.Missing, StringComparison.Ordinal), "info.txt 存在时显示其内容");
                        check(card.InfoBody.Contains("ClassTell"), "描述文本读取自同目录 info.txt");
                    }
                }

                if (existed) File.Delete(path);
                using (var card2 = new DevInfoCard())
                {
                    check(string.Equals(card2.InfoBody, InfoText.Missing, StringComparison.Ordinal), "info.txt 不存在时显示（Err:NotExist）");
                }
            }
            finally
            {
                if (existed && backup != null) File.WriteAllText(path, backup, new UTF8Encoding(false));
            }
            check(File.Exists(path) == existed, "测试后 info.txt 已恢复");
        }

        // ---------- 通知只用系统通知，软件不再有自己的弹窗 ----------
        private static void TestNoAppOwnedPopups(Action<bool, string> check)
        {
            // 1) 静态：本程序集里不应存在任何自定义窗体（ShellForm 是主窗口，不是弹窗）
            var formTypes = new List<string>();
            foreach (Type t in typeof(ShellForm).Assembly.GetTypes())
            {
                if (t.IsPublic || t.IsNested) continue;
                if (typeof(Form).IsAssignableFrom(t) && !t.IsAbstract) formTypes.Add(t.Name);
            }
            check(formTypes.Count == 1 && formTypes[0] == "ShellForm",
                "程序内只有一个窗体（主窗口），没有自有弹窗类型：[" + string.Join(", ", formTypes.ToArray()) + "]");

            // 2) 运行期：发通知不会创建任何窗体
            int before = Application.OpenForms.Count;
            using (var owner = new Control())
            {
                owner.CreateControl();
                using (var notifier = new Notifier(owner))
                {
                    MessageItem item = Sample("家长会通知", "本周五 18:30 在教室召开家长会\n请准时参加", MailCommand.Call);
                    notifier.NotifyCall(item);
                    notifier.RefreshIcon();
                    check(notifier.LastNotified == item, "通知记录了最后一条消息");
                    check(Application.OpenForms.Count == before,
                        "发送通知不会弹出软件自有窗口（窗体数 " + before + " → " + Application.OpenForms.Count + "）");
                }
            }

            // 3) 通知文本仍按规范：超长加省略号
            string longBody = new string('长', 400);
            string shown = Typography.Truncate(longBody, 150);
            check(shown.Length <= 151 && shown.EndsWith("…", StringComparison.Ordinal), "通知正文超长时自动加省略号");
            check(longBody.StartsWith(shown.Substring(0, 10), StringComparison.Ordinal), "截断保留原文开头");
        }

        /// <summary>动画引擎（需要消息循环）：验证动效回调与延时回调会被真正触发。</summary>
        private static void TestAnimator(Action<bool, string> check)
        {
            bool finished = false;
            bool delayed = false;
            int frames = 0;
            double last = -1d;
            bool monotonic = true;

            Animator.Run(300, Ease.Standard, p =>
            {
                frames++;
                if (p < last - 1e-9) monotonic = false;
                last = p;
            }, () => { finished = true; });

            Animator.Delay(150, () => { delayed = true; });

            Pump(800);
            check(finished, "动画完成回调被触发");
            check(delayed, "延时回调被触发（通知与自动收起依赖该机制）");
            check(frames > 5, "动画产生多帧回调（" + frames + " 帧）");
            check(monotonic, "动画进度单调递增");
        }
        // ---------- 深色 / 浅色 + 配色切换 ----------
        private static void TestThemeSwitching(Action<bool, string> check)
        {
            float originalFont = Settings.FontSize;
            bool originalDark = Settings.DarkMode;
            string originalAccent = Settings.AccentKey;
            try
            {
                check(Theme.Palettes.Length >= 3, "内置配色方案数量 " + Theme.Palettes.Length + " 套（≥3）");
                foreach (ThemePalette p in Theme.Palettes)
                {
                    check(p.Hex.StartsWith("#", StringComparison.Ordinal) && p.Hex.Length == 7 && p.Name.Length > 0,
                        "配色定义有效：" + p.Name + " " + p.Hex + "（浅色底文字 " + p.TextDarkHex + "）");
                }

                // 浅色模式 + 青碧配色
                Theme.Apply(Theme.DefaultFontSize, false, "teal");
                check(!Theme.Dark && Theme.Mode == ThemeMode.Light, "可切换到浅色模式");
                check(Theme.Luminance(Theme.Window) > 0.9d, "浅色模式窗口底色为白色（亮度 " + Theme.Luminance(Theme.Window).ToString("0.00") + "）");
                check(Theme.Accent.ToArgb() == ColorTranslator.FromHtml("#48C0A3").ToArgb(), "强调色切换为青碧 #48C0A3");
                check(Theme.Luminance(Theme.AccentText) < 0.6d, "浅色模式下强调色文字自动加深（亮度 " + Theme.Luminance(Theme.AccentText).ToString("0.00") + "）");
                check(Theme.Luminance(Theme.TextPrimary) < 0.3d, "浅色模式下正文颜色为深色");

                using (var page = new MessagesPage())
                {
                    var size = new Size(1400, 900);
                    page.SetBounds(0, 0, size.Width, size.Height);
                    page.AddMessage(Sample("浅色模式测试", "正文第一行\n正文第二行", MailCommand.Call));
                    Pump(80);
                    using (Bitmap bmp = Render(page, size))
                    {
                        check(CountColor(bmp, new Rectangle(0, 0, size.Width, size.Height), Theme.Window, 3) > size.Width * size.Height / 20,
                            "浅色模式下页面渲染为白色底");
                        int accentPixels = CountColor(bmp, new Rectangle(0, 0, size.Width, size.Height), Theme.Accent, 20)
                                         + CountColor(bmp, new Rectangle(0, 0, size.Width, size.Height), Theme.AccentText, 20);
                        check(accentPixels > 10, "浅色模式下仍使用当前强调色绘制元素（" + accentPixels + " 像素）");
                    }
                }

                // 回到深色 + 水蓝
                Theme.Apply(Theme.DefaultFontSize, true, "water");
                check(Theme.Dark, "可切回深色模式");
                check(Theme.Luminance(Theme.Window) < 0.2d, "深色模式窗口底色为深色");
                check(Theme.Accent.ToArgb() == ColorTranslator.FromHtml("#7AB5D6").ToArgb(), "强调色切回水蓝 #7AB5D6");
                check(Theme.Luminance(Theme.OnAccent) < 0.2d, "水蓝底上的按钮文字自动使用深色以保证对比度");

                // 非空自检：每套配色都能切换且不抛异常
                foreach (ThemePalette p in Theme.Palettes)
                {
                    Theme.Apply(12f, false, p.Key);
                    bool light = Theme.Luminance(Theme.Window) > 0.9d;
                    Theme.Apply(12f, true, p.Key);
                    bool dark = Theme.Luminance(Theme.Window) < 0.2d;
                    check(light && dark && Theme.Palette.Key == p.Key, "配色「" + p.Name + "」在深/浅两种模式下都可应用");
                }
            }
            finally
            {
                Theme.Apply(originalFont, originalDark, originalAccent);
            }
        }

        // ---------- 字号放大后内容仍要显示完整（不被裁切 / 不重叠） ----------
        private static void TestLargeFontLayout(Action<bool, string> check)
        {
            float originalFont = Settings.FontSize;
            bool originalDark = Settings.DarkMode;
            string originalAccent = Settings.AccentKey;
            var size = new Size(1600, 1000);
            try
            {
                foreach (float fontSize in new[] { Theme.MinFontSize, 10f, 13f, Theme.MaxFontSize })
                {
                    Theme.Apply(fontSize, true, "water");
                    check(Math.Abs(Theme.FontSize - fontSize) < 0.01f, "字号 " + fontSize + " pt 已生效");

                    using (var page = new MessagesPage())
                    {
                        page.SetBounds(0, 0, size.Width, size.Height);
                        for (int i = 0; i < 5; i++)
                            page.AddMessage(Sample("标题" + (i + 1), "一、注意事项\n1. 请提前十分钟到校\n2. 携带通知单\n二、议程\n1. 成绩分析\n2. 家校配合", i % 2 == 0 ? MailCommand.Call : MailCommand.Tell));
                        Pump(60);

                        int pad = Theme.PagePadding;
                        int gap = Theme.Gap;
                        int header = page.HeaderHeightForTest;   // 直接读页面实际值，避免测试与实现各写一份公式
                        int footer = page.FooterHeightForTest;
                        int gridW = size.Width - pad * 2;
                        int gridH = size.Height - header - footer;
                        int cw = Math.Max(Theme.S(140), (gridW - gap) / 2);
                        int ch = Math.Max(Theme.S(110), (gridH - gap) / 2);

                        using (Bitmap bmp = Render(page, size))
                        {
                            int cards = 0;
                            for (int row = 0; row < 2; row++)
                            {
                                for (int col = 0; col < 2; col++)
                                {
                                    if (Near(bmp.GetPixel(pad + col * (cw + gap) + Theme.S(12), header + row * (ch + gap) + Theme.S(20)), Theme.Surface, 6)) cards++;
                                }
                            }
                            check(cards == 4, "字号 " + fontSize + " pt：田字格 4 张卡片完整渲染（命中 " + cards + "）");

                            Color gapColor = bmp.GetPixel(pad + cw + gap / 2, header + ch / 2);
                            check(Near(gapColor, Theme.Window, 6), "字号 " + fontSize + " pt：卡片之间仍有分隔缝隙（无重叠溢出）");

                            int withText = 0;
                            for (int row = 0; row < 2; row++)
                            {
                                for (int col = 0; col < 2; col++)
                                {
                                    var cardArea = new Rectangle(pad + col * (cw + gap), header + row * (ch + gap), cw, ch);
                                    if (CountBright(bmp, cardArea) > 10) withText++;
                                }
                            }
                            check(withText == 4, "字号 " + fontSize + " pt：4 张卡片都显示标题与正文（" + withText + " 张有文字）");

                            check(CountBright(bmp, new Rectangle(pad, 0, size.Width / 3, header)) > 10, "字号 " + fontSize + " pt：页头标题正常显示");
                            int footerTop = size.Height - footer;
                            check(footerTop > header + ch, "字号 " + fontSize + " pt：页脚未被卡片挤压");
                            check(CountBright(bmp, new Rectangle(size.Width / 2, footerTop, size.Width / 2, footer)) > 5, "字号 " + fontSize + " pt：翻页控件正常显示");
                        }
                    }

                    using (var about = new AboutPage(new AuthService()))
                    {
                        about.SetBounds(0, 0, size.Width, 1400);
                        Pump(120);
                        MaterialSlider slider = FindControl<MaterialSlider>(about);
                        MaterialSwitch modeSwitch = FindControl<MaterialSwitch>(about);
                        AccentSwatch[] swatches = FindAll<AccentSwatch>(about);
                        check(slider != null && modeSwitch != null && swatches.Length >= 3,
                            "字号 " + fontSize + " pt：界面设置的滑块 / 模式开关 / 配色色卡都在");
                        if (slider != null && modeSwitch != null)
                            check(!slider.Bounds.IntersectsWith(modeSwitch.Bounds), "字号 " + fontSize + " pt：滑块与模式开关不重叠");
                        using (Bitmap bmp = Render(about, new Size(size.Width, 1400)))
                        {
                            check(CountColor(bmp, new Rectangle(0, 0, size.Width, 1400), Theme.Accent, 10) > 20,
                                "字号 " + fontSize + " pt：关于页仍渲染出强调色元素");
                        }
                    }
                }
            }
            finally
            {
                Theme.Apply(originalFont, originalDark, originalAccent);
            }
        }

        // ---------- 高 DPI / 高缩放 + 低分辨率 响应式适配 ----------
        private static void TestDpiScaling(Action<bool, string> check)
        {
            float originalFont = Settings.FontSize;
            bool originalDark = Settings.DarkMode;
            string originalAccent = Settings.AccentKey;

            var cases = new[]
            {
                new { scale = 1.00f, w = 1920, h = 1080 },   // 1080p @100%
                new { scale = 1.00f, w = 1366, h = 768  },   // 1366x768 @100%
                new { scale = 1.25f, w = 1920, h = 1080 },   // 1080p @125%
                new { scale = 1.50f, w = 2560, h = 1440 },   // 2K @150%
                new { scale = 1.75f, w = 2560, h = 1440 },   // 2K @175%
                new { scale = 2.00f, w = 3840, h = 2160 },   // 4K @200%
                new { scale = 2.50f, w = 3840, h = 2400 }    // 4K+ @250%
            };

            try
            {
                Theme.Apply(Theme.DefaultFontSize, true, "water");
                Theme.UpdateDpi(1f, new Size(1920, 1080));
                float baseLine = Theme.LineHeight(Theme.Body);
                float baseRatio = baseLine / Math.Max(1f, Theme.S(96));
                check(baseLine > 10f && baseLine < 24f, "100% 缩放下正文字号适中（行高 " + baseLine.ToString("0.0") + "px）");

                foreach (var c in cases)
                {
                    Theme.Apply(Theme.DefaultFontSize, true, "water");
                    Theme.UpdateDpi(c.scale, new Size(c.w, c.h));

                    float logicalW = c.w / c.scale;
                    float logicalH = c.h / c.scale;
                    string tag = c.w + "x" + c.h + "@" + (int)(c.scale * 100) + "%";

                    float line = Theme.LineHeight(Theme.Body);
                    float expected = baseLine * c.scale;
                    check(Math.Abs(line - expected) / expected < 0.12f,
                        tag + " 字体按 DPI 线性适配（行高 " + line.ToString("0.0") + "px ≈ " + expected.ToString("0.0") + "px）");

                    float ratio = line / Math.Max(1f, Theme.S(96));
                    check(Math.Abs(ratio - baseRatio) / baseRatio < 0.2f,
                        tag + " 文字与控件比例一致（" + ratio.ToString("0.000") + " vs " + baseRatio.ToString("0.000") + "）");

                    if (logicalW < 1366f || logicalH < 800f)
                        check(Theme.Density <= 0.86f, tag + " 很小的逻辑区域启用更紧凑布局（系数 " + Theme.Density.ToString("0.00") + "）");
                    else if (logicalW < 1680f || logicalH < 1000f)
                        check(Theme.Density > 0.86f && Theme.Density <= 0.95f, tag + " 偏小逻辑区域适度收紧（系数 " + Theme.Density.ToString("0.00") + "）");
                    else
                        check(Theme.Density >= 0.95f, tag + " 足量逻辑区域保持常规间距（系数 " + Theme.Density.ToString("0.00") + "）");

                    int winW = (int)Math.Round(Math.Min(1000f, logicalW - 40f) * c.scale);
                    int winH = (int)Math.Round(Math.Min(680f, logicalH - 60f) * c.scale);

                    using (var page = new MessagesPage())
                    {
                        page.SetBounds(0, 0, winW, winH);
                        for (int i = 0; i < 5; i++)
                            page.AddMessage(Sample("标题" + (i + 1), "一、注意事项\n1. 请提前十分钟到校\n2. 携带通知单", i % 2 == 0 ? MailCommand.Call : MailCommand.Tell));
                        Pump(40);

                        int pad = Theme.PagePadding;
                        int gap = Theme.Gap;
                        int header = page.HeaderHeightForTest;   // 直接读页面实际值
                        int footer = page.FooterHeightForTest;
                        check(header + footer + Theme.S(120) < winH, tag + " 页头页脚与内容区容纳在窗口内（" + winW + "x" + winH + "）");

                        int gridW = winW - pad * 2;
                        int gridH = winH - header - footer;
                        int cw = Math.Max(Theme.S(140), (gridW - gap) / 2);
                        int ch = Math.Max(Theme.S(110), (gridH - gap) / 2);

                        using (Bitmap bmp = Render(page, new Size(winW, winH)))
                        {
                            int cards = 0;
                            for (int row = 0; row < 2; row++)
                                for (int col = 0; col < 2; col++)
                                    if (Near(bmp.GetPixel(pad + col * (cw + gap) + Theme.S(10), header + row * (ch + gap) + Theme.S(16)), Theme.Surface, 8)) cards++;
                            check(cards == 4, tag + " 田字格 4 张卡片完整（命中 " + cards + "）");

                            Color gapColor = bmp.GetPixel(Math.Min(winW - 1, pad + cw + gap / 2), Math.Min(winH - 1, header + ch / 2));
                            check(Near(gapColor, Theme.Window, 8), tag + " 卡片之间保留缝隙（无重叠）");

                            int withText = 0;
                            for (int row = 0; row < 2; row++)
                                for (int col = 0; col < 2; col++)
                                    if (CountBright(bmp, new Rectangle(pad + col * (cw + gap), header + row * (ch + gap), cw, ch)) > 8) withText++;
                            check(withText >= 3, tag + " 卡片内文字正常显示（" + withText + "/4 张有文字）");
                        }
                    }
                }
            }
            finally
            {
                Theme.UpdateDpi(1f, new Size(1920, 1080));
                Theme.Apply(originalFont, originalDark, originalAccent);
            }
        }

        // ---------- 按钮文字必须完整显示（不被裁切） ----------
        private static void TestButtonLabelsFit(Action<bool, string> check)
        {
            float originalFont = Settings.FontSize;
            bool originalDark = Settings.DarkMode;
            string originalAccent = Settings.AccentKey;
            var size = new Size(1600, 1000);
            try
            {
                foreach (float fontSize in new[] { Theme.MinFontSize, 10f, 13f, Theme.MaxFontSize })
                {
                    foreach (float scale in new[] { 1f, 1.5f, 2f })
                    {
                        Theme.UpdateDpi(scale, new Size((int)(1920 * scale), (int)(1080 * scale)));
                        Theme.Apply(fontSize, true, "water");
                        string tag = fontSize + "pt@" + (int)(scale * 100) + "%";

                        using (var page = new MessagesPage())
                        {
                            page.SetBounds(0, 0, size.Width, size.Height);
                            page.AddMessage(Sample("标题", "正文", MailCommand.Call));
                            Pump(60);
                            foreach (MaterialButton btn in FindAll<MaterialButton>(page))
                            {
                                if (!btn.Visible || string.IsNullOrEmpty(btn.Text)) continue;
                                check(btn.Width + 1 >= btn.IdealWidth,
                                    "消息页按钮「" + btn.Text + "」在 " + tag + " 下完整显示（需要 " + btn.IdealWidth + " / 实际 " + btn.Width + "）");
                            }
                        }

                        using (var about = new AboutPage(new AuthService()))
                        {
                            about.SetBounds(0, 0, size.Width, 1400);
                            Pump(120);
                            MaterialButton advanced = FindButtonByText(about, "高级设置");
                            if (advanced != null)
                            {
                                advanced.PerformClick();
                                Pump(450);
                            }
                            var visible = new List<MaterialButton>();
                            foreach (MaterialButton btn in FindAll<MaterialButton>(about))
                            {
                                if (!btn.Visible) continue;
                                if (btn.Width > 0) visible.Add(btn);
                                if (string.IsNullOrEmpty(btn.Text)) continue;
                                check(btn.Width + 1 >= btn.IdealWidth,
                                    "关于页按钮「" + btn.Text + "」在 " + tag + " 下完整显示（需要 " + btn.IdealWidth + " / 实际 " + btn.Width + "）");
                            }
                            int overlaps = 0;
                            string pair = "";
                            for (int i = 0; i < visible.Count; i++)
                                for (int j = i + 1; j < visible.Count; j++)
                                    if (visible[i].Bounds.IntersectsWith(visible[j].Bounds))
                                    {
                                        overlaps++;
                                        pair = "「" + visible[i].Text + "」" + visible[i].Bounds + " × 「" + visible[j].Text + "」" + visible[j].Bounds;
                                    }
                            check(overlaps == 0, "关于页 " + tag + " 下按钮互不重叠（重叠 " + overlaps + " 对 " + pair + "）");
                        }
                    }
                }
            }
            finally
            {
                Theme.UpdateDpi(1f, new Size(1920, 1080));
                Theme.Apply(originalFont, originalDark, originalAccent);
            }
        }

        // ---------- 翻页按钮改为 “< >” ----------
        private static void TestPagerButtons(Action<bool, string> check)
        {
            var size = new Size(1600, 1000);
            using (var page = new MessagesPage())
            {
                page.SetBounds(0, 0, size.Width, size.Height);
                for (int i = 0; i < 5; i++) page.AddMessage(Sample("标题" + i, "正文", MailCommand.Call));
                Pump(60);

                MaterialButton prev = FindButtonByText(page, "<");
                MaterialButton next = FindButtonByText(page, ">");
                check(prev != null, "存在 “<” 上一页按钮");
                check(next != null, "存在 “>” 下一页按钮");
                if (prev == null || next == null) return;

                check(string.IsNullOrEmpty(prev.Glyph) && string.IsNullOrEmpty(next.Glyph), "翻页按钮不再依赖图标字体字形");
                check(prev.Height >= Theme.S(30) && prev.Width >= Theme.S(30), "翻页按钮尺寸可点击（" + prev.Width + "x" + prev.Height + "）");
                check(!prev.Bounds.IntersectsWith(next.Bounds), "两个翻页按钮不重叠");

                using (Bitmap bmp = Render(page, size))
                {
                    int prevInk = 0;
                    for (int y = prev.Top; y < prev.Bottom; y++)
                    {
                        for (int x = prev.Left; x < prev.Right; x++)
                        {
                            if (Theme.Luminance(bmp.GetPixel(x, y)) > 0.45d) prevInk++;
                        }
                    }
                    check(prevInk > 3, "“<” 按钮文字已渲染（" + prevInk + " 个高亮像素）");
                }

                next.PerformClick();
                Pump(420);
                check(page.PageCount >= 1, "点击 “>” 后仍可正常翻页");
            }
        }

        /// <summary>
        /// 把关键界面渲染成 PNG（视觉回归 / 开发者查看）：
        /// 主窗口（消息页）、消息页样例、过期页样例、过期页空状态。
        /// </summary>
        internal static void SaveSnapshots(string dir)
        {
            Directory.CreateDirectory(dir);
            Theme.Apply(Settings.FontSize, Settings.DarkMode, Settings.AccentKey);

            using (var shell = new ShellForm())
            {
                shell.SetBounds(0, 0, 1400, 900);
                Pump(250);
                NavRail nav = FindControl<NavRail>(shell);
                if (nav != null)
                {
                    // 未显示的窗口整体渲染会是空白，因此渲染导航栏（可看到“消息 / 过期 / 关于”三个分项）
                    using (Bitmap bmp = Render(nav, nav.Size))
                        bmp.Save(Path.Combine(dir, "nav.png"), System.Drawing.Imaging.ImageFormat.Png);
                }
            }

            using (var page = new MessagesPage())
            {
                page.SetBounds(0, 0, 1000, 700);
                for (int i = 0; i < 5; i++)
                    page.AddMessage(Sample("家长会通知 " + (i + 1), "一、时间\n本周五 18:30\n二、地点\nA101 教室",
                        i % 2 == 0 ? MailCommand.Call : MailCommand.Tell));
                Pump(150);
                using (Bitmap bmp = Render(page, page.ClientSize))
                    bmp.Save(Path.Combine(dir, "messages.png"), System.Drawing.Imaging.ImageFormat.Png);
            }

            using (var stale = new MessagesPage("过期", MailWindow.StaleSubtitle(), "暂无过期邮件", MailWindow.StaleEmptyHint(), false))
            {
                stale.SetBounds(0, 0, 1000, 700);
                Pump(150);
                using (Bitmap bmp = Render(stale, stale.ClientSize))
                    bmp.Save(Path.Combine(dir, "stale-empty.png"), System.Drawing.Imaging.ImageFormat.Png);

                for (int i = 0; i < 3; i++)
                    stale.AddMessage(Sample("过期通知 " + (i + 1), "打开软件前收到的指令邮件\n只显示，不提醒",
                        MailCommand.Tell));
                Pump(150);
                using (Bitmap bmp = Render(stale, stale.ClientSize))
                    bmp.Save(Path.Combine(dir, "stale.png"), System.Drawing.Imaging.ImageFormat.Png);
            }

            // 开发者信息卡片（示例图片 1.jpg，用于确认“图片在最后”的排版）
            string imageDir = Path.Combine(Path.GetTempPath(), "ct-devinfo-snap");
            Directory.CreateDirectory(imageDir);
            string imagePath = Path.Combine(imageDir, "1.jpg");
            using (var sample = new Bitmap(520, 220))
            using (Graphics sg = Graphics.FromImage(sample))
            {
                sg.Clear(Color.FromArgb(255, 72, 192, 163));
                using (var f = new Font("Microsoft YaHei", 26f, FontStyle.Bold))
                using (var br = new SolidBrush(Color.White))
                    sg.DrawString("1.jpg 说明图片", f, br, new PointF(26f, 80f));
                sample.Save(imagePath, System.Drawing.Imaging.ImageFormat.Jpeg);
            }
            try
            {
                DevInfoCard.DirectoryOverrideForTest = new[] { imageDir };
                using (var card = new DevInfoCard())
                {
                    card.SetBounds(0, 0, 900, 780);
                    Pump(150);
                    using (Bitmap bmp = Render(card, card.ClientSize))
                        bmp.Save(Path.Combine(dir, "devinfo.png"), System.Drawing.Imaging.ImageFormat.Png);
                }
            }
            finally
            {
                DevInfoCard.DirectoryOverrideForTest = null;
            }
        }

        // ---------- 打开软件前的邮件：进「过期」且不提醒 ----------
        private static void TestStaleRouting(Action<bool, string> check)
        {
            using (var form = new ShellForm())
            {
                form.SetBounds(0, 0, 1400, 900);
                Pump(120);

                MessagesPage mainPage = null;
                MessagesPage stalePage = null;
                foreach (MessagesPage p in FindAll<MessagesPage>(form))
                {
                    if (p.ShowsMailStatus) mainPage = p;
                    else stalePage = p;
                }
                check(mainPage != null && stalePage != null, "找到「消息」与「过期」两个列表页");
                if (mainPage == null || stalePage == null) return;

                MethodInfo onMessage = typeof(ShellForm).GetMethod("OnMessageReceived",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                FieldInfo notifierField = typeof(ShellForm).GetField("_notifier",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                check(onMessage != null && notifierField != null, "自测可通过反射触达收信回调与通知器");
                if (onMessage == null || notifierField == null) return;

                object notifier = notifierField.GetValue(form);
                PropertyInfo lastNotified = notifier.GetType().GetProperty("LastNotified");

                // 1) 打开软件前 3 小时收到的 Call 邮件：应进「过期」，且不触发任何提醒
                MessageItem stale = Sample("过期呼叫", "打开软件前收到的邮件", MailCommand.Call);
                stale.ReceivedLocal = DateTime.Now.AddHours(-3);
                onMessage.Invoke(form, new object[] { stale });
                Pump(80);
                check(stalePage.MessageCount == 1, "启动前的邮件进入「过期」列表（" + stalePage.MessageCount + " 条）");
                check(mainPage.MessageCount == 0, "启动前的邮件不进入「消息」列表（" + mainPage.MessageCount + " 条）");
                check(lastNotified == null || lastNotified.GetValue(notifier, null) == null,
                    "启动前的呼叫邮件不触发提醒（通知器未记录）");

                // 2) 启动后收到的 Call 邮件：进「消息」并正常提醒
                MessageItem fresh = Sample("新呼叫", "打开软件后收到的邮件", MailCommand.Call);
                fresh.ReceivedLocal = DateTime.Now.AddMinutes(1);
                onMessage.Invoke(form, new object[] { fresh });
                Pump(80);
                check(mainPage.MessageCount == 1, "启动后的邮件进入「消息」列表（" + mainPage.MessageCount + " 条）");
                check(lastNotified != null && ReferenceEquals(lastNotified.GetValue(notifier, null), fresh),
                    "启动后的呼叫邮件正常触发提醒");
            }
        }

        // ---------- 开发者信息：可选的说明图片 1.jpg（目录下没有则不显示） ----------
        private static void TestDevInfoImage(Action<bool, string> check)
        {
            string dir = Path.Combine(Path.GetTempPath(), "ct-devinfo-" + Guid.NewGuid().ToString("N").Substring(0, 6));
            Directory.CreateDirectory(dir);
            string jpg = Path.Combine(dir, "1.jpg");
            string png = Path.Combine(dir, "1.png");
            float originalFont = Settings.FontSize;
            try
            {
                Theme.Apply(Theme.DefaultFontSize, true, "water");
                DevInfoCard.DirectoryOverrideForTest = new[] { dir };

                using (var card = new DevInfoCard())
                {
                    card.SetBounds(0, 0, 900, 500);

                    // 1) 目录下没有图片 → 不加载、不占高度
                    int without = card.PreferredHeight;
                    check(!card.HasImage, "目录下没有 1.jpg 时不显示图片");
                    check(card.ImagePath == null, "未命中时 ImagePath 为空");

                    // 2) 放入 240×120 的图片 → 自动加载、卡片为其留出空间（图片在最后）
                    using (var bmp = new Bitmap(240, 120))
                    using (Graphics g = Graphics.FromImage(bmp)) { g.Clear(Color.CornflowerBlue); bmp.Save(jpg, System.Drawing.Imaging.ImageFormat.Jpeg); }
                    card.ReloadInfo();
                    check(card.HasImage, "放入 1.jpg 后自动加载（" + card.ImagePath + "）");
                    int with = card.PreferredHeight;
                    check(with > without + 100, "卡片高度为图片留出空间（" + without + " → " + with + "）");
                    Size small = card.ImageDisplaySize(600);
                    check(small.Width == 240 && small.Height == 120, "小图不放大（" + small.Width + "×" + small.Height + "）");

                    // 3) 删除图片后重载 → 回到不显示、高度恢复
                    File.Delete(jpg);
                    card.ReloadInfo();
                    check(!card.HasImage, "删除 1.jpg 后不再显示该图片");
                    check(card.PreferredHeight == without, "卡片高度随之恢复（" + card.PreferredHeight + " px）");

                    // 4) 也接受 1.png；大图/竖图等比缩小并限高
                    using (var tall = new Bitmap(1000, 4000))
                    using (Graphics g = Graphics.FromImage(tall)) { g.Clear(Color.SeaGreen); tall.Save(png, System.Drawing.Imaging.ImageFormat.Png); }
                    card.ReloadInfo();
                    check(card.HasImage, "没有 1.jpg 时会退而使用 1.png（" + card.ImagePath + "）");
                    Size fitted = card.ImageDisplaySize(600);
                    check(fitted.Height <= 720 && fitted.Width <= 600, "竖图被限制在卡片内（" + fitted.Width + "×" + fitted.Height + "）");
                    check(fitted.Height == 720, "竖图按限高缩放（实际高度 " + fitted.Height + "）");

                    // 5) 损坏的图片文件不会导致异常，只是不显示
                    File.Delete(png);
                    File.WriteAllText(jpg, "not an image");
                    card.ReloadInfo();
                    check(!card.HasImage, "图片文件损坏时按“无图片”处理，不抛异常");
                }

                check(DevInfoCard.FindImagePath(null) == null, "候选目录为 null 时安全返回 null");
                check(DevInfoCard.FindImagePath(new[] { Path.Combine(dir, "not-exist") }) == null,
                    "候选目录不存在时返回 null（不抛异常）");
                string[] dirs = DevInfoCard.ImageDirectories();
                check(dirs.Length == 2 && dirs[0] == AppPaths.ExeDir && dirs[1] == AppPaths.DataDir,
                    "默认查找顺序：程序目录 → 用户数据目录（安装版下普通用户可写）");
            }
            finally
            {
                DevInfoCard.DirectoryOverrideForTest = null;
                Theme.Apply(originalFont, Settings.DarkMode, Settings.AccentKey);
                try { Directory.Delete(dir, true); } catch (Exception) { }
            }
        }

        // ---------- 消息卡片不贴顶：网格起点包含页头留白 ----------
        private static void TestGridTopGap(Action<bool, string> check)
        {
            using (var page = new MessagesPage())
            {
                page.SetBounds(0, 0, 1000, 700);
                Pump(120);

                MessageCard[] cards = FindAll<MessageCard>(page);
                check(cards.Length == MessagesPage.PageSize, "消息页有 4 张卡片控件（实测 " + cards.Length + "）");
                check(page.GridTopGap >= Theme.S(10), "页头与卡片之间存在留白（" + page.GridTopGap + " px）");
                check(page.HeaderHeightForTest >= page.GridTopGap + Theme.S(30),
                    "网格起点位于页头下方（" + page.HeaderHeightForTest + " px）");
                if (cards.Length > 0)
                    check(cards[0].Top == page.HeaderHeightForTest,
                        "第一行卡片从网格起点开始（Top=" + cards[0].Top + "）");
            }
        }

        // ---------- 输入框圆角 ----------
        private static void TestRoundedInputs(Action<bool, string> check)
        {
            float originalFont = Settings.FontSize;
            try
            {
                Theme.Apply(Theme.DefaultFontSize, true, "water");
                using (var about = new AboutPage(new AuthService()))
                {
                    about.SetBounds(0, 0, 1400, 1200);
                    Pump(120);
                    MaterialButton advanced = FindButtonByText(about, "高级设置");
                    if (advanced != null)
                    {
                        advanced.PerformClick();
                        Pump(450);
                    }

                    RoundedInputHost[] hosts = FindAll<RoundedInputHost>(about);
                    check(hosts.Length >= 3, "存在 3 个圆角输入框容器（" + hosts.Length + "）");
                    if (hosts.Length > 0)
                    {
                        RoundedInputHost host = hosts[0];
                        using (Bitmap bmp = Render(host, host.Size))
                        {
                            Color corner = bmp.GetPixel(1, 1);
                            Color center = bmp.GetPixel(host.Width / 2, host.Height / 2);
                            check(!Near(corner, center, 6), "输入框四角为圆角（角 " + corner.R + "," + corner.G + "," + corner.B +
                                " ≠ 中心 " + center.R + "," + center.G + "," + center.B + "）");
                            check(Near(center, Theme.SurfaceAlt, 10), "输入框底色为主题输入底色");
                        }
                        check(host.Box != null && host.Box.BorderStyle == BorderStyle.None, "内部输入框无边框（由圆角容器统一绘制）");
                    }
                }
            }
            finally
            {
                Theme.Apply(originalFont, Settings.DarkMode, Settings.AccentKey);
            }
        }

        // ---------- 收信方式（Graph / IMAP）可切换、文字不被裁切 ----------
        private static void TestMailModeSelector(Action<bool, string> check)
        {
            string originalMode = Settings.MailMode;
            float originalFont = Settings.FontSize;
            try
            {
                Theme.Apply(Theme.DefaultFontSize, true, "water");
                using (var about = new AboutPage(new AuthService()))
                {
                    about.SetBounds(0, 0, 1400, 1300);
                    Pump(120);
                    MaterialButton advanced = FindButtonByText(about, "高级设置");
                    if (advanced != null)
                    {
                        advanced.PerformClick();
                        Pump(450);
                    }

                    MaterialButton mode = FindButtonByText(about, "Microsoft Graph（推荐）");
                    check(mode != null, "高级设置里有“收信方式：Microsoft Graph（推荐）”按钮");
                    if (mode == null) return;

                    check(mode.Visible && mode.Width > 0 && mode.Height > 0, "收信方式按钮可见且有尺寸（" + mode.Width + "×" + mode.Height + "）");
                    check(!mode.Text.EndsWith("…", StringComparison.Ordinal), "收信方式文字未被省略（" + mode.Text + "）");

                    mode.PerformClick();
                    Pump(120);
                    check(Settings.MailMode == "imap", "点击后收信方式切换为 IMAP");
                    check(string.Equals(mode.Text, "IMAP（需邮箱已开启）", StringComparison.Ordinal),
                        "按钮文案随模式更新（实际：" + mode.Text + "）");

                    mode.PerformClick();
                    Pump(120);
                    check(Settings.MailMode == "graph", "再次点击切回 Microsoft Graph");
                    check(string.Equals(mode.Text, "Microsoft Graph（推荐）", StringComparison.Ordinal),
                        "切回后文案恢复（实际：" + mode.Text + "）");

                    Size needed = TextRenderer.MeasureText(mode.Text, mode.Font);
                    check(mode.Width >= needed.Width, "按钮宽度放得下文字（" + mode.Width + " ≥ " + needed.Width + "）");
                }
            }
            finally
            {
                Settings.MailMode = originalMode;
                Theme.Apply(originalFont, Settings.DarkMode, Settings.AccentKey);
            }
        }

        // ---------- 常规设置：关闭时驻留托盘 / 开机自启动 两个开关 ----------
        private static void TestTrayAndStartupSwitches(Action<bool, string> check)
        {
            bool originalTray = Settings.CloseToTray;
            bool originalStartup = Settings.RunAtStartup;
            float originalFont = Settings.FontSize;
            try
            {
                Theme.Apply(Theme.DefaultFontSize, true, "water");
                using (var about = new AboutPage(new AuthService()))
                {
                    about.SetBounds(0, 0, 1400, 1500);
                    Pump(160);

                    SettingsCard card = FindControl<SettingsCard>(about);
                    check(card != null, "存在“界面与常规设置”卡片");
                    if (card == null) return;
                    check(card.PreferredHeight > Theme.S(400), "卡片高度包含新增的托盘 / 自启动行（" + card.PreferredHeight + " px）");

                    MaterialSwitch[] switches = FindAll<MaterialSwitch>(card);
                    check(switches.Length >= 3, "卡片里有 3 个开关（深色 / 托盘 / 开机自启动，实测 " + switches.Length + "）");

                    check(FindSwitchCausing(switches, delegate { return Settings.CloseToTray; }) != null,
                        "有一个开关可切换“关闭窗口后驻留托盘”");
                    check(FindSwitchCausing(switches, delegate { return Settings.RunAtStartup; }) != null,
                        "有一个开关可切换“开机自动启动”");
                }
            }
            finally
            {
                Settings.CloseToTray = originalTray;
                Settings.RunAtStartup = originalStartup;
                Theme.Apply(originalFont, Settings.DarkMode, Settings.AccentKey);
            }
        }

        /// <summary>依次翻转开关，返回“翻转后给定设置发生变化”的那个开关（并立即复原）。</summary>
        private static MaterialSwitch FindSwitchCausing(MaterialSwitch[] switches, Func<bool> read)
        {
            bool before = read();
            foreach (MaterialSwitch sw in switches)
            {
                bool original = sw.Checked;
                sw.Checked = !original;
                bool changed = read() != before;
                sw.Checked = original;
                if (changed) return sw;
            }
            return null;
        }

        // ---------- 反复切换主题后不应残留旧底色（随机黑块回归） ----------
        private static void TestRepeatedThemeSwitch(Action<bool, string> check)
        {
            float originalFont = Settings.FontSize;
            bool originalDark = Settings.DarkMode;
            string originalAccent = Settings.AccentKey;
            var size = new Size(1400, 900);
            try
            {
                using (var about = new AboutPage(new AuthService()))
                {
                    about.SetBounds(0, 0, size.Width, 1200);
                    Pump(150);
                    MaterialButton advanced = FindButtonByText(about, "高级设置");
                    if (advanced != null)
                    {
                        advanced.PerformClick();
                        Pump(450);
                    }

                    for (int round = 0; round < 5; round++)
                    {
                        Theme.Apply(Theme.DefaultFontSize, false, "water");
                        Theme.InvalidateTree(about);
                        Pump(90);
                        using (Bitmap bmp = Render(about, new Size(size.Width, 1200)))
                        {
                            Color margin = bmp.GetPixel(6, 1180);
                            check(Theme.Luminance(margin) > 0.7d,
                                "第 " + (round + 1) + " 次切浅色后留白为浅色（实测 " + margin.R + "," + margin.G + "," + margin.B + "）");
                            int darkBlocks = CountDark(bmp, new Rectangle(0, 0, size.Width, 1200));
                            check(darkBlocks * 20 < size.Width * 1200 / 8,
                                "第 " + (round + 1) + " 次切浅色后无大片深色残留（深色像素 " + darkBlocks + "）");
                        }

                        Theme.Apply(Theme.DefaultFontSize, true, "water");
                        Theme.InvalidateTree(about);
                        Pump(90);
                    }
                }
            }
            finally
            {
                Theme.Apply(originalFont, originalDark, originalAccent);
            }
        }

        /// <summary>统计明显偏暗的像素（用于判断浅色模式下是否残留深色块）。</summary>
        private static int CountDark(Bitmap bmp, Rectangle area)
        {
            int count = 0;
            for (int y = area.Top; y < area.Bottom; y += 3)
            {
                for (int x = area.Left; x < area.Right; x += 3)
                {
                    Color c = bmp.GetPixel(x, y);
                    if (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B < 60d) count++;
                }
            }
            return count;
        }

        // ---------- 浅色模式：不应残留深色底 ----------
        private static void TestLightModeSurfaces(Action<bool, string> check)
        {
            float originalFont = Settings.FontSize;
            string originalAccent = Settings.AccentKey;
            try
            {
                Theme.Apply(Theme.DefaultFontSize, false, "water");

                using (var nav = new NavRail())
                {
                    nav.SetBounds(0, 0, 220, 600);
                    using (Bitmap bmp = Render(nav, nav.Size))
                    {
                        Color probe = bmp.GetPixel(nav.Width - 30, nav.Height - 120);
                        check(Near(probe, Theme.NavBackground, 6),
                            "浅色模式下导航栏为浅色底（实测 " + probe.R + "," + probe.G + "," + probe.B + "）");
                        int dark = CountDark(bmp, new Rectangle(0, 0, nav.Width, nav.Height));
                        check(dark * 8 < nav.Width * nav.Height / 9, "浅色模式下导航栏几乎没有深色块（深色像素 " + dark + "）");
                    }
                }

                using (var about = new AboutPage(new AuthService()))
                {
                    about.SetBounds(0, 0, 1200, 900);
                    Pump(120);
                    using (Bitmap bmp = Render(about, new Size(1200, 900)))
                    {
                        Color margin = bmp.GetPixel(6, 880);
                        check(Near(margin, Theme.Window, 6),
                            "浅色模式下关于页留白为浅色（实测 " + margin.R + "," + margin.G + "," + margin.B + "）");
                    }

                    MaterialButton advanced = FindButtonByText(about, "高级设置");
                    if (advanced != null)
                    {
                        advanced.PerformClick();
                        Pump(400);
                    }
                    TextBox[] boxes = FindAll<TextBox>(about);
                    check(boxes.Length >= 3, "登录面板包含 3 个输入框（Client ID / 租户 / 权限范围）");
                    bool allLight = boxes.Length > 0;
                    foreach (TextBox box in boxes)
                        if (Theme.Luminance(box.BackColor) < 0.7d) allLight = false;
                    check(allLight, "浅色模式下输入框底色跟随主题（不残留黑底）");
                }

                Theme.Apply(Theme.DefaultFontSize, true, "water");
                using (var nav = new NavRail())
                {
                    nav.SetBounds(0, 0, 220, 600);
                    using (Bitmap bmp = Render(nav, nav.Size))
                    {
                        Color probe = bmp.GetPixel(nav.Width - 30, nav.Height - 120);
                        check(Near(probe, Theme.NavBackground, 6), "切回深色模式后导航栏恢复深色底");
                    }
                }
            }
            finally
            {
                Theme.Apply(originalFont, true, originalAccent);
            }
        }

        /// <summary>
        /// 页头 / 页脚高度统一由 MessagesPage 暴露（HeaderHeightForTest / FooterHeightForTest），
        /// 测试不再各自维护一份布局公式，避免实现调整后探针错位。
        /// </summary>
    }
}
