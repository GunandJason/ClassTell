using System;
using System.Drawing;
using System.Windows.Forms;

namespace ClassTell
{
    /// <summary>
    /// 圆角输入框容器：自绘圆角底 + 描边（聚焦时描边变强调色），
    /// 内部放一个无边框的原生 TextBox，保留输入法、选区、复制粘贴等系统能力。
    /// </summary>
    internal sealed class RoundedInputHost : MotionControl
    {
        private readonly TextBox _box;
        private bool _focused;

        public RoundedInputHost()
        {
            _box = new TextBox
            {
                BorderStyle = BorderStyle.None,
                BackColor = Theme.SurfaceAlt,
                ForeColor = Theme.TextPrimary,
                Font = Theme.Font(-0.5f, FontStyle.Regular)
            };
            _box.GotFocus += (s, e) => { _focused = true; Invalidate(); };
            _box.LostFocus += (s, e) => { _focused = false; Invalidate(); };
            _box.TextChanged += (s, e) =>
            {
                var handler = TextChanged2;
                if (handler != null) handler();
            };
            Controls.Add(_box);
        }

        /// <summary>兼容 WinForms 自带事件签名之外的自定义回调。</summary>
        public event Action TextChanged2;

        /// <summary>内部的真实输入框（用于读写文本、显示/隐藏）。</summary>
        public TextBox Box { get { return _box; } }

        public override string Text
        {
            get { return _box.Text; }
            set { _box.Text = value ?? string.Empty; }
        }

        /// <summary>占位提示（灰色显示在空输入框上）。</summary>
        public string PlaceholderText { get; set; }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutBox();
        }

        private void LayoutBox()
        {
            int padX = Theme.S(10);
            int h = Math.Max(Theme.S(16), _box.PreferredHeight);
            _box.SetBounds(padX, Math.Max(Theme.S(2), (Height - h) / 2), Math.Max(Theme.S(10), Width - padX * 2), h);
        }

        protected override void OnThemeChanged()
        {
            _box.BackColor = Theme.SurfaceAlt;
            _box.ForeColor = Theme.TextPrimary;
            _box.Font = Theme.Font(-0.5f, FontStyle.Regular);
            LayoutBox();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Gfx.Smooth(g);
            var r = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            float radius = Theme.RadiusSmall;

            Gfx.FillRounded(g, r, radius, Theme.SurfaceAlt);
            Gfx.StrokeRounded(g, r, radius, _focused ? Theme.Accent : Theme.Outline, _focused ? Theme.SF(1.6f) : 1f);

            if (string.IsNullOrEmpty(_box.Text) && !string.IsNullOrEmpty(PlaceholderText))
            {
                Gfx.DrawText(g, PlaceholderText, _box.Font, Theme.TextSecondary,
                    new RectangleF(Theme.S(12), 0f, Math.Max(Theme.S(20), Width - Theme.S(20)), Height),
                    Typography.SingleLine);
            }
        }
    }
}
