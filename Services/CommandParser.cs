using System;
using System.Collections.Generic;
using System.Linq;
using MimeKit;

namespace ClassTell
{
    /// <summary>
    /// 解析邮件格式：
    /// 标题 = 命令字（C / call / T / tell）
    /// 正文第一行 = 标题 BBBB
    /// 第一行之后的内容 = 正文 CCCC（完整保留换行）
    /// </summary>
    internal static class CommandParser
    {
        /// <summary>标题里的命令字（忽略大小写与首尾空白）。</summary>
        public static MailCommand ParseCommand(string subject)
        {
            if (string.IsNullOrEmpty(subject)) return MailCommand.Unknown;
            string s = subject.Trim().Trim('[', ']', '<', '>').Trim().ToUpperInvariant();
            if (s == "C" || s == "CALL") return MailCommand.Call;
            if (s == "T" || s == "TELL") return MailCommand.Tell;
            return MailCommand.Unknown;
        }

        /// <summary>从纯文本正文里拆出第一行（标题）与其余内容（正文）。</summary>
        public static void SplitTitleBody(string bodyText, out string title, out string body)
        {
            title = string.Empty;
            body = string.Empty;
            if (string.IsNullOrEmpty(bodyText)) return;

            string normalized = bodyText.Replace("\r\n", "\n").Replace('\r', '\n');
            string[] lines = normalized.Split('\n');

            int titleIndex = -1;
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Trim().Length > 0) { titleIndex = i; break; }
            }
            if (titleIndex < 0) return;

            title = StripTitleLabel(lines[titleIndex].Trim());

            var rest = new List<string>();
            for (int i = titleIndex + 1; i < lines.Length; i++)
                rest.Add(lines[i].TrimEnd());
            body = Typography.NormalizeNewlines(string.Join("\n", rest));
        }

        private static string StripTitleLabel(string line)
        {
            string[] labels = { "标题:", "标题：", "标题 :", "标题 ：", "标题", "title:", "title：", "subject:", "subject：" };
            foreach (string label in labels)
            {
                if (line.StartsWith(label, StringComparison.OrdinalIgnoreCase))
                {
                    string rest = line.Substring(label.Length).TrimStart(' ', ':', '：', '-', '—', '\t');
                    if (rest.Length > 0) return rest.Trim();
                }
            }
            return line;
        }

        /// <summary>取邮件纯文本正文（没有纯文本时用 HTML 转换）。</summary>
        public static string GetBodyText(MimeMessage message)
        {
            if (message == null) return string.Empty;
            string text = null;
            try { text = message.TextBody; }
            catch (Exception) { }
            if (!string.IsNullOrEmpty(text) && text.Trim().Length > 0) return text;

            string html = null;
            try { html = message.HtmlBody; }
            catch (Exception) { }
            if (!string.IsNullOrEmpty(html)) return HtmlText.ToPlainText(html);

            return string.Empty;
        }

        /// <summary>解析成消息对象；返回 null 表示这封邮件不是 ClassTell 指令。</summary>
        public static MessageItem Parse(MimeMessage message, uint uid, out string skipReason)
        {
            skipReason = null;
            if (message == null) { skipReason = "邮件为空"; return null; }

            MailCommand command = ParseCommand(message.Subject);
            if (command == MailCommand.Unknown)
            {
                skipReason = "标题不是 C/call/T/tell：" + (message.Subject ?? "(无标题)");
                return null;
            }

            string title, body;
            SplitTitleBody(GetBodyText(message), out title, out body);
            if (title.Length == 0) title = (message.Subject ?? string.Empty).Trim();

            var box = message.From == null ? null : message.From.Mailboxes.FirstOrDefault();
            var item = new MessageItem
            {
                Uid = uid,
                Command = command,
                Title = title,
                Body = body,
                Source = box == null ? string.Empty : (box.Address ?? string.Empty),
                SenderName = box == null ? string.Empty : (box.Name ?? string.Empty),
                ReceivedLocal = message.Date == DateTimeOffset.MinValue
                    ? DateTime.Now
                    : message.Date.ToLocalTime().DateTime
            };
            return item;
        }
    }
}
