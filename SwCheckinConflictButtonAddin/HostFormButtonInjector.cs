using System;
using System.Drawing;
using System.Windows.Forms;

namespace SwCheckinConflictButtonAddin
{
    /// <summary>
    /// 把按钮加到目标 Form.Controls。跨线程只用 BeginInvoke，避免关闭弹窗时和 SW 主线程死锁。
    /// </summary>
    internal static class HostFormButtonInjector
    {
        public static bool TryEnsureButton(IntPtr hwnd)
        {
            try
            {
                Form form = TryGetForm(hwnd);
                if (form == null || form.IsDisposed || form.Disposing || !form.IsHandleCreated)
                {
                    return false;
                }

                if (form.InvokeRequired)
                {
                    form.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            InjectOnFormThread(form, hwnd);
                        }
                        catch (Exception ex)
                        {
                            AddinLog.Info("窗体线程注入失败: " + ex.Message);
                        }
                    }));
                    return true;
                }

                return InjectOnFormThread(form, hwnd);
            }
            catch (Exception ex)
            {
                AddinLog.Info("注入失败: " + ex.Message);
                return false;
            }
        }

        public static void Remove(IntPtr hwnd)
        {
            try
            {
                Form form = TryGetForm(hwnd);
                if (form == null || form.IsDisposed || form.Disposing || !form.IsHandleCreated)
                {
                    return;
                }

                if (form.InvokeRequired)
                {
                    form.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            RemoveOnFormThread(form);
                        }
                        catch (Exception ex)
                        {
                            AddinLog.Info("窗体线程移除按钮失败: " + ex.Message);
                        }
                    }));
                    return;
                }

                RemoveOnFormThread(form);
            }
            catch (Exception ex)
            {
                AddinLog.Info("移除注入按钮失败: " + ex.Message);
            }
        }

        private static bool InjectOnFormThread(Form form, IntPtr hwnd)
        {
            if (form.IsDisposed || form.Disposing)
            {
                return false;
            }

            Button existing = FindInjectedButton(form);
            if (existing != null)
            {
                PositionButton(form, existing);
                existing.BringToFront();
                existing.Visible = true;
                return true;
            }

            Button button = CreateButton(hwnd);
            PositionButton(form, button);
            form.Controls.Add(button);
            button.BringToFront();
            AddinLog.Info("已注入 Controls, form=" + form.GetType().FullName
                + " thread=" + System.Threading.Thread.CurrentThread.ManagedThreadId);
            return true;
        }

        private static void RemoveOnFormThread(Form form)
        {
            if (form.IsDisposed || form.Disposing)
            {
                return;
            }

            Button button = FindInjectedButton(form);
            if (button == null)
            {
                return;
            }

            form.Controls.Remove(button);
            button.Dispose();
        }

        private static Form TryGetForm(IntPtr hwnd)
        {
            try
            {
                return Control.FromHandle(hwnd) as Form;
            }
            catch (Exception ex)
            {
                AddinLog.Info("Control.FromHandle 失败: " + ex.Message);
                return null;
            }
        }

        private static Button FindInjectedButton(Form form)
        {
            foreach (Control control in form.Controls)
            {
                if (Equals(control.Tag, AddinOptions.ButtonTag) && control is Button button)
                {
                    return button;
                }
            }

            return null;
        }

        private static Button CreateButton(IntPtr hwnd)
        {
            var button = new Button
            {
                Text = AddinOptions.ButtonText,
                Tag = AddinOptions.ButtonTag,
                Size = new Size(AddinOptions.ButtonWidth, AddinOptions.ButtonHeight),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.White,
                BackColor = Color.FromArgb(0, 120, 215),
                Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular),
                Cursor = Cursors.Hand,
                TabStop = false
            };
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(16, 110, 190);
            button.Click += (s, e) =>
            {
                try
                {
                    CustomButtonActions.OnClick(hwnd);
                }
                catch (Exception ex)
                {
                    AddinLog.Info("按钮点击异常: " + ex);
                    HcDialog.Show(button.FindForm(),
                        "自定义按钮执行失败: " + ex.Message,
                        AddinOptions.ButtonText,
                        HcMessageKind.Error);
                }
            };
            return button;
        }

        private static void PositionButton(Form form, Button button)
        {
            int x = form.ClientSize.Width - button.Width - 8;
            if (x < 8)
            {
                x = 8;
            }

            button.Location = new Point(x, 8);
        }
    }
}
