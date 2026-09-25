using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace ClassTell
{
    /// <summary>
    /// 页面切换动效层：把“旧页面”和“新页面”的截图做交叉淡入 + 水平位移（Google Shared Axis 风格）。
    /// 过渡期间它自己是不透明层，动画结束后隐藏并交还控制权。
    /// </summary>
    internal sealed class TransitionOverlay : MotionControl
    {
        private Bitmap _from;
        private Bitmap _to;
        private double _progress;
        private int _dx;
        private Motion _handle;

        public event Action Finished;

        public bool IsRunning { get { return _handle != null && !_handle.Done && !_handle.Canceled; } }

        public TransitionOverlay()
        {
            Visible = false;
            BackColor = Theme.Window;
        }

        /// <summary>过渡层自带底色，需要跟随深浅模式。</summary>
        protected override Color ThemeBackColor { get { return Theme.Window; } }

        /// <summary>执行一次过渡；位图由调用方生成，过渡结束后自动释放。</summary>
        public void Play(Bitmap from, Bitmap to, int slideDx, int durationMs)
        {
            DisposeBitmaps();
            _from = from;
            _to = to;
            _dx = slideDx;
            _progress = 0d;
            BackColor = Theme.Window;
            Visible = true;
            BringToFront();
            if (_handle != null) _handle.Stop();
            _handle = Animator.Run(durationMs, Ease.EmphasizedDecelerate, p =>
            {
                _progress = p;
                Invalidate();
            }, Complete);
        }

        private void Complete()
        {
            Visible = false;
            DisposeBitmaps();
            Action handler = Finished;
            if (handler != null) handler();
        }

        private void DisposeBitmaps()
        {
            if (_from != null) { _from.Dispose(); _from = null; }
            if (_to != null) { _to.Dispose(); _to = null; }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) DisposeBitmaps();
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            if (_from == null && _to == null) return;

            double p = _progress;
            if (_from != null)
            {
                DrawFaded(g, _from, -(float)(_dx * p), 1d - p);
            }
            if (_to != null)
            {
                DrawFaded(g, _to, (float)(_dx * (1d - p)), p);
            }
        }

        private static void DrawFaded(Graphics g, Bitmap bmp, float offsetX, double alpha)
        {
            if (alpha <= 0.001d) return;
            var dest = new RectangleF(offsetX, 0f, bmp.Width, bmp.Height);
            var attrs = new ImageAttributes();
            var matrix = new ColorMatrix { Matrix33 = (float)Math.Max(0d, Math.Min(1d, alpha)) };
            attrs.SetColorMatrix(matrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
            g.DrawImage(bmp, Rectangle.Round(dest), 0, 0, bmp.Width, bmp.Height, GraphicsUnit.Pixel, attrs);
            attrs.Dispose();
        }

        /// <summary>把控件渲染成位图（用于过渡与遮罩背景）。失败时返回 null。</summary>
        public static Bitmap CaptureControl(Control control, Size size)
        {
            if (control == null || size.Width <= 0 || size.Height <= 0) return null;
            try
            {
                var bmp = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
                var rect = new Rectangle(0, 0, size.Width, size.Height);
                control.DrawToBitmap(bmp, rect);
                return bmp;
            }
            catch (Exception ex)
            {
                AppLog.Exception_("页面截图失败", ex);
                return null;
            }
        }
    }
}
