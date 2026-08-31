using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Forms;

namespace SwCheckinConflictButtonAddin
{
    /// <summary>
    /// 用 WinEvent 即时发现目标窗体，并用定时器兜底扫描。
    /// 优先把按钮注入 Form.Controls，失败再退回标题栏 overlay。
    /// </summary>
    internal sealed class ConflictWindowWatcher : IDisposable
    {
        private readonly Control _sync;
        private readonly Timer _pollTimer;
        private readonly object _syncLock = new object();
        private readonly HashSet<IntPtr> _injected = new HashSet<IntPtr>();
        private readonly Dictionary<IntPtr, CaptionButtonOverlay> _overlays =
            new Dictionary<IntPtr, CaptionButtonOverlay>();

        private NativeMethods.WinEventDelegate _winEventProc;
        private IntPtr _hook = IntPtr.Zero;
        private bool _disposed;

        public ConflictWindowWatcher()
        {
            _sync = new Control();
            GC.KeepAlive(_sync.Handle);

            _pollTimer = new Timer { Interval = 400 };
            _pollTimer.Tick += (s, e) => ScanTopLevelWindows();
        }

        public void Start()
        {
            _winEventProc = OnWinEvent;
            uint pid = (uint)Process.GetCurrentProcess().Id;
            try
            {
                _hook = NativeMethods.SetWinEventHook(
                    NativeMethods.EVENT_OBJECT_DESTROY,
                    NativeMethods.EVENT_OBJECT_NAMECHANGE,
                    IntPtr.Zero,
                    _winEventProc,
                    pid,
                    0,
                    NativeMethods.WINEVENT_OUTOFCONTEXT);
                AddinLog.Info("WinEvent hook=" + _hook);
            }
            catch (Exception ex)
            {
                AddinLog.Info("SetWinEventHook 失败，改用轮询: " + ex.Message);
            }

            _pollTimer.Start();
            try
            {
                ScanTopLevelWindows();
            }
            catch (Exception ex)
            {
                AddinLog.Info("首次扫描失败: " + ex.Message);
            }
        }

