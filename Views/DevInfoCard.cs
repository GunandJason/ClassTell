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

    /// <summary>
    /// “开发者信息”卡片：版本号 + info.txt 说明文本 +（可选）说明图片。
    /// 图片规则：在**程序目录**或**用户数据目录**（%APPDATA%\ClassTell，安装版下普通用户可写）
    /// 查找 1.jpg（也接受 1.jpeg / 1.png）；找不到就不显示，卡片高度也随之收缩。
    /// </summary>
    internal sealed class DevInfoCard : CardPanel
    {
        private static readonly string[] ImageNames = { "1.jpg", "1.jpeg", "1.png" };

        /// <summary>自测用：把搜索目录替换为指定目录（正常运行时为 null）。</summary>
        internal static string[] DirectoryOverrideForTest;

        private string _info = string.Empty;
        private Bitmap _image;
        private string _imagePath;

        public DevInfoCard()
        {
            CardTitle = "开发者信息";
            // 除版本号外不再显示任何内置开发信息，正文完全来自同目录 info.txt
            CardSubtitle = null;
            ReloadInfo();
        }

        public string InfoBody { get { return _info; } }

        /// <summary>是否已加载到说明图片。</summary>
        internal bool HasImage { get { return _image != null; } }

        /// <summary>命中的图片路径（未加载时为 null）。</summary>
        internal string ImagePath { get { return _imagePath; } }

        public void ReloadInfo()
        {
            _info = InfoText.Read();
            if (_image != null)
            {
                _image.Dispose();
                _image = null;
            }

            _imagePath = FindImagePath(DirectoryOverrideForTest ?? ImageDirectories());
            _image = LoadImage(_imagePath);
            if (_image != null)
            {
                AppLog.Info("开发者信息已加载说明图片：" + _imagePath + "（" + _image.Width + "×" + _image.Height + "）");
            }
            else if (DirectoryOverrideForTest == null)
            {
                // 没找到也记录下来，便于用户按日志核对该把 1.jpg 放在哪里
                AppLog.Info("开发者信息未找到说明图片，已查找：" +
                            string.Join(" | ", ImageDirectories()) + "（文件名 1.jpg / 1.jpeg / 1.png）");
            }
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _image != null)
            {
                _image.Dispose();
                _image = null;
            }
            base.Dispose(disposing);
        }

        /// <summary>图片候选目录：程序目录（绿色版）→ 用户数据目录（安装版下普通用户可写）。</summary>
        internal static string[] ImageDirectories()
        {
            return new[] { AppPaths.ExeDir, AppPaths.DataDir };
        }

        /// <summary>在候选目录里查找说明图片，返回首个命中的完整路径（都没有则返回 null）。</summary>
        internal static string FindImagePath(string[] directories)
        {
            if (directories == null) return null;
            foreach (string dir in directories)
            {
                if (string.IsNullOrEmpty(dir)) continue;
                foreach (string name in ImageNames)
                {
                    try
                    {
                        string path = Path.Combine(dir, name);
                        if (File.Exists(path)) return path;
                    }
                    catch (Exception) { }
                }
            }
            return null;
        }

        /// <summary>把图片读进内存（不留文件句柄，用户可以随时替换/删除该文件）；过大时先等比缩小。</summary>
        internal static Bitmap LoadImage(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            try
            {
                using (var source = Image.FromFile(path))
                {
                    const int maxSide = 2000;   // 只保留展示需要的分辨率，避免大图占用过多内存
                    double scale = Math.Min(1d, Math.Min((double)maxSide / source.Width, (double)maxSide / source.Height));
                    int w = Math.Max(1, (int)Math.Round(source.Width * scale));
                    int h = Math.Max(1, (int)Math.Round(source.Height * scale));

                    var copy = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    using (Graphics g = Graphics.FromImage(copy))
                    {
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        g.DrawImage(source, 0, 0, w, h);
                    }
                    return copy;
                }
            }
            catch (Exception ex)
            {
                AppLog.Exception_("读取说明图片失败（已忽略）：" + path, ex);
                return null;
            }
        }

        /// <summary>说明图片的展示尺寸：按卡片宽度等比缩放（不放大、竖图限高，避免卡片过长）。</summary>
        internal Size ImageDisplaySize(int availableWidth)
        {
            if (_image == null) return Size.Empty;
            int maxW = Math.Max(Theme.S(80), availableWidth);
            int maxH = (int)(maxW * 1.2);   // 竖图也不至于太高
            double scale = Math.Min((double)maxW / _image.Width, (double)maxH / _image.Height);
            if (scale > 1d) scale = 1d;
            return new Size(Math.Max(1, (int)Math.Round(_image.Width * scale)),
                            Math.Max(1, (int)Math.Round(_image.Height * scale)));
        }

        /// <summary>说明图片占用的额外高度（未加载图片时为 0）。</summary>
        private int ImageBlockHeight(int cardWidth)
        {
            if (_image == null) return 0;
            return Theme.S(18) + ImageDisplaySize(cardWidth).Height + Theme.S(10);
        }

        /// <summary>卡片高度：版本行 + info.txt 内容 +（可选）说明图片，全部按字体度量推导。</summary>
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
                     + (int)Math.Ceiling(lines * lineHeight) + Theme.S(26)
                     + ImageBlockHeight(width);   // 与 OnPaint 里图片的可用宽度保持一致
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
            float textWidth = Math.Max(Theme.S(60), r.Width - pad * 2f);
            SizeF infoSize = Theme.MeasureText(_info, infoFont, textWidth);
            float textHeight = Math.Max(Theme.SF(24f), infoSize.Height);
            Gfx.DrawText(g, _info, infoFont, missing ? Theme.Error : Theme.TextPrimary,
                new RectangleF(r.X + pad, y, textWidth, textHeight), Typography.Wrap);

            // 说明图片放在最后（目录下没有 1.jpg 时完全不画，卡片也不会为其留白）
            if (_image != null)
            {
                Size size = ImageDisplaySize((int)textWidth);
                var dest = new Rectangle((int)(r.X + pad), (int)(y + textHeight + Theme.S(18)), size.Width, size.Height);
                try
                {
                    using (var attrs = new System.Drawing.Imaging.ImageAttributes())
                    {
                        attrs.SetWrapMode(System.Drawing.Drawing2D.WrapMode.TileFlipXY);
                        g.DrawImage(_image, dest, 0, 0, _image.Width, _image.Height, GraphicsUnit.Pixel, attrs);
                    }
                    Gfx.StrokeRounded(g, dest, Theme.RadiusSmall, Theme.Outline, 1f);
                }
                catch (Exception ex) { AppLog.Exception_("绘制说明图片失败", ex); }
            }
        }
    }
}
