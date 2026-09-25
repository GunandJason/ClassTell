using System;
using System.Drawing;
using System.Windows.Forms;

namespace ClassTell
{
    /// <summary>
    /// “界面设置”卡片：
    ///   · 字体大小滑块（9~20pt，实时预览）
    ///   · 深色 / 浅色模式切换
    ///   · 4 套强调色配色（水蓝 / 青碧 / 丁香 / 杏黄）
    /// 所有行高都由字体度量推导，大字号下不会互相挤压或被裁切。
    /// </summary>
    internal sealed class SettingsCard : CardPanel
    {
        private readonly MaterialSlider _slider;
        private readonly MaterialSwitch _modeSwitch;
        private readonly MaterialSwitch _traySwitch;
        private readonly MaterialSwitch _startupSwitch;
        private readonly AccentSwatch[] _swatches;
        private readonly Timer _saveTimer;

        private Rectangle _fontLabelRect;
        private Rectangle _previewRect;
        private Rectangle _modeLabelRect;
        private Rectangle _modeHintRect;
        private Rectangle _paletteLabelRect;
        private Rectangle _trayLabelRect;
        private Rectangle _trayHintRect;
        private Rectangle _startupLabelRect;
        private Rectangle _startupHintRect;

        public SettingsCard()
        {
            CardTitle = "界面与常规设置";
            CardSubtitle = "字体大小 · 深色 / 浅色模式 · 配色方案 · 托盘与开机自启动";

            _slider = new MaterialSlider
            {
                Minimum = Theme.MinFontSize,
                Maximum = Theme.MaxFontSize,
                Step = 0.5d,
                Value = Math.Max(Theme.MinFontSize, Math.Min(Theme.MaxFontSize, Settings.FontSize))
            };
            _slider.ValueChanged += OnFontSizeChanged;
            Controls.Add(_slider);

            _modeSwitch = new MaterialSwitch { Checked = Settings.DarkMode };
            _modeSwitch.SetCheckedImmediate(Settings.DarkMode);
            _modeSwitch.CheckedChanged += OnModeChanged;
            Controls.Add(_modeSwitch);

            // 运行方式：关闭时驻留托盘 / 开机自启动（只改设置，注册表写入由主窗口负责）
            _traySwitch = new MaterialSwitch { Checked = Settings.CloseToTray };
            _traySwitch.SetCheckedImmediate(Settings.CloseToTray);
            _traySwitch.CheckedChanged += OnTrayChanged;
            Controls.Add(_traySwitch);

            _startupSwitch = new MaterialSwitch { Checked = Settings.RunAtStartup };
            _startupSwitch.SetCheckedImmediate(Settings.RunAtStartup);
            _startupSwitch.CheckedChanged += OnStartupChanged;
            Controls.Add(_startupSwitch);

            ThemePalette[] palettes = Theme.Palettes;
            _swatches = new AccentSwatch[palettes.Length];
            for (int i = 0; i < palettes.Length; i++)
            {
                var swatch = new AccentSwatch
                {
                    PaletteKey = palettes[i].Key,
                    Swatch = palettes[i].Accent,
                    Name = palettes[i].Name,
                    Selected = string.Equals(palettes[i].Key, Settings.AccentKey, StringComparison.OrdinalIgnoreCase)
                };
                swatch.Click += OnSwatchClick;
                _swatches[i] = swatch;
                Controls.Add(swatch);
            }

            _saveTimer = new Timer { Interval = 450 };
            _saveTimer.Tick += (s, e) =>
            {
                _saveTimer.Stop();
                Settings.FontSize = (float)Math.Round(_slider.Value, 2);
            };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _saveTimer.Dispose();
            base.Dispose(disposing);
        }

        // ---------- 交互 ----------
        private void OnFontSizeChanged()
        {
            float size = (float)Math.Round(_slider.Value, 2);
            Theme.Apply(size, Settings.DarkMode, Settings.AccentKey);
            _saveTimer.Stop();
            _saveTimer.Start();
            LayoutChildren();
            Invalidate();
        }

        private void OnModeChanged()
        {
            bool dark = _modeSwitch.Checked;
            Settings.DarkMode = dark;
            Theme.Apply(Settings.FontSize, dark, Settings.AccentKey);
            LayoutChildren();
            Invalidate();
        }

        /// <summary>关闭窗口是否驻留托盘（只改设置项；主窗口按设置决定关闭行为）。</summary>
        private void OnTrayChanged()
        {
            Settings.CloseToTray = _traySwitch.Checked;
            AppLog.Info(_traySwitch.Checked ? "已启用：关闭窗口后驻留托盘" : "已关闭：关闭窗口即退出");
            Invalidate();
        }

        /// <summary>开机自启动开关（只改设置项；主窗口收到 Settings.Changed 后写注册表）。</summary>
        private void OnStartupChanged()
        {
            Settings.RunAtStartup = _startupSwitch.Checked;
            Invalidate();
        }

