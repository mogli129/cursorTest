using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace SwCheckinConflictButtonAddin
{
    /// <summary>
    /// 自定义按钮：读取冲突列表中的 CAD 文档并展示权限。
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
                // 对方程序集不可见时 FromHandle 可能失败
            }

            if (hostForm == null || hostForm.IsDisposed)
            {
                HcDialog.Show("无法获取冲突窗口，请重试。", AddinOptions.ButtonText, HcMessageKind.Warning);
                return;
            }

            if (hostForm.InvokeRequired)
            {
                hostForm.BeginInvoke(new Action(() => OnClick(targetWindow)));
                return;
            }

            try
            {
                List<CadPermissionRow> rows = ConflictFormReader.ReadCadRows(hostForm);
                if (rows.Count == 0)
                {
                    HcDialog.Show(hostForm,
                        "没有识别到冲突列表中的 CAD 文档。\n可查看 %TEMP%\\SwCheckinConflictButtonAddin.log。",
                        AddinOptions.ButtonText,
                        HcMessageKind.Info);
                    return;
                }

                WpfApp.Ensure();
                var dialog = new CadPermissionWindow(hostForm, rows);
                dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                AddinLog.Info("自定义按钮异常: " + ex);
                HcDialog.Show(hostForm, "打开权限界面失败: " + Flatten(ex), AddinOptions.ButtonText, HcMessageKind.Error);
            }
        }

        private static string Flatten(Exception ex)
        {
            if (ex == null)
            {
                return string.Empty;
            }

            if (ex.InnerException == null)
            {
                return ex.Message;
            }

            return ex.Message + " " + Flatten(ex.InnerException);
        }
    }
}
