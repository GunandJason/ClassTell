using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace ClassTell
{
    internal enum ThemeMode
    {
        Dark,
        Light
    }

    /// <summary>
    /// 一套配色方案（强调色）。色值取自常见中国传统色 / 常用网页配色：
    /// 水蓝 #7AB5D6、青碧 #48C0A3、丁香 #B57EDC、杏黄 #FFA631。
    /// TextDarkHex 是同一色相在“浅色底”上用于文字与图标的加深版本，保证对比度。
    /// </summary>
    internal sealed class ThemePalette
    {
        public ThemePalette(string key, string name, string hex, string textDarkHex)
        {
            Key = key;
            Name = name;
            Hex = hex;
            TextDarkHex = textDarkHex;
            Accent = ColorTranslator.FromHtml(hex);
            AccentTextOnLight = ColorTranslator.FromHtml(textDarkHex);
        }

        public string Key { get; private set; }
        public string Name { get; private set; }
        public string Hex { get; private set; }
        public string TextDarkHex { get; private set; }
        public Color Accent { get; private set; }
        public Color AccentTextOnLight { get; private set; }
    }

    /// <summary>
    /// 全局视觉主题：深色 / 浅色两种模式 × 4 套强调色，字号与 DPI 缩放集中在这里。
    /// 自绘控件在 OnPaint 中实时读取颜色，因此模式、配色、字号都可热切换。
    /// </summary>
    internal static class Theme
    {
        public const string FontFamilyName = "Microsoft YaHei";   // 微软雅黑
        public const string IconFontName = "Segoe MDL2 Assets";   // Win10/11 内置图标字体
        public const string DefaultPaletteKey = "water";
        /// <summary>正式版版本号（显示在标题栏底部、关于页与日志里）。</summary>
        public const string VersionText = "v1.0.0_r";

        private static readonly ThemePalette[] AllPalettes =
        {
            new ThemePalette("water",   "水蓝", "#7AB5D6", "#2E7CA8"),
            new ThemePalette("teal",    "青碧", "#48C0A3", "#12816A"),
            new ThemePalette("lilac",   "丁香", "#B57EDC", "#7B44B0"),
            new ThemePalette("apricot", "杏黄", "#FFA631", "#A66300")
        };

        private static float _fontSize = 10f;
        private static ThemeMode _mode = ThemeMode.Dark;
        private static ThemePalette _palette = AllPalettes[0];
        private static float _dpiScale = 1f;
        private static float _density = 1f;
        private static float _logicalWidth;
        private static float _logicalHeight;
        private static readonly Dictionary<string, Font> FontCache = new Dictionary<string, Font>();
        private static readonly Dictionary<string, Font> IconFontCache = new Dictionary<string, Font>();

        /// <summary>主题、配色或字号发生变化时触发（UI 线程）。</summary>
        public static event Action Changed;

        public static ThemeMode Mode { get { return _mode; } }
        public static bool Dark { get { return _mode == ThemeMode.Dark; } }
        public static float FontSize { get { return _fontSize; } }
        public static float DpiScale { get { return _dpiScale; } }
        public static ThemePalette Palette { get { return _palette; } }
        public static ThemePalette[] Palettes { get { return AllPalettes; } }

        public static ThemePalette FindPalette(string key)
        {
            if (!string.IsNullOrEmpty(key))
            {
                foreach (ThemePalette p in AllPalettes)
                {
                    if (string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase)) return p;
                }
            }
            return AllPalettes[0];
        }

        private static Color Hex(string hex) { return ColorTranslator.FromHtml(hex); }

        // ---------- 颜色 ----------
        public static Color Window { get { return _mode == ThemeMode.Dark ? Hex("#121316") : Hex("#FFFFFF"); } }
        public static Color Surface { get { return _mode == ThemeMode.Dark ? Hex("#1B1D21") : Hex("#FFFFFF"); } }
        public static Color SurfaceAlt { get { return _mode == ThemeMode.Dark ? Hex("#212429") : Hex("#F3F6F9"); } }
        public static Color NavBackground { get { return _mode == ThemeMode.Dark ? Hex("#17191C") : Hex("#F7FAFC"); } }
        public static Color Outline { get { return _mode == ThemeMode.Dark ? Hex("#2C3037") : Hex("#E2E8EE"); } }
        public static Color TextPrimary { get { return _mode == ThemeMode.Dark ? Hex("#E8EAED") : Hex("#1F2328"); } }
        public static Color TextSecondary { get { return _mode == ThemeMode.Dark ? Hex("#9AA0A6") : Hex("#5F6B76"); } }
        public static Color Error { get { return _mode == ThemeMode.Dark ? Hex("#F28B82") : Hex("#C0392B"); } }
        public static Color Success { get { return _mode == ThemeMode.Dark ? Hex("#81C995") : Hex("#1E8E3E"); } }
        public static Color Warning { get { return _mode == ThemeMode.Dark ? Hex("#FDD663") : Hex("#B26A00"); } }
        public static Color Scrim { get { return Color.FromArgb(_mode == ThemeMode.Dark ? 170 : 110, 0, 0, 0); } }
        public static Color HoverVeil { get { return _mode == ThemeMode.Dark ? Color.FromArgb(20, 255, 255, 255) : Color.FromArgb(16, 0, 0, 0); } }
        public static Color PressVeil { get { return _mode == ThemeMode.Dark ? Color.FromArgb(38, 255, 255, 255) : Color.FromArgb(28, 0, 0, 0); } }
        public static Color RippleColor { get { return _mode == ThemeMode.Dark ? Color.FromArgb(92, 255, 255, 255) : Color.FromArgb(60, 0, 0, 0); } }

        /// <summary>强调色（填充、指示条、选中态）。</summary>
        public static Color Accent { get { return _palette.Accent; } }
        public static Color AccentHover { get { return Darken(_palette.Accent, 0.12); } }
        public static Color AccentPressed { get { return Darken(_palette.Accent, 0.22); } }

        /// <summary>强调色文字：深色模式用提亮版本，浅色模式用加深版本，保证可读性。</summary>
        public static Color AccentText
        {
            get { return _mode == ThemeMode.Dark ? Lighten(_palette.Accent, 0.16) : _palette.AccentTextOnLight; }
        }

        /// <summary>填充按钮前景色（按强调色亮度自动选深色或白色文字）。</summary>
        public static Color OnAccent
        {
            get { return Luminance(_palette.Accent) > 0.5 ? Hex("#0D2430") : Color.White; }
        }

        public static Color AccentSoft { get { return Color.FromArgb(_mode == ThemeMode.Dark ? 46 : 34, _palette.Accent); } }
        public static Color AccentSoftStrong { get { return Color.FromArgb(_mode == ThemeMode.Dark ? 76 : 54, _palette.Accent); } }

        /// <summary>投影强度（浅色模式下用更淡的灰色投影）。</summary>
        public static double ShadowScale { get { return _mode == ThemeMode.Dark ? 1d : 0.55d; } }

        // ---------- 圆角 / 间距（随 DPI 缩放） ----------
        public static int RadiusCard { get { return S(14); } }
        public static int RadiusSmall { get { return S(8); } }
        public static int PagePadding { get { return S(24); } }
        public static int Gap { get { return S(16); } }
        public static int NavWidth { get { return S(206); } }

        // ---------- 颜色工具 ----------
        public static Color Darken(Color c, double t) { return Mix(c, Color.Black, t); }
        public static Color Lighten(Color c, double t) { return Mix(c, Color.White, t); }

        public static double Luminance(Color c)
        {
            return (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255d;
        }

        public static Color WithAlpha(Color c, int alpha)
        {
            return Color.FromArgb(Math.Max(0, Math.Min(255, alpha)), c);
        }

        // ---------- 字体 ----------
        // 说明：字号以“磅(pt)”定义，但按像素创建（换算公式 pt * DPI/72），
        // 因此 100% 缩放时 10pt≈13px，200% 缩放时物理像素翻倍而“逻辑尺寸”不变，
        // 既不会在高缩放下把字体放大两遍，也不会在小屏上小到看不清。
        public static Font Body { get { return Font(0f, FontStyle.Regular); } }
        public static Font BodyBold { get { return Font(0f, FontStyle.Bold); } }
        public static Font Small { get { return Font(-1f, FontStyle.Regular); } }

        /// <summary>磅 → 像素（含 DPI 与紧凑系数）。</summary>
        public static float PointToPixel(float pt)
        {
            return Math.Max(8f, (float)Math.Round(pt * _dpiScale * 96f / 72f, 1));
        }

        public static Font Font(float deltaPt, FontStyle style)
        {
            string key = deltaPt.ToString("0.##") + "|" + (int)style;
            Font cached;
            if (FontCache.TryGetValue(key, out cached)) return cached;
            float px = PointToPixel(ClampFontSize(_fontSize + deltaPt));
            Font f;
            try { f = new Font(FontFamilyName, px, style, GraphicsUnit.Pixel); }
            catch (Exception) { f = new Font(FontFamily.GenericSansSerif, px, style, GraphicsUnit.Pixel); }
            FontCache[key] = f;
            return f;
        }

        public static Font IconFont(float deltaPt)
        {
            string key = deltaPt.ToString("0.##");
            Font cached;
            if (IconFontCache.TryGetValue(key, out cached)) return cached;
            float px = PointToPixel(ClampFontSize(_fontSize + deltaPt));
            Font f;
            try { f = new Font(IconFontAvailable ? IconFontName : FontFamilyName, px, FontStyle.Regular, GraphicsUnit.Pixel); }
            catch (Exception) { f = new Font(FontFamily.GenericSansSerif, px, FontStyle.Regular, GraphicsUnit.Pixel); }
            IconFontCache[key] = f;
            return f;
        }

        private static bool? _iconFontAvailable;

        /// <summary>系统是否安装了图标字体（Segoe MDL2 Assets）；未安装时用文字符号兜底，避免出现乱码方块。</summary>
        public static bool IconFontAvailable
        {
            get
            {
                if (!_iconFontAvailable.HasValue)
                {
                    try
                    {
                        using (var family = new FontFamily(IconFontName))
                        {
                            _iconFontAvailable = string.Equals(family.Name, IconFontName, StringComparison.OrdinalIgnoreCase);
                        }
                    }
                    catch (Exception) { _iconFontAvailable = false; }
                }
                return _iconFontAvailable.Value;
            }
        }

        /// <summary>图标不可用时返回兜底文本（例如 "›" "×"），可用时返回图标字形。</summary>
        public static string IconOr(string glyph, string fallback)
        {
            return IconFontAvailable ? glyph : fallback;
        }

        /// <summary>
        /// 绘制图标：安装了图标字体时用图标字体绘制字形，否则用普通字体绘制兜底符号，
        /// 因此在任何系统上都不会出现乱码方块。
        /// </summary>
        public static void DrawIcon(Graphics g, string glyph, string fallback, float deltaPt, Color color, Rectangle bounds, StringAlignment align)
        {
            if (IconFontAvailable) Gfx.DrawGlyph(g, glyph, IconFont(deltaPt), color, bounds, align);
            else Gfx.DrawGlyph(g, fallback, Font(deltaPt + 1f, FontStyle.Bold), color, bounds, align);
        }

        /// <summary>递归重绘整棵控件树（主题切换后消除残留旧色）。</summary>
        public static void InvalidateTree(Control root)
        {
            if (root == null) return;
            try
            {
                root.Invalidate(true);
                foreach (Control child in root.Controls) InvalidateTree(child);
            }
            catch (Exception) { }
        }

        private static float ClampFontSize(float pt)
        {
            return Math.Max(MinFontSize, Math.Min(MaxFontSize, pt));
        }

        /// <summary>单行文字高度（排版时统一用它计算行高，避免大字号下内容被裁切）。</summary>
        public static float LineHeight(Graphics g, Font font)
        {
            return font.GetHeight(g) * 1.28f;
        }

        private static Graphics _measureGraphics;

        /// <summary>测量用 Graphics（屏幕 DC，仅在 UI 线程使用）。</summary>
        private static Graphics MeasureGraphics
        {
            get
            {
                if (_measureGraphics == null) _measureGraphics = Graphics.FromHwnd(IntPtr.Zero);
                return _measureGraphics;
            }
        }

        /// <summary>单行行高（不依赖外部 Graphics，布局时用它做度量）。</summary>
        public static float LineHeight(Font font)
        {
            return font.GetHeight(MeasureGraphics) * 1.28f;
        }

        /// <summary>测量多行文本高度（布局时用它做度量）。</summary>
        public static SizeF MeasureText(string text, Font font, float maxWidth)
        {
            if (string.IsNullOrEmpty(text)) return SizeF.Empty;
            return MeasureGraphics.MeasureString(text, font, new SizeF(Math.Max(1f, maxWidth), 100000f), Typography.Wrap);
        }

        /// <summary>测量单行文本宽度（布局时用它做度量）。</summary>
        public static float MeasureLine(string text, Font font)
        {
            if (string.IsNullOrEmpty(text)) return 0f;
            return MeasureGraphics.MeasureString(text, font, new SizeF(100000f, 100000f), Typography.SingleLine).Width;
        }

        /// <summary>DPI 缩放（启动时按主屏 DPI 计算，窗口 DPI 变化时刷新），同时推算紧凑系数。</summary>
        public static void UpdateDpi(float dpiScale)
        {
            Rectangle wa = Screen.PrimaryScreen == null ? new Rectangle(0, 0, 1920, 1080) : Screen.PrimaryScreen.WorkingArea;
            UpdateDpi(dpiScale, new Size(wa.Width, wa.Height));
        }

        /// <summary>DPI 缩放 + 可用显示区域（物理像素）：据此决定紧凑系数，做响应式适配。</summary>
        public static void UpdateDpi(float dpiScale, Size physicalWorkArea)
        {
            if (dpiScale <= 0f) dpiScale = 1f;
            float density = ComputeDensity(dpiScale, physicalWorkArea);
            bool dirty = Math.Abs(dpiScale - _dpiScale) >= 0.01f || Math.Abs(density - _density) >= 0.01f;
            _dpiScale = dpiScale;
            _density = density;
            _logicalWidth = physicalWorkArea.Width / dpiScale;
            _logicalHeight = physicalWorkArea.Height / dpiScale;
            if (!dirty) return;
            ClearFonts();
            RaiseChanged();
        }

        /// <summary>按“逻辑可用区域”决定紧凑系数：小屏/高缩放时适当收紧间距，避免内容被挤压。</summary>
        private static float ComputeDensity(float dpiScale, Size physicalWorkArea)
        {
            if (physicalWorkArea.Width <= 0 || physicalWorkArea.Height <= 0) return 1f;
            float lw = physicalWorkArea.Width / dpiScale;
            float lh = physicalWorkArea.Height / dpiScale;
            if (lw < 1366f || lh < 800f) return 0.84f;   // 逻辑区域很小（1080p@125% 及以下、2K@200% 等）
            if (lw < 1680f || lh < 1000f) return 0.92f;  // 偏小（1536x864/900 这类逻辑尺寸）
            return 1f;                                   // 1920x1080 及以上的逻辑区域
        }

        /// <summary>
        /// 首次运行时推荐的默认字号：按逻辑可用高度在 9~10.5pt 之间选择，
        /// 避免在高分辨率/高缩放下字体过大、低分辨率下过小。
        /// </summary>
        public static float RecommendedFontSize
        {
            get
            {
                float h = _logicalHeight > 0f ? _logicalHeight : 900f;
                if (h < 700f) return 9f;
                if (h < 900f) return 9.5f;
                if (h < 1200f) return 10f;
                return 10.5f;
            }
        }

        public static float LogicalWidth { get { return _logicalWidth; } }
        public static float LogicalHeight { get { return _logicalHeight; } }
        public static float Density { get { return _density; } }

        /// <summary>应用字号 / 深浅模式 / 配色方案。</summary>
        public static void Apply(float fontSize, bool dark, string paletteKey = null)
        {
            ThemeMode mode = dark ? ThemeMode.Dark : ThemeMode.Light;
            ThemePalette palette = FindPalette(paletteKey ?? (_palette == null ? DefaultPaletteKey : _palette.Key));
            float size = ClampFontSize(fontSize);
            bool dirty = Math.Abs(size - _fontSize) > 0.001f || mode != _mode || palette != _palette;
            _fontSize = size;
            _mode = mode;
            _palette = palette;
            if (!dirty) return;
            ClearFonts();
            RaiseChanged();
        }

        /// <summary>字号上限/下限与默认值（默认值已按可读性适当调小）。</summary>
        public const float MaxFontSize = 16f;
        public const float MinFontSize = 8f;
        public const float DefaultFontSize = 10f;

        public static void RaiseChanged()
        {
            Action h = Changed;
            if (h != null) h();
        }

        private static void ClearFonts()
        {
            foreach (Font f in FontCache.Values) { try { f.Dispose(); } catch (Exception) { } }
            FontCache.Clear();
            foreach (Font f in IconFontCache.Values) { try { f.Dispose(); } catch (Exception) { } }
            IconFontCache.Clear();
        }

        // ---------- 缩放辅助（DPI × 屏幕紧凑系数，构成响应式布局） ----------
        public static int S(int px) { return (int)Math.Round(px * _dpiScale * _density); }
        public static int Sim(float px) { return (int)Math.Round(px); }
        public static float SF(float px) { return px * _dpiScale * _density; }

        public static Color Mix(Color a, Color b, double t)
        {
            t = Math.Max(0d, Math.Min(1d, t));
            return Color.FromArgb(
                (int)Math.Round(a.A + (b.A - a.A) * t),
                (int)Math.Round(a.R + (b.R - a.R) * t),
                (int)Math.Round(a.G + (b.G - a.G) * t),
                (int)Math.Round(a.B + (b.B - a.B) * t));
        }
    }
}
