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
        private IntPtr _hookCreate = IntPtr.Zero;
        private IntPtr _hookName = IntPtr.Zero;
        private bool _disposed;

        private const int PollIdleMs = 2000;
        private const int PollActiveMs = 1000;

        public ConflictWindowWatcher()
        {
            _sync = new Control();
            GC.KeepAlive(_sync.Handle);

            _pollTimer = new Timer { Interval = PollIdleMs };
            _pollTimer.Tick += (s, e) => ScanTopLevelWindows();
        }

        public void Start()
        {
            _winEventProc = OnWinEvent;
            uint pid = (uint)Process.GetCurrentProcess().Id;
            try
            {
                // DESTROY..HIDE 连续且不含 LOCATIONCHANGE；改标题单独钩 NAMECHANGE。
                _hookCreate = NativeMethods.SetWinEventHook(
                    NativeMethods.EVENT_OBJECT_DESTROY,
                    NativeMethods.EVENT_OBJECT_HIDE,
                    IntPtr.Zero,
                    _winEventProc,
                    pid,
                    0,
                    NativeMethods.WINEVENT_OUTOFCONTEXT);
                _hookName = NativeMethods.SetWinEventHook(
                    NativeMethods.EVENT_OBJECT_NAMECHANGE,
                    NativeMethods.EVENT_OBJECT_NAMECHANGE,
                    IntPtr.Zero,
                    _winEventProc,
                    pid,
                    0,
                    NativeMethods.WINEVENT_OUTOFCONTEXT);
                AddinLog.Info("WinEvent hook create=" + _hookCreate + " name=" + _hookName);
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
            Unhook(ref _hookCreate);
            Unhook(ref _hookName);

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
                // 卸载插件时也不要同步 Invoke 到对方窗体线程
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

            if (eventType == NativeMethods.EVENT_OBJECT_SHOW
                || eventType == NativeMethods.EVENT_OBJECT_NAMECHANGE)
            {
                if (!string.Equals(
                    NativeMethods.GetWindowTitle(hwnd),
                    AddinOptions.TargetWindowTitle,
                    StringComparison.Ordinal))
                {
                    return;
                }
            }
            else if (eventType == NativeMethods.EVENT_OBJECT_HIDE
                || eventType == NativeMethods.EVENT_OBJECT_DESTROY)
            {
                lock (_syncLock)
                {
                    if (!_injected.Contains(hwnd) && !_overlays.ContainsKey(hwnd))
                    {
                        return;
                    }
                }
            }
            else
            {
                return;
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

            UpdatePollInterval();
        }

        private void UpdatePollInterval()
        {
            bool active;
            lock (_syncLock)
            {
                active = _injected.Count > 0 || _overlays.Count > 0;
            }

            int interval = active ? PollActiveMs : PollIdleMs;
            if (_pollTimer.Interval != interval)
            {
                _pollTimer.Interval = interval;
            }
        }

        private static void Unhook(ref IntPtr hook)
        {
            if (hook == IntPtr.Zero)
            {
                return;
            }

            NativeMethods.UnhookWinEvent(hook);
            hook = IntPtr.Zero;
        }

        private void Attach(IntPtr hwnd)
        {
            lock (_syncLock)
            {
                if (_injected.Contains(hwnd) || _overlays.ContainsKey(hwnd))
                {
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
                    UpdatePollInterval();
                    return;
                }

                var overlay = new CaptionButtonOverlay(hwnd);
                overlay.Attach();
                lock (_syncLock)
                {
                    _overlays[hwnd] = overlay;
                }

                AddinLog.Info("无法注入 Controls，改用 overlay hwnd=" + hwnd.ToInt64().ToString("X"));
                UpdatePollInterval();
            }
            catch (Exception ex)
            {
                AddinLog.Info("附加按钮失败: " + ex.Message);
            }
        }

        private void Detach(IntPtr hwnd)
        {
            CaptionButtonOverlay overlay;
            bool injected;
            lock (_syncLock)
            {
                injected = _injected.Remove(hwnd);
                _overlays.TryGetValue(hwnd, out overlay);
                _overlays.Remove(hwnd);
            }

            // 弹窗正在关闭时不要 Remove/Invoke 对方 Form，子控件会随窗体一起销毁。
            TryCloseOverlay(overlay);
            if (injected || overlay != null)
            {
                AddinLog.Info("已移除跟踪 hwnd=" + hwnd.ToInt64().ToString("X"));
                UpdatePollInterval();
            }
        }

        private static void TryCloseOverlay(CaptionButtonOverlay overlay)
        {
            if (overlay == null || overlay.IsDisposed)
            {
                return;
            }

            try
            {
                if (overlay.IsHandleCreated && overlay.InvokeRequired)
                {
                    overlay.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            if (!overlay.IsDisposed)
                            {
                                overlay.Close();
                                overlay.Dispose();
                            }
                        }
                        catch (Exception ex)
                        {
                            AddinLog.Info("异步关闭 overlay 失败: " + ex.Message);
                        }
                    }));
                    return;
                }

                overlay.Close();
                overlay.Dispose();
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
