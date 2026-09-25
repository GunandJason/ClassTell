using System;

namespace ClassTell
{
    /// <summary>文本排版格式与工具（微软雅黑 + 保留换行）。</summary>
    internal static class Typography
    {
        public static readonly System.Drawing.StringFormat Wrap = new System.Drawing.StringFormat
        {
            Trimming = System.Drawing.StringTrimming.None,
            FormatFlags = System.Drawing.StringFormatFlags.LineLimit | System.Drawing.StringFormatFlags.NoClip
        };

        public static readonly System.Drawing.StringFormat WrapCenter = new System.Drawing.StringFormat
        {
            Trimming = System.Drawing.StringTrimming.None,
            FormatFlags = System.Drawing.StringFormatFlags.LineLimit | System.Drawing.StringFormatFlags.NoClip,
            Alignment = System.Drawing.StringAlignment.Center,
            LineAlignment = System.Drawing.StringAlignment.Center
        };

        public static readonly System.Drawing.StringFormat SingleLine = new System.Drawing.StringFormat
        {
            Trimming = System.Drawing.StringTrimming.EllipsisCharacter,
            FormatFlags = System.Drawing.StringFormatFlags.NoWrap
        };

        public static readonly System.Drawing.StringFormat SingleLineCenter = new System.Drawing.StringFormat
        {
            Trimming = System.Drawing.StringTrimming.EllipsisCharacter,
            FormatFlags = System.Drawing.StringFormatFlags.NoWrap,
            Alignment = System.Drawing.StringAlignment.Center,
            LineAlignment = System.Drawing.StringAlignment.Center
        };

        /// <summary>单行 + 左对齐 + 垂直居中（用于标签与输入框对齐）。</summary>
        public static readonly System.Drawing.StringFormat SingleLineMiddle = new System.Drawing.StringFormat
        {
            Trimming = System.Drawing.StringTrimming.EllipsisCharacter,
            FormatFlags = System.Drawing.StringFormatFlags.NoWrap,
            Alignment = System.Drawing.StringAlignment.Near,
            LineAlignment = System.Drawing.StringAlignment.Center
        };

        /// <summary>单行 + 右对齐 + 垂直居中。</summary>
        public static readonly System.Drawing.StringFormat SingleLineMiddleFar = new System.Drawing.StringFormat
        {
            Trimming = System.Drawing.StringTrimming.EllipsisCharacter,
            FormatFlags = System.Drawing.StringFormatFlags.NoWrap,
            Alignment = System.Drawing.StringAlignment.Far,
            LineAlignment = System.Drawing.StringAlignment.Center
        };

        /// <summary>多行 + 末尾省略号（用于消息卡片预览，超出部分裁剪）。</summary>
        public static readonly System.Drawing.StringFormat WrapEllipsis = new System.Drawing.StringFormat
        {
            Trimming = System.Drawing.StringTrimming.EllipsisCharacter,
            FormatFlags = System.Drawing.StringFormatFlags.LineLimit
        };

        /// <summary>统一换行符并按行拆分（保留空行）。</summary>
        public static string[] SplitLines(string text)
        {
            if (string.IsNullOrEmpty(text)) return new string[0];
            return NormalizeNewlines(text).Split('\n');
        }

        /// <summary>统一换行符 + 去除首尾空白（内部换行与空格全部保留）。</summary>
        public static string NormalizeNewlines(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            string s = text.Replace("\r\n", "\n").Replace('\r', '\n');
            int start = 0;
            int end = s.Length - 1;
            while (start < s.Length && (s[start] == '\n' || s[start] == ' ' || s[start] == '\t')) start++;
            while (end >= start && (s[end] == '\n' || s[end] == ' ' || s[end] == '\t')) end--;
            if (end < start) return string.Empty;
            return s.Substring(start, end - start + 1);
        }

        /// <summary>截断文本并加省略号（保留换行，长度按字符数计算）。</summary>
        public static string Truncate(string text, int maxChars)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            string s = NormalizeNewlines(text);
            if (s.Length <= maxChars) return s;
            return s.Substring(0, Math.Max(1, maxChars)).TrimEnd() + "…";
        }
    }
}
