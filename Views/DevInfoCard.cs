using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace ClassTell
{
    /// <summary>读取同目录下的 info.txt（不存在时按约定显示 Err:NotExist）。</summary>
    internal static class InfoText
    {
        public const string Missing = "Err:NotExist";

        public static string Read()
        {
            try
            {
                string path = AppPaths.InfoFile;
                if (!File.Exists(path)) return Missing;
                string text = File.ReadAllText(path, Encoding.UTF8);
                text = Typography.NormalizeNewlines(text);
                return text.Length == 0 ? Missing : text;
            }
            catch (Exception ex)
            {
                AppLog.Exception_("读取 info.txt 失败", ex);
                return Missing;
            }
        }

        public static bool Exists
        {
            get
            {
                try { return File.Exists(AppPaths.InfoFile); }
                catch (Exception) { return false; }
            }
        }
    }

    /// <summary>“开发者信息”卡片：版本号 + 同目录 info.txt 的说明文本（微软雅黑，行高按字号推导）。</summary>
    internal sealed class DevInfoCard : CardPanel
    {
        private string _info = string.Empty;

        public DevInfoCard()
        {
            CardTitle = "开发者信息";
            // 除版本号外不再显示任何内置开发信息，正文完全来自同目录 info.txt
            CardSubtitle = null;
            ReloadInfo();
        }

        public string InfoBody { get { return _info; } }

        public void ReloadInfo()
        {
            _info = InfoText.Read();
            Invalidate();
        }

        /// <summary>卡片高度：只有版本行 + info.txt 内容，全部按字体度量推导。</summary>
        public int PreferredHeight
        {
            get
            {
                Font infoFont = Theme.Font(-0.3f, FontStyle.Regular);
                float lineHeight = Math.Max(1f, Theme.LineHeight(infoFont));
                int width = Math.Max(Theme.S(120), Width - Theme.S(72));
                SizeF size = Theme.MeasureText(_info, infoFont, width);
                int lines = Math.Max(1, (int)Math.Ceiling(size.Height / lineHeight));
                return Theme.S(74) + (int)Math.Ceiling(Theme.LineHeight(Theme.Font(1f, FontStyle.Bold))) + Theme.S(12)
                     + (int)Math.Ceiling(lines * lineHeight) + Theme.S(26);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            Gfx.Smooth(g);

            float pad = Theme.S(26);
            var r = new RectangleF(Theme.S(10), Theme.S(8), Width - Theme.S(20), Height - Theme.S(16));
            float y = r.Y + Theme.S(74);

            Font versionFont = Theme.Font(1f, FontStyle.Bold);
            float versionLine = Theme.LineHeight(versionFont);
            Gfx.DrawText(g, "版本：" + Theme.VersionText, versionFont, Theme.TextPrimary,
                new RectangleF(r.X + pad, y, Math.Max(Theme.S(80), r.Width - pad * 2f), versionLine), Typography.SingleLine);
            y += versionLine + Theme.S(12);

            // 描述内容来自同目录 info.txt（缺失时为 Err:NotExist）
            Font infoFont = Theme.Font(-0.3f, FontStyle.Regular);
            bool missing = string.Equals(_info, InfoText.Missing, StringComparison.Ordinal);
            Gfx.DrawText(g, _info, infoFont, missing ? Theme.Error : Theme.TextPrimary,
                new RectangleF(r.X + pad, y, Math.Max(Theme.S(60), r.Width - pad * 2f), Math.Max(Theme.SF(24f), r.Bottom - Theme.S(16) - y)),
                Typography.Wrap);
        }
    }
}
