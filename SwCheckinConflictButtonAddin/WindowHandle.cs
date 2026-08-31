using System;
using System.Windows.Forms;

namespace SwCheckinConflictButtonAddin
{
    internal sealed class WindowHandle : IWin32Window
    {
        public WindowHandle(IntPtr handle)
        {
            Handle = handle;
        }

        public IntPtr Handle { get; }
    }
}
