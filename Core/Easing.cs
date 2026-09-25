using System;

namespace ClassTell
{
    /// <summary>Google Material 风格缓动曲线。</summary>
    internal enum Ease
    {
        Linear,
        Standard,             // cubic-bezier(0.4, 0.0, 0.2, 1)   标准缓动
        Decelerate,           // cubic-bezier(0.0, 0.0, 0.2, 1)   进场
        Accelerate,           // cubic-bezier(0.4, 0.0, 1.0, 1)   退场
        EmphasizedDecelerate, // cubic-bezier(0.05, 0.7, 0.1, 1)  强调进场
        Emphasized,           // cubic-bezier(0.2, 0.0, 0.0, 1)   强调
        Overshoot             // cubic-bezier(0.34, 1.56, 0.64, 1)
    }

    /// <summary>三次贝塞尔缓动求值器（与 CSS cubic-bezier 行为一致）。</summary>
    internal static class Bezier
    {
        public static double Eval(Ease ease, double t)
        {
            switch (ease)
            {
                case Ease.Linear: return Clamp(t);
                case Ease.Standard: return Solve(0.4, 0.0, 0.2, 1.0, t);
                case Ease.Decelerate: return Solve(0.0, 0.0, 0.2, 1.0, t);
                case Ease.Accelerate: return Solve(0.4, 0.0, 1.0, 1.0, t);
                case Ease.EmphasizedDecelerate: return Solve(0.05, 0.7, 0.1, 1.0, t);
                case Ease.Emphasized: return Solve(0.2, 0.0, 0.0, 1.0, t);
                case Ease.Overshoot: return Solve(0.34, 1.56, 0.64, 1.0, t);
                default: return Clamp(t);
            }
        }

        private static double Clamp(double v) { return v < 0d ? 0d : (v > 1d ? 1d : v); }

        private static double Solve(double x1, double y1, double x2, double y2, double x)
        {
            if (x <= 0d) return 0d;
            if (x >= 1d) return 1d;
            double t = x;
            for (int i = 0; i < 8; i++)
            {
                double cx = Curve(t, x1, x2) - x;
                if (Math.Abs(cx) < 1e-5) break;
                double d = Derivative(t, x1, x2);
                if (Math.Abs(d) < 1e-6) break;
                t = Clamp(t - cx / d);
            }
            if (Math.Abs(Curve(t, x1, x2) - x) > 1e-4)
            {
                double lo = 0d, hi = 1d;
                for (int i = 0; i < 24; i++)
                {
                    t = (lo + hi) / 2d;
                    double v = Curve(t, x1, x2);
                    if (Math.Abs(v - x) < 1e-5) break;
                    if (v < x) lo = t; else hi = t;
                }
            }
            return Curve(t, y1, y2);
        }

        private static double Curve(double t, double p1, double p2)
        {
            double u = 1d - t;
            return 3d * u * u * t * p1 + 3d * u * t * t * p2 + t * t * t;
        }

        private static double Derivative(double t, double p1, double p2)
        {
            double u = 1d - t;
            return 3d * u * u * p1 + 6d * u * t * (p2 - p1) + 3d * t * t * (1d - p2);
        }
    }
}
