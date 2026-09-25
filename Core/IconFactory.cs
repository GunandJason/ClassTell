using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;
using System.Drawing.Text;

namespace ClassTell
{
    /// <summary>运行时生成程序图标（水蓝色圆角方块 + 白色 C），避免依赖二进制资源。</summary>
    internal static class IconFactory
    {
        private static Icon _appIcon;
        private static IntPtr _appIconHandle = IntPtr.Zero;

        public static Icon AppIcon
        {
            get
            {
                if (_appIcon == null) _appIcon = CreateIcon(32);
                return _appIcon;
            }
        }

        /// <summary>配色切换后让程序图标按新强调色重新生成。</summary>
        public static void InvalidateAppIcon()
        {
            if (_appIcon != null)
            {
                try { _appIcon.Dispose(); } catch (Exception) { }
                _appIcon = null;
            }
        }

        public static Icon CreateIcon(int size)
        {
            using (Bitmap bmp = CreateBrandBitmap(size, true))
            {
                IntPtr handle = bmp.GetHicon();
                Icon icon = (Icon)Icon.FromHandle(handle).Clone();
                Native.DestroyIconHandle(handle);
                if (size == 32 && _appIconHandle != IntPtr.Zero)
                {
                    Native.DestroyIconHandle(_appIconHandle);
                }
                return icon;
            }
        }

        /// <summary>品牌图形：圆角矩形 + 水蓝色渐变 + 白色 C。</summary>
        public static Bitmap CreateBrandBitmap(int size, bool drawLetter)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                Gfx.Smooth(g);
                g.Clear(Color.Transparent);

                var rect = new RectangleF(0.5f, 0.5f, size - 1f, size - 1f);
                float radius = size * 0.26f;
                using (var path = Gfx.RoundedPath(rect, radius))
                using (var brush = new LinearGradientBrush(rect, Theme.Accent, Theme.AccentPressed, 55f))
                {
                    g.FillPath(brush, path);
                }

                if (drawLetter)
                {
                    // 用 TextRenderer 精确居中绘制字母，避免自绘标题栏/图标里的错位
                    using (var font = new Font(Theme.FontFamilyName, size * 0.54f, FontStyle.Bold, GraphicsUnit.Pixel))
                    {
                        TextFormatFlags flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                                                TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix |
                                                TextFormatFlags.SingleLine;
                        TextRenderer.DrawText(g, "C", font, new Rectangle(0, 0, size, size), Theme.OnAccent, flags);
                    }
                }
            }
            return bmp;
        }
    }
}
