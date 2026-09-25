using System;
using System.Drawing;
using System.Windows.Forms;

namespace ClassTell
{
    /// <summary>Material 滑块（字体大小调节用）：轨道 + 圆形滑块，支持鼠标拖动、滚轮与方向键。</summary>
    internal sealed class MaterialSlider : MotionControl
    {
        private double _value;
        private bool _dragging;
        private double _thumbGrow;

        public MaterialSlider()
        {
            Height = Theme.S(36);
            Minimum = 0d;
            Maximum = 1d;
            Step = 0.01d;
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        public double Minimum { get; set; }
        public double Maximum { get; set; }
        public double Step { get; set; }
        public bool UseCustomThumbColor { get; set; }
        public Color ThumbColor { get; set; }

        public event Action ValueChanged;

        public double Value
        {
            get { return _value; }
            set
            {
                double v = Snap(value);
                if (Math.Abs(v - _value) < 1e-9) return;
                _value = v;
                Invalidate();
                Action handler = ValueChanged;
                if (handler != null) handler();
            }
        }

        public double Normalized
        {
            get
            {
                double span = Maximum - Minimum;
                return span <= 0d ? 0d : (_value - Minimum) / span;
            }
        }

        public void SetNormalized(double t, bool raiseEvent)
        {
            double v = Snap(Minimum + Clamp01(t) * (Maximum - Minimum));
            if (Math.Abs(v - _value) < 1e-9) return;
            _value = v;
            Invalidate();
            if (raiseEvent)
            {
                Action handler = ValueChanged;
                if (handler != null) handler();
            }
        }

        private double Clamp(double v) { return Math.Max(Minimum, Math.Min(Maximum, v)); }
        private static double Clamp01(double v) { return Math.Max(0d, Math.Min(1d, v)); }

        private double Snap(double v)
        {
            if (Step > 0d)
            {
                double steps = Math.Round((v - Minimum) / Step);
                v = Minimum + steps * Step;
            }
            return Clamp(v);
        }

        private float ThumbRadius { get { return Theme.SF(9f) * (float)(1d + _thumbGrow * 0.35d); } }
        private float TrackLeft { get { return ThumbRadius; } }
        private float TrackRight { get { return Width - ThumbRadius; } }

        private float ValueToX(double v)
        {
            double span = Maximum - Minimum;
            double t = span <= 0d ? 0d : (v - Minimum) / span;
            return TrackLeft + (float)(Clamp01(t) * Math.Max(1f, TrackRight - TrackLeft));
        }

        private double XToValue(float x)
        {
            float usable = Math.Max(1f, TrackRight - TrackLeft);
            double t = Clamp01((x - TrackLeft) / usable);
            return Minimum + t * (Maximum - Minimum);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Gfx.Smooth(e.Graphics);
            float cy = Height / 2f;
            float trackHeight = Theme.SF(4f);
            float left = TrackLeft;
            float right = TrackRight;
            float thumbX = ValueToX(_value);

            var full = new RectangleF(left, cy - trackHeight / 2f, Math.Max(1f, right - left), trackHeight);
            Gfx.FillRounded(e.Graphics, full, trackHeight / 2f, Theme.Outline);

            var active = new RectangleF(left, cy - trackHeight / 2f, Math.Max(1f, thumbX - left), trackHeight);
            Gfx.FillRounded(e.Graphics, active, trackHeight / 2f, Theme.Accent);

            float radius = ThumbRadius;
            Color thumb = UseCustomThumbColor && ThumbColor.A > 0 ? ThumbColor : Theme.Accent;
            if (Enabled)
            {
                double glow = Math.Max(HoverAmount, _thumbGrow);
                if (glow > 0.02d)
                {
                    float glowRadius = radius + (float)(Theme.SF(9f) * glow);
                    Gfx.FillRounded(e.Graphics,
                        new RectangleF(thumbX - glowRadius, cy - glowRadius, glowRadius * 2f, glowRadius * 2f),
                        glowRadius, Theme.Mix(Color.Transparent, Theme.AccentSoft, glow));
                }
            }
            else
            {
                thumb = Theme.Mix(thumb, Theme.Outline, 0.6);
            }
            var thumbRect = new RectangleF(thumbX - radius, cy - radius, radius * 2f, radius * 2f);
            Gfx.FillRounded(e.Graphics, thumbRect, radius, thumb);
            Gfx.StrokeRounded(e.Graphics, thumbRect, radius, Theme.Mix(Theme.Window, Theme.Accent, 0.4), Theme.SF(1.5f));
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!Enabled || e.Button != MouseButtons.Left) return;
            _dragging = true;
            Animator.Tween(_thumbGrow, 1d, 140, Ease.Standard, v => { _thumbGrow = v; Invalidate(); });
            Value = XToValue(e.X);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_dragging) return;
            Value = XToValue(e.X);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!_dragging) return;
            _dragging = false;
            Animator.Tween(_thumbGrow, 0d, 220, Ease.Standard, v => { _thumbGrow = v; Invalidate(); });
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (!Enabled || Step <= 0d) return;
            Value = _value + Math.Sign(e.Delta) * Step;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (!Enabled) return;
            double step = Step > 0d ? Step : (Maximum - Minimum) / 20d;
            switch (e.KeyCode)
            {
                case Keys.Left:
                case Keys.Down:
                    Value = _value - step;
                    e.Handled = true;
                    break;
                case Keys.Right:
                case Keys.Up:
                    Value = _value + step;
                    e.Handled = true;
                    break;
                case Keys.PageDown:
                    Value = _value - step * 5d;
                    e.Handled = true;
                    break;
                case Keys.PageUp:
                    Value = _value + step * 5d;
                    e.Handled = true;
                    break;
                case Keys.Home:
                    Value = Minimum;
                    e.Handled = true;
                    break;
                case Keys.End:
                    Value = Maximum;
                    e.Handled = true;
                    break;
            }
        }

        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Left:
                case Keys.Right:
                case Keys.Up:
                case Keys.Down:
                case Keys.Home:
                case Keys.End:
                    return true;
            }
            return base.IsInputKey(keyData);
        }
    }
}

