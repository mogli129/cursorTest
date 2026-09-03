using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SwCheckinConflictButtonAddin
{
    /// <summary>
    /// 贴在目标窗口标题栏右上角、关闭按钮左侧的无焦点工具窗。
    /// </summary>
    internal sealed class CaptionButtonOverlay : Form
    {
        private readonly IntPtr _targetHwnd;
        private readonly Button _button;

        public CaptionButtonOverlay(IntPtr targetHwnd)
        {
            _targetHwnd = targetHwnd;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(AddinOptions.ButtonWidth, AddinOptions.ButtonHeight);
            TopMost = false;
            BackColor = Color.FromArgb(0, 120, 215);
            AutoScaleMode = AutoScaleMode.None;

            _button = new Button
            {
                Text = AddinOptions.ButtonText,
                Dock = DockStyle.Fill,
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.White,
                BackColor = Color.FromArgb(0, 120, 215),
                Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular),
                Cursor = Cursors.Hand,
                TabStop = false
            };
            _button.FlatAppearance.BorderSize = 0;
            _button.FlatAppearance.MouseOverBackColor = Color.FromArgb(16, 110, 190);
            _button.Click += OnButtonClick;
            Controls.Add(_button);
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                const int WsExNoActivate = 0x08000000;
                const int WsExToolWindow = 0x00000080;
                var cp = base.CreateParams;
                cp.ExStyle |= WsExNoActivate | WsExToolWindow;
                return cp;
            }
        }

        public IntPtr TargetHwnd => _targetHwnd;

        public void Attach()
        {
            if (!NativeMethods.IsWindow(_targetHwnd))
            {
                return;
            }

            UpdateLocation();
            if (!Visible)
            {
                Show(new WindowHandle(_targetHwnd));
                UpdateLocation();
            }

            NativeMethods.SetWindowPos(
                Handle,
                new IntPtr(NativeMethods.HWND_TOP),
                0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
        }

        public void Reposition()
        {
            if (!NativeMethods.IsWindow(_targetHwnd) || NativeMethods.IsIconic(_targetHwnd)
                || !NativeMethods.IsWindowVisible(_targetHwnd))
            {
                if (Visible)
                {
                    Hide();
                }

                return;
            }

            if (!Visible)
            {
                UpdateLocation();
                Show(new WindowHandle(_targetHwnd));
            }

            UpdateLocation();
        }

        private void UpdateLocation()
        {
            if (!TryGetCaptionButtonScreenBounds(out var captionButtons))
            {
                NativeMethods.GetWindowRect(_targetHwnd, out var window);
                captionButtons = new NativeMethods.RECT
                {
                    Left = window.Right - 46,
                    Top = window.Top + 6,
                    Right = window.Right - 8,
                    Bottom = window.Top + 30
                };
            }

            int x = captionButtons.Left - Width - AddinOptions.ButtonGapFromCaptionButtons;
            int y = captionButtons.Top + (captionButtons.Height - Height) / 2;
            Location = new Point(x, y);
        }

        private bool TryGetCaptionButtonScreenBounds(out NativeMethods.RECT screenBounds)
        {
            screenBounds = default;
            int size = Marshal.SizeOf(typeof(NativeMethods.RECT));
            if (NativeMethods.DwmGetWindowAttribute(
                    _targetHwnd,
                    NativeMethods.DWMWA_CAPTION_BUTTON_BOUNDS,
                    out var relative,
                    size) != 0 || relative.IsEmpty)
            {
                return false;
            }

            if (!NativeMethods.GetWindowRect(_targetHwnd, out var window))
            {
                return false;
            }

            screenBounds = new NativeMethods.RECT
            {
                Left = window.Left + relative.Left,
                Top = window.Top + relative.Top,
                Right = window.Left + relative.Right,
                Bottom = window.Top + relative.Bottom
            };
            return !screenBounds.IsEmpty;
        }

        private void OnButtonClick(object sender, EventArgs e)
        {
            try
            {
                CustomButtonActions.OnClick(_targetHwnd);
            }
            catch (Exception ex)
            {
                AddinLog.Info("按钮点击异常: " + ex);
                HcDialog.Show(this,
                    "自定义按钮执行失败: " + ex.Message,
                    AddinOptions.ButtonText,
                    HcMessageKind.Error);
            }
        }
    }
}