        private void OnSwatchClick(object sender, EventArgs e)
        {
            var swatch = sender as AccentSwatch;
            if (swatch == null) return;
            Settings.AccentKey = swatch.PaletteKey;
            Theme.Apply(Settings.FontSize, Settings.DarkMode, swatch.PaletteKey);
            foreach (AccentSwatch s in _swatches)
                s.Selected = string.Equals(s.PaletteKey, swatch.PaletteKey, StringComparison.OrdinalIgnoreCase);
            Invalidate();
        }

        // ---------- 布局 ----------
        /// <summary>卡片所需高度（随字号增长，避免内容被裁切）。</summary>
        public int PreferredHeight
        {
            get
            {
                float labelLine = Theme.LineHeight(Theme.Font(-0.5f, FontStyle.Regular));
                float hintLine = Theme.LineHeight(Theme.Font(-1.5f, FontStyle.Regular));
                float previewText = Theme.LineHeight(Theme.Font(3f, FontStyle.Bold)) + Theme.LineHeight(Theme.Body);
                return Theme.S(78)
                     + (int)labelLine + Theme.S(6) + Theme.S(38)
                     + Theme.S(14) + (int)previewText + Theme.S(20)
                     + (int)labelLine + Theme.S(10) + Theme.S(28)
                     + (int)hintLine + Theme.S(12)
                     + (int)labelLine + Theme.S(6) + Theme.S(76)
                     + (int)labelLine + Theme.S(2) + (int)hintLine + Theme.S(16)
                     + (int)labelLine + Theme.S(2) + (int)hintLine + Theme.S(24);
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutChildren();
        }

        protected override void OnThemeChanged()
        {
            base.OnThemeChanged();
            // 外部改动（例如开机自启动写入失败后的回滚）后同步开关状态
            if (_traySwitch != null && _traySwitch.Checked != Settings.CloseToTray) _traySwitch.SetCheckedImmediate(Settings.CloseToTray);
            if (_startupSwitch != null && _startupSwitch.Checked != Settings.RunAtStartup) _startupSwitch.SetCheckedImmediate(Settings.RunAtStartup);
            LayoutChildren();
        }

        private void LayoutChildren()
        {
            int pad = Theme.S(26);
            int width = Math.Max(Theme.S(120), Width - pad * 2);
            float y = Theme.S(78);

            float labelLine = Theme.LineHeight(Theme.Font(-0.5f, FontStyle.Regular));
            float hintLine = Theme.LineHeight(Theme.Font(-1.5f, FontStyle.Regular));

            _fontLabelRect = new Rectangle(pad, (int)y, width, (int)Math.Ceiling(labelLine));
            y += labelLine + Theme.S(4);

            int sliderHeight = Math.Max(Theme.S(36), (int)Math.Ceiling(labelLine) + Theme.S(10));
            _slider.SetBounds(pad, (int)y, Math.Max(Theme.S(120), width - Theme.S(84)), sliderHeight);
            y += sliderHeight + Theme.S(14);

            float previewText = Theme.LineHeight(Theme.Font(3f, FontStyle.Bold)) + Theme.LineHeight(Theme.Body);
            int previewHeight = (int)Math.Ceiling(previewText) + Theme.S(20);
            _previewRect = new Rectangle(pad, (int)y, width, previewHeight);
            y += previewHeight + Theme.S(20);

            _modeLabelRect = new Rectangle(pad, (int)y, width, (int)Math.Ceiling(labelLine));
            _modeSwitch.SetBounds(Width - pad - Theme.S(42), (int)(_modeLabelRect.Y + (labelLine - Theme.S(24)) / 2f), Theme.S(42), Theme.S(24));
            y += labelLine + Theme.S(2);
            _modeHintRect = new Rectangle(pad, (int)y, width, (int)Math.Ceiling(hintLine));
            y += hintLine + Theme.S(14);

            _paletteLabelRect = new Rectangle(pad, (int)y, width, (int)Math.Ceiling(labelLine));
            y += labelLine + Theme.S(6);

            int swatchHeight = Theme.S(72);
            int step = Math.Max(Theme.S(70), Math.Min(Theme.S(100), (width - Theme.S(6)) / Math.Max(1, _swatches.Length)));
            int swatchWidth = Math.Max(Theme.S(58), step - Theme.S(6));
            for (int i = 0; i < _swatches.Length; i++)
                _swatches[i].SetBounds(pad + i * step, (int)y, swatchWidth, swatchHeight);

            // 关闭时驻留托盘
            y += swatchHeight + Theme.S(18);
            _trayLabelRect = new Rectangle(pad, (int)y, width, (int)Math.Ceiling(labelLine));
            _traySwitch.SetBounds(Width - pad - Theme.S(42), (int)(_trayLabelRect.Y + (labelLine - Theme.S(24)) / 2f), Theme.S(42), Theme.S(24));
            y += labelLine + Theme.S(2);
            _trayHintRect = new Rectangle(pad, (int)y, width, (int)Math.Ceiling(hintLine));
            y += hintLine + Theme.S(16);

            // 开机自启动
            _startupLabelRect = new Rectangle(pad, (int)y, width, (int)Math.Ceiling(labelLine));
            _startupSwitch.SetBounds(Width - pad - Theme.S(42), (int)(_startupLabelRect.Y + (labelLine - Theme.S(24)) / 2f), Theme.S(42), Theme.S(24));
            y += labelLine + Theme.S(2);
            _startupHintRect = new Rectangle(pad, (int)y, width, (int)Math.Ceiling(hintLine));
        }

        // ---------- 绘制 ----------
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            Gfx.Smooth(g);

            Font labelFont = Theme.Font(-0.5f, FontStyle.Regular);
            Font hintFont = Theme.Font(-1.5f, FontStyle.Regular);

            // 字体大小 + 当前值
            Gfx.DrawText(g, string.Format("字体大小：{0:0.#} pt", _slider.Value), labelFont, Theme.TextPrimary,
                _fontLabelRect, Typography.SingleLine);
            string range = string.Format("范围 {0:0.#}~{1:0.#} pt", _slider.Minimum, _slider.Maximum);
            Gfx.DrawText(g, range, hintFont, Theme.TextSecondary,
                _fontLabelRect, new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter });

