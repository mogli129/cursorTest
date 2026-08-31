using System;
using System.Runtime.InteropServices;
using System.Text;

namespace SwCheckinConflictButtonAddin
{
    internal static class NativeMethods
    {
        public const uint EVENT_OBJECT_DESTROY = 0x8001;
        public const uint EVENT_OBJECT_SHOW = 0x8002;
        public const uint EVENT_OBJECT_HIDE = 0x8003;
        public const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
        public const uint EVENT_OBJECT_NAMECHANGE = 0x800C;

        public const uint WINEVENT_OUTOFCONTEXT = 0;
        public const int OBJID_WINDOW = 0;
        public const int CHILDID_SELF = 0;

        public const int GWL_STYLE = -16;
        public const int WS_CAPTION = 0x00C00000;
        public const int WS_VISIBLE = 0x10000000;
        public const int WS_CHILD = 0x40000000;

        public const int DWMWA_CAPTION_BUTTON_BOUNDS = 5;
        public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

        public const int GA_ROOT = 2;
        public const int HWND_TOP = 0;
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint SWP_SHOWWINDOW = 0x0040;

        public delegate void WinEventDelegate(
            IntPtr hWinEventHook,
            uint eventType,
            IntPtr hwnd,
            int idObject,
            int idChild,
            uint dwEventThread,
            uint dwmsEventTime);

        [DllImport("user32.dll")]
        public static extern IntPtr SetWinEventHook(
            uint eventMin,
            uint eventMax,
            IntPtr hmodWinEventProc,
            WinEventDelegate lpfnWinEventProc,
            uint idProcess,
            uint idThread,
            uint dwFlags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        public static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        public static extern IntPtr GetAncestor(IntPtr hwnd, int gaFlags);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(
            IntPtr hWnd,
            IntPtr hWndInsertAfter,
            int x,
            int y,
            int cx,
            int cy,
            uint uFlags);

        [DllImport("dwmapi.dll")]
        public static extern int DwmGetWindowAttribute(
            IntPtr hwnd,
            int dwAttribute,
            out RECT pvAttribute,
            int cbAttribute);

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;

            public int Width => Right - Left;
            public int Height => Bottom - Top;
            public bool IsEmpty => Right <= Left || Bottom <= Top;
        }

        public static string GetWindowTitle(IntPtr hwnd)
        {
            var buffer = new StringBuilder(512);
            GetWindowText(hwnd, buffer, buffer.Capacity);
            return buffer.ToString();
        }

        public static string GetWindowClass(IntPtr hwnd)
        {
            var buffer = new StringBuilder(256);
            GetClassName(hwnd, buffer, buffer.Capacity);
            return buffer.ToString();
        }
    }
}
