using System;

namespace ClassTell
{
    /// <summary>邮件标题里指定的命令字（AAAA）。</summary>
    internal enum MailCommand
    {
        Unknown = 0,
        /// <summary>C / call：系统通知 + 消息界面显示。</summary>
        Call = 1,
        /// <summary>T / tell：仅消息界面显示。</summary>
        Tell = 2
    }

    /// <summary>一条已解析的邮件消息（标题 BBBB + 正文 CCCC + 来源）。</summary>
    internal sealed class MessageItem
    {
        public uint Uid { get; set; }
        public MailCommand Command { get; set; }
        public string Title { get; set; }
        public string Body { get; set; }
        public string Source { get; set; }
        public string SenderName { get; set; }
        public DateTime ReceivedLocal { get; set; }

        public bool IsCall { get { return Command == MailCommand.Call; } }

        /// <summary>命令字原文（用于界面角标）。</summary>
        public string CommandText { get { return IsCall ? "C / call" : "T / tell"; } }

        /// <summary>通知里显示的简短来源。</summary>
        public string SourceText
        {
            get
            {
                if (!string.IsNullOrEmpty(SenderName)) return SenderName + " <" + Source + ">";
                return Source;
            }
        }

        public string ReceivedText
        {
            get { return ReceivedLocal.ToString("MM-dd HH:mm"); }
        }

        public MessageItem()
        {
            Title = string.Empty;
            Body = string.Empty;
            Source = string.Empty;
            SenderName = string.Empty;
            ReceivedLocal = DateTime.Now;
        }
    }
}
