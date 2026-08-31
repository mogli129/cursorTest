using System;
using System.Drawing;
using System.Windows.Forms;

namespace SwCheckinConflictButtonAddin
{
    /// <summary>
    /// 把按钮加到目标 Form.Controls，锚在客户区右上角。
    /// </summary>
    internal static class HostFormButtonInjector
    {
        public static bool TryEnsureButton(IntPtr hwnd)
        {
            Form form = TryGetForm(hwnd);
            if (form == null || form.IsDisposed)
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
            AddinLog.Info("已注入 Controls, form=" + form.GetType().FullName);
            return true;
        }

        public static void Remove(IntPtr hwnd)
        {
            Form form = TryGetForm(hwnd);
            if (form == null || form.IsDisposed)
            {
                return;
            }

            Button button = FindInjectedButton(form);
            if (button == null)
            {
                return;
            }

            try
            {
                form.Controls.Remove(button);
                button.Dispose();
            }
            catch (Exception ex)
            {
                AddinLog.Info("移除注入按钮失败: " + ex.Message);
            }
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
                    MessageBox.Show(
                        button.FindForm(),
                        "自定义按钮执行失败: " + ex.Message,
                        AddinOptions.ButtonText,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
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