            // 预览框：标题行 + 正文行，行高按字号推导
            Gfx.FillRounded(g, _previewRect, Theme.RadiusSmall, Theme.SurfaceAlt);
            Gfx.StrokeRounded(g, _previewRect, Theme.RadiusSmall, Theme.Outline, 1f);
            float previewX = _previewRect.X + Theme.S(14);
            float previewWidth = Math.Max(Theme.S(40), _previewRect.Width - Theme.S(28));
            float previewY = _previewRect.Y + Theme.S(10);
            float titleLine = Theme.LineHeight(Theme.Font(3f, FontStyle.Bold));
            Gfx.DrawText(g, "预览：ClassTell 通知示例", Theme.Font(3f, FontStyle.Bold), Theme.TextPrimary,
                new RectangleF(previewX, previewY, previewWidth, titleLine), Typography.SingleLine);
            previewY += titleLine;
            Gfx.DrawText(g, "正文示例：邮件正文保留换行完整显示，超长自动换行与省略。", Theme.Body, Theme.TextSecondary,
                new RectangleF(previewX, previewY, previewWidth, Theme.LineHeight(Theme.Body)), Typography.SingleLine);

            // 深色 / 浅色模式
            Gfx.DrawText(g, _modeSwitch.Checked ? "深色模式" : "浅色模式", labelFont, Theme.TextPrimary,
                _modeLabelRect, Typography.SingleLine);
            Gfx.DrawText(g, _modeSwitch.Checked
                    ? "深色底 + 高对比文字；点击右侧开关可切换到浅色（白色简约）模式。"
                    : "白色简约底 + 加深后的强调色文字，适合明亮环境；点击开关切回深色。",
                hintFont, Theme.TextSecondary, _modeHintRect, Typography.SingleLine);

            // 配色方案
            Gfx.DrawText(g, "配色方案（强调色）：" + Theme.Palette.Name + " " + Theme.Palette.Hex, labelFont, Theme.TextPrimary,
                _paletteLabelRect, Typography.SingleLine);

            // 运行方式：关闭窗口是否驻留托盘
            Gfx.DrawText(g, _traySwitch.Checked ? "关闭窗口时：保持在后台继续收信" : "关闭窗口时：直接退出程序",
                labelFont, Theme.TextPrimary, _trayLabelRect, Typography.SingleLine);
            Gfx.DrawText(g, _traySwitch.Checked
                    ? "点右侧开关可改为“关闭即退出”；驻留托盘时用托盘菜单的“退出 ClassTell”结束程序。"
                    : "当前关闭窗口会直接退出；打开开关则隐藏到托盘并在后台继续收信。",
                hintFont, Theme.TextSecondary, _trayHintRect, Typography.SingleLine);

            // 运行方式：开机自启动
            Gfx.DrawText(g, _startupSwitch.Checked ? "开机自动启动：已开启" : "开机自动启动：未开启",
                labelFont, Theme.TextPrimary, _startupLabelRect, Typography.SingleLine);
            Gfx.DrawText(g, _startupSwitch.Checked
                    ? "已登记当前用户的启动项：开机后自动在托盘运行并开始收信。"
                    : "打开开关后开机自动在托盘启动并收信（当前用户级，无需管理员权限）。",
                hintFont, Theme.TextSecondary, _startupHintRect, Typography.SingleLine);
        }
    }
}
