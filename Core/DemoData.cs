using System;
using System.Collections.Generic;

namespace ClassTell
{
    /// <summary>演示数据：`--demo` 启动时注入若干示例消息，用于在没有邮箱的情况下预览界面与通知效果。</summary>
    internal static class DemoData
    {
        public static List<MessageItem> CreateMessages()
        {
            var list = new List<MessageItem>();
            list.Add(New(1u, MailCommand.Call, "家长会通知",
                "本周五 18:30 在本班教室召开家长会。\n会议内容：\n1. 期中成绩分析\n2. 下阶段学习安排\n3. 家校配合事项\n请家长准时参加，如有特殊情况请提前告知班主任。",
                "teacher@contoso.com", "王老师", -3));
            list.Add(New(2u, MailCommand.Tell, "作业清单",
                "语文：背诵第 12 课，家长签字。\n数学：练习册 P22-P24。\n英语：抄写单词 3 遍。",
                "teacher@contoso.com", "王老师", -25));
            list.Add(New(3u, MailCommand.Call, "临时调课",
                "明天上午第三节由体育课调整为数学课，请同学们带好课本。",
                "office@contoso.com", "教务处", -70));
            list.Add(New(4u, MailCommand.Tell, "值日安排",
                "本周值日：\n周一 张三\n周二 李四\n周三 王五\n周四 赵六\n周五 全班大扫除",
                "office@contoso.com", "教务处", -140));
            list.Add(New(5u, MailCommand.Call, "运动会报名",
                "校运动会将于下月举行，报名截止本周五。\n项目：60 米、跳绳、立定跳远、4×100 米接力。\n请有意参加的同学在班群接龙报名。",
                "pe@contoso.com", "体育组", -200));
            return list;
        }

        private static MessageItem New(uint uid, MailCommand command, string title, string body, string source, string sender, int minutesAgo)
        {
            return new MessageItem
            {
                Uid = uid,
                Command = command,
                Title = title,
                Body = body,
                Source = source,
                SenderName = sender,
                ReceivedLocal = DateTime.Now.AddMinutes(minutesAgo)
            };
        }
    }
}
