using System;
using System.Drawing;
using System.Windows.Forms;

namespace ClassTell
{
    /// <summary>Material 开关（当前仅用于展示“深色模式”状态）。</summary>
    internal sealed class MaterialSwitch : MotionControl
    {
        private double _knob;
        private bool _checked;

        public MaterialSwitch()
        {
            Size = new Size(Theme.S(42), Theme.S(24));
            Cursor = Cursors.Hand;
            TabStop = false;
        }

        public event Action CheckedChanged;

        public bool Checked
        {
            get { return _checked; }
            set
            {
                if (_checked == value) return;
                _checked = value;
                Animator.Tween(_knob, value ? 1d : 0d, 240, Ease.EmphasizedDecelerate, v => { _knob = v; Invalidate(); });
                Action handler = CheckedChanged;
                if (handler != null) handler();
            }
        }

        public void SetCheckedImmediate(bool value)
        {
            _checked = value;
            _knob = value ? 1d : 0d;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Gfx.Smooth(e.Graphics);
            var r = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            float radius = r.Height / 2f;

            Color track = Theme.Mix(Theme.Outline, Theme.Accent, _knob);
            if (!Enabled) track = Theme.Mix(track, Theme.SurfaceAlt, 0.5);
            Gfx.FillRounded(e.Graphics, r, radius, track);
            Gfx.StrokeRounded(e.Graphics, r, radius, Theme.Mix(Theme.Outline, Theme.Accent, _knob * 0.6), 1f);

            float knobRadius = r.Height / 2f - Theme.SF(3.5f);
            float left = r.X + Theme.SF(3.5f) + knobRadius;
            float right = r.Right - Theme.SF(3.5f) - knobRadius;
            float cx = left + (float)(_knob * (right - left));
            float cy = r.Y + r.Height / 2f;

            if (Enabled && HoverAmount > 0.02d)
            {
                float glow = knobRadius + (float)(Theme.SF(6f) * HoverAmount);
                Gfx.FillRounded(e.Graphics, new RectangleF(cx - glow, cy - glow, glow * 2f, glow * 2f), glow, Theme.HoverVeil);
            }

            Color knobColor = _checked ? Color.White : Theme.Mix(Theme.TextSecondary, Color.White, 0.4);
            if (!Enabled) knobColor = Theme.Mix(knobColor, Theme.Outline, 0.4);
            Gfx.FillRounded(e.Graphics, new RectangleF(cx - knobRadius, cy - knobRadius, knobRadius * 2f, knobRadius * 2f), knobRadius, knobColor);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (Enabled && e.Button == MouseButtons.Left) Checked = !Checked;
        }
    }
}
