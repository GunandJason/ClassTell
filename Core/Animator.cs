using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Forms;

namespace ClassTell
{
    /// <summary>动画句柄。</summary>
    internal sealed class Motion
    {
        internal double Duration;
        internal double Elapsed;
        internal Ease EaseKind;
        internal Action<double> Update;
        internal Action Finish;
        internal bool Done;
        internal bool Canceled;

        /// <summary>立即结束（不触发回调）。</summary>
        public void Stop()
        {
            Canceled = true;
            Animator.Remove(this);
        }
    }

    /// <summary>全局动画驱动：所有动效共用一条 ~60fps 计时器。</summary>
    internal static class Animator
    {
        private const int IntervalMs = 15;
        private static readonly List<Motion> Items = new List<Motion>();
        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        private static Timer _timer;
        private static double _lastTick;

        public static Motion Run(int durationMs, Ease ease, Action<double> update, Action finish = null)
        {
            var m = new Motion
            {
                Duration = Math.Max(1, durationMs),
                EaseKind = ease,
                Update = update,
                Finish = finish
            };
            Items.Add(m);
            EnsureTimer();
            return m;
        }

        /// <summary>补间：把 from→to 的当前值传给 onValue。</summary>
        public static Motion Tween(double from, double to, int durationMs, Ease ease, Action<double> onValue, Action finish = null)
        {
            return Run(durationMs, ease, p => { if (onValue != null) onValue(from + (to - from) * p); }, finish);
        }

        public static Motion Delay(int ms, Action action)
        {
            return Run(ms, Ease.Linear, p => { }, action);
        }

        public static void Remove(Motion m)
        {
            Items.Remove(m);
            if (Items.Count == 0) StopTimer();
        }

        private static void EnsureTimer()
        {
            if (_timer == null)
            {
                _timer = new Timer();
                _timer.Interval = IntervalMs;
                _timer.Tick += OnTick;
            }
            if (!_timer.Enabled)
            {
                _lastTick = Clock.Elapsed.TotalMilliseconds;
                _timer.Start();
            }
        }

        private static void StopTimer()
        {
            if (_timer != null) _timer.Stop();
        }

        private static void OnTick(object sender, EventArgs e)
        {
            double now = Clock.Elapsed.TotalMilliseconds;
            double dt = now - _lastTick;
            _lastTick = now;
            if (dt <= 0d) dt = IntervalMs;
            if (dt > 100d) dt = 100d;

            Motion[] snapshot = Items.ToArray();
            var completed = new List<Motion>();
            foreach (Motion m in snapshot)
            {
                if (m.Canceled || m.Done) continue;
                m.Elapsed += dt;
                double raw = m.Elapsed / m.Duration;
                if (raw >= 1d) raw = 1d;
                double p = Bezier.Eval(m.EaseKind, raw);
                try
                {
                    if (m.Update != null) m.Update(p);
                }
                catch (Exception ex)
                {
                    m.Done = true;
                    completed.Add(m);
                    AppLog.Exception_("动画回调异常（已忽略）", ex);
                    continue;
                }
                if (raw >= 1d)
                {
                    m.Done = true;
                    completed.Add(m);
                }
            }

            foreach (Motion m in completed)
            {
                Items.Remove(m);
                try
                {
                    if (m.Finish != null) m.Finish();
                }
                catch (Exception ex)
                {
                    AppLog.Exception_("动画完成回调异常（已忽略）", ex);
                }
            }

            if (Items.Count == 0) StopTimer();
        }
    }
}
