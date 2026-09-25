using System;
using System.Net;
using System.Text.RegularExpressions;

namespace ClassTell
{
    /// <summary>把 HTML 邮件正文转成纯文本（保留段落换行）。</summary>
    internal static class HtmlText
    {
        private const RegexOptions Opts = RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled;

        private static readonly Regex ScriptStyle = new Regex("<(script|style)[^>]*>.*?</\\1\\s*>", Opts);
        private static readonly Regex BreakTags = new Regex("<\\s*br\\s*/?\\s*>", Opts);
        private static readonly Regex BlockClose = new Regex("<\\s*/\\s*(p|div|tr|li|ul|ol|h[1-6]|table|blockquote)\\s*>", Opts);
        private static readonly Regex BlockOpen = new Regex("<\\s*(p|div|tr|li|ul|ol|h[1-6]|table|blockquote)(\\s[^>]*)?>", Opts);
        private static readonly Regex AnyTag = new Regex("<[^>]*>", Opts);
        private static readonly Regex ManyNewlines = new Regex("\n{3,}", RegexOptions.Compiled);

        public static string ToPlainText(string html)
        {
            if (string.IsNullOrEmpty(html)) return string.Empty;
            string s = ScriptStyle.Replace(html, " ");
            s = BreakTags.Replace(s, "\n");
            s = BlockClose.Replace(s, "\n");
            s = BlockOpen.Replace(s, "\n");
            s = AnyTag.Replace(s, string.Empty);
            try { s = WebUtility.HtmlDecode(s); }
            catch (Exception) { }
            s = s.Replace('\u00A0', ' ').Replace('\u200B', ' ');
            s = ManyNewlines.Replace(s, "\n\n");
            return Typography.NormalizeNewlines(s);
        }
    }
}