        public void Stop()
        {
            _pollTimer.Stop();
            if (_hook != IntPtr.Zero)
            {
                NativeMethods.UnhookWinEvent(_hook);
                _hook = IntPtr.Zero;
            }

            List<IntPtr> injected;
            List<CaptionButtonOverlay> overlays;
            lock (_syncLock)
            {
                injected = new List<IntPtr>(_injected);
                _injected.Clear();
                overlays = new List<CaptionButtonOverlay>(_overlays.Values);
                _overlays.Clear();
            }

            foreach (IntPtr hwnd in injected)
            {
                HostFormButtonInjector.Remove(hwnd);
            }

            foreach (var overlay in overlays)
            {
                TryCloseOverlay(overlay);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Stop();
            _pollTimer.Dispose();
            _sync.Dispose();
            _disposed = true;
        }

        private void OnWinEvent(
            IntPtr hWinEventHook,
            uint eventType,
            IntPtr hwnd,
            int idObject,
            int idChild,
            uint dwEventThread,
            uint dwmsEventTime)
        {
            if (hwnd == IntPtr.Zero || idObject != NativeMethods.OBJID_WINDOW
                || idChild != NativeMethods.CHILDID_SELF)
            {
                return;
            }

            if (eventType == NativeMethods.EVENT_OBJECT_LOCATIONCHANGE)
            {
                lock (_syncLock)
                {
                    if (!_overlays.ContainsKey(hwnd))
                    {
                        return;
                    }
                }
            }

            RunOnUi(() => HandleEvent(eventType, hwnd));
        }

        private void HandleEvent(uint eventType, IntPtr hwnd)
        {
            if (eventType == NativeMethods.EVENT_OBJECT_DESTROY
                || eventType == NativeMethods.EVENT_OBJECT_HIDE)
            {
                Detach(hwnd);
                return;
            }

            if (eventType == NativeMethods.EVENT_OBJECT_LOCATIONCHANGE)
            {
                CaptionButtonOverlay overlay;
                lock (_syncLock)
                {
                    if (!_overlays.TryGetValue(hwnd, out overlay))
                    {
                        return;
                    }
                }

                overlay.Reposition();
                return;
            }

            if (IsTargetWindow(hwnd))
            {
                Attach(hwnd);
            }
        }

        private void ScanTopLevelWindows()
        {
            var seen = new HashSet<IntPtr>();
            NativeMethods.EnumWindows((hwnd, l) =>
            {
                if (IsTargetWindow(hwnd))
                {
                    seen.Add(hwnd);
                    Attach(hwnd);
                }

                return true;
            }, IntPtr.Zero);

            var stale = new List<IntPtr>();
            List<CaptionButtonOverlay> liveOverlays;
            lock (_syncLock)
            {
                foreach (IntPtr hwnd in _injected)
                {
                    if (!seen.Contains(hwnd) || !NativeMethods.IsWindow(hwnd)
                        || !NativeMethods.IsWindowVisible(hwnd))
                    {
                        stale.Add(hwnd);
                    }
                }

                foreach (IntPtr hwnd in _overlays.Keys)
                {
                    if (!seen.Contains(hwnd) || !NativeMethods.IsWindow(hwnd)
                        || !NativeMethods.IsWindowVisible(hwnd))
                    {
                        stale.Add(hwnd);
                    }
                }

                liveOverlays = new List<CaptionButtonOverlay>(_overlays.Values);
            }

            foreach (IntPtr hwnd in stale)
            {
                Detach(hwnd);
            }

            foreach (var overlay in liveOverlays)
            {
                if (!overlay.IsDisposed)
                {
                    overlay.Reposition();
                }
            }
        }

        private void Attach(IntPtr hwnd)
        {
            lock (_syncLock)
            {
                if (_injected.Contains(hwnd))
                {
                    HostFormButtonInjector.TryEnsureButton(hwnd);
                    return;
                }

                if (_overlays.ContainsKey(hwnd))
                {
                    _overlays[hwnd].Attach();
                    return;
                }
            }

            try
            {
                if (HostFormButtonInjector.TryEnsureButton(hwnd))
                {
                    lock (_syncLock)
                    {
                        _injected.Add(hwnd);
                    }

                    AddinLog.Info("已附加注入按钮 hwnd=" + hwnd.ToInt64().ToString("X"));
                    return;
                }

                var overlay = new CaptionButtonOverlay(hwnd);
                overlay.Attach();
                lock (_syncLock)
                {
                    _overlays[hwnd] = overlay;
                }

                AddinLog.Info("FromHandle 失败，改用 overlay hwnd=" + hwnd.ToInt64().ToString("X"));
            }
            catch (Exception ex)
            {
                AddinLog.Info("附加按钮失败: " + ex);
            }
        }

        private void Detach(IntPtr hwnd)
        {
            bool injected;
            CaptionButtonOverlay overlay;
            lock (_syncLock)
            {
                injected = _injected.Remove(hwnd);
                _overlays.TryGetValue(hwnd, out overlay);
                _overlays.Remove(hwnd);
            }

            if (injected)
            {
                HostFormButtonInjector.Remove(hwnd);
            }

            TryCloseOverlay(overlay);
            if (injected || overlay != null)
            {
                AddinLog.Info("已移除按钮 hwnd=" + hwnd.ToInt64().ToString("X"));
            }
        }

        private static void TryCloseOverlay(CaptionButtonOverlay overlay)
        {
            try
            {
                if (overlay != null && !overlay.IsDisposed)
                {
                    overlay.Close();
                    overlay.Dispose();
                }
            }
            catch (Exception ex)
            {
                AddinLog.Info("关闭 overlay 失败: " + ex.Message);
            }
        }

        private static bool IsTargetWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero
                || !NativeMethods.IsWindow(hwnd)
                || !NativeMethods.IsWindowVisible(hwnd)
                || NativeMethods.IsIconic(hwnd))
            {
                return false;
            }

            if (NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT) != hwnd)
            {
                return false;
            }

            int style = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_STYLE);
            if ((style & NativeMethods.WS_CHILD) != 0)
            {
                return false;
            }

            string title = NativeMethods.GetWindowTitle(hwnd);
            if (!string.Equals(title, AddinOptions.TargetWindowTitle, StringComparison.Ordinal))
            {
                return false;
            }

            string className = NativeMethods.GetWindowClass(hwnd);
            return className != null
                && className.StartsWith(AddinOptions.WinFormsClassPrefix, StringComparison.Ordinal);
        }

        private void RunOnUi(Action action)
        {
            if (_sync.IsDisposed)
            {
                return;
            }

            try
            {
                if (_sync.InvokeRequired)
                {
                    _sync.BeginInvoke(action);
                }
                else
                {
                    action();
                }
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }
}
