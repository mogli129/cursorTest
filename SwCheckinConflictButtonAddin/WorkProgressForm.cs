using System;
using System.Drawing;
using System.Windows.Forms;

namespace SwCheckinConflictButtonAddin
{
    internal sealed class WorkProgressForm : AntdUI.Window
    {
        private readonly AntdUI.Label _label = new AntdUI.Label();
        private readonly AntdUI.Progress _bar = new AntdUI.Progress();

        public WorkProgressForm()
        {
            AntdUiApp.Ensure();
            Text = "正在加载权限数据";
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ControlBox = false;
            ShowInTaskbar = false;
            Resizable = false;
            Width = 460;
            Height = 168;
            Font = new Font("Microsoft YaHei UI", 9f);

            var header = new AntdUI.PageHeader
            {
                Dock = DockStyle.Top,
                Height = 40,
                Text = Text,
                ShowButton = false,
                UseTextBold = true,
                DividerShow = true
            };

            _label.SetBounds(20, 56, 420, 36);
            _label.Text = "正在准备…";

            _bar.SetBounds(20, 100, 420, 22);
            _bar.Shape = AntdUI.TShapeProgress.Round;
            _bar.Loading = true;
            _bar.LoadingFull = true;
            _bar.Value = 0f;
            _bar.UseSystemText = false;
            _bar.Text = string.Empty;

            Controls.Add(_label);
            Controls.Add(_bar);
            Controls.Add(header);
        }

        public void UpdateProgress(string message, int current, int maximum)
        {
            if (IsDisposed)
            {
                return;
            }

            if (!string.IsNullOrEmpty(message))
            {
                _label.Text = message;
            }

            if (maximum <= 0)
            {
                _bar.Loading = true;
                _bar.LoadingFull = true;
                _bar.Value = 0f;
                return;
            }

            _bar.Loading = false;
            _bar.LoadingFull = false;
            float value = (float)current / Math.Max(maximum, 1);
            if (value < 0f)
            {
                value = 0f;
            }

            if (value > 1f)
            {
                value = 1f;
            }

            _bar.Value = value;
        }
    }
}
