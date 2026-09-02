using System;
using System.Collections.Generic;
using System.Threading;
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
                AntdUiApp.Alert(null, AddinOptions.ButtonText, "无法获取冲突窗口，请重试。", AntdUI.TType.Warn);
                return;
            }

            try
            {
                List<CadPermissionRow> rows = ConflictFormReader.ReadCadRows(hostForm);
                if (rows.Count == 0)
                {
                    AntdUiApp.Alert(hostForm, AddinOptions.ButtonText,
                        "没有识别到冲突列表中的 CAD 文档。\n可查看 %TEMP%\\SwCheckinConflictButtonAddin.log。",
                        AntdUI.TType.Info);
                    return;
                }

                Exception error = null;
                using (var progress = new WorkProgressForm())
                {
                    progress.Shown += (s, e) =>
                    {
                        ThreadPool.QueueUserWorkItem(_ =>
                        {
                            try
                            {
                                Report(progress, "正在读取 TeamSpace 登录信息…", 0, 0);
                                TsSession session = TsSessionLocator.Resolve(hostForm);
                                if (!session.IsUsable)
                                {
                                    throw new InvalidOperationException(
                                        "未能从 TeamSpace 取到服务器地址、登录 Token 或用户 OID。请确认已登录 TS 后再试。");
                                }

                                new PlmApiClient(session).Fill(rows, (message, current, maximum) =>
                                    Report(progress, message, current, maximum));
                            }
                            catch (Exception ex)
                            {
                                error = ex;
                                AddinLog.Info("加载权限数据失败: " + ex);
                            }
                            finally
                            {
                                CloseProgress(progress);
                            }
                        });
                    };
                    progress.ShowDialog(hostForm);
                }

                if (error != null)
                {
                    AntdUiApp.Alert(hostForm, AddinOptions.ButtonText,
                        "打开权限界面失败: " + error.Message, AntdUI.TType.Error);
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
                AntdUiApp.Alert(hostForm, AddinOptions.ButtonText,
                    "打开权限界面失败: " + ex.Message, AntdUI.TType.Error);
            }
        }

        private static void Report(WorkProgressForm progress, string message, int current, int maximum)
        {
            if (progress == null || progress.IsDisposed)
            {
                return;
            }

            Action update = () =>
            {
                if (!progress.IsDisposed)
                {
                    progress.UpdateProgress(message, current, maximum);
                }
            };

            try
            {
                if (progress.IsHandleCreated && progress.InvokeRequired)
                {
                    progress.BeginInvoke(update);
                }
                else
                {
                    update();
                }
            }
            catch
            {
            }
        }

        private static void CloseProgress(WorkProgressForm progress)
        {
            if (progress == null || progress.IsDisposed)
            {
                return;
            }

            Action close = () =>
            {
                if (!progress.IsDisposed)
                {
                    progress.Close();
                }
            };

            try
            {
                if (progress.IsHandleCreated && progress.InvokeRequired)
                {
                    progress.BeginInvoke(close);
                }
                else
                {
                    close();
                }
            }
            catch
            {
            }
        }
    }
}
