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
                MessageBox.Show("无法获取冲突窗口，请重试。", AddinOptions.ButtonText,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                Cursor.Current = Cursors.WaitCursor;
                List<CadPermissionRow> rows = ConflictFormReader.ReadCadRows(hostForm);
                if (rows.Count == 0)
                {
                    MessageBox.Show(
                        hostForm,
                        "没有识别到冲突列表中的 CAD 文档。\n可查看 %TEMP%\\SwCheckinConflictButtonAddin.log 中的表格列、绑定对象和权限服务信息。",
                        AddinOptions.ButtonText,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                using (var dialog = new CadPermissionForm(rows))
                {
                    dialog.ShowDialog(hostForm);
                }
            }
            catch (Exception ex)
            {
                AddinLog.Info("自定义按钮异常: " + ex);
                MessageBox.Show(hostForm, "打开权限界面失败: " + ex.Message,
                    AddinOptions.ButtonText, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Cursor.Current = Cursors.Default;
            }
        }
    }
}
