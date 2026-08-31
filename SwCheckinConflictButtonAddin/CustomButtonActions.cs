using System;
using System.Windows.Forms;

namespace SwCheckinConflictButtonAddin
{
    /// <summary>
    /// 自定义按钮点击逻辑。按需要改这里即可，不必动钩子代码。
    /// </summary>
    public static class CustomButtonActions
    {
        public static void OnClick(IntPtr targetWindow)
        {
            Form hostForm = null;
            try
            {
                hostForm = Control.FromHandle(targetWindow) as Form;
            }
            catch
            {
                // 对方程序集不可见时 FromHandle 可能失败，忽略
            }

            IWin32Window owner = hostForm != null
                ? (IWin32Window)hostForm
                : new WindowHandle(targetWindow);

            MessageBox.Show(
                owner,
                "已拦截到「" + AddinOptions.TargetWindowTitle + "」窗口。\n\n"
                + "请在 CustomButtonActions.OnClick 中实现你的业务逻辑。",
                AddinOptions.ButtonText,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
    }
}
