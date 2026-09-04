using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using WinForms = System.Windows.Forms;

namespace SwCheckinConflictButtonAddin
{
    internal enum HcMessageKind
    {
        Info,
        Warning,
        Error
    }

    internal partial class HcMessageWindow : Window
    {
        public HcMessageWindow(string message, string title, HcMessageKind kind)
        {
            InitializeComponent();
            Title = string.IsNullOrWhiteSpace(title) ? AddinOptions.ButtonText : title;
            MessageText.Text = message ?? string.Empty;
            ApplyKind(kind);
        }

        private void ApplyKind(HcMessageKind kind)
        {
            Color color;
            string glyph;
            switch (kind)
            {
                case HcMessageKind.Warning:
                    color = Color.FromRgb(0xE6, 0xA2, 0x3C);
                    glyph = "!";
                    break;
                case HcMessageKind.Info:
                    color = Color.FromRgb(0x40, 0x9E, 0xFF);
                    glyph = "i";
                    break;
                default:
                    color = Color.FromRgb(0xF5, 0x6C, 0x6C);
                    glyph = "!";
                    break;
            }

            KindMark.Background = new SolidColorBrush(color);
            KindGlyph.Text = glyph;
        }

        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape || e.Key == Key.Enter)
            {
                e.Handled = true;
                DialogResult = true;
                Close();
            }
        }
    }

    internal static class HcDialog
    {
        public static void Show(string message, string title, HcMessageKind kind)
        {
            Show(null, null, message, title, kind);
        }

        public static void Show(Window wpfOwner, string message, string title, HcMessageKind kind)
        {
            Show(wpfOwner, null, message, title, kind);
        }

        public static void Show(WinForms.IWin32Window winFormsOwner, string message, string title, HcMessageKind kind)
        {
            var form = winFormsOwner as WinForms.Form;
            Show(null, form, message, title, kind);
        }

        public static void Error(Window wpfOwner, string message, string title)
        {
            Show(wpfOwner, null, message, title, HcMessageKind.Error);
        }

        public static void Warning(Window wpfOwner, string message, string title)
        {
            Show(wpfOwner, null, message, title, HcMessageKind.Warning);
        }

        private static void Show(
            Window wpfOwner,
            WinForms.Form winFormsOwner,
            string message,
            string title,
            HcMessageKind kind)
        {
            try
            {
                WpfApp.Ensure();
                var dialog = new HcMessageWindow(message, title, kind);
                if (wpfOwner != null)
                {
                    dialog.Owner = wpfOwner;
                }
                else if (winFormsOwner != null && winFormsOwner.IsHandleCreated && !winFormsOwner.IsDisposed)
                {
                    new WindowInteropHelper(dialog).Owner = winFormsOwner.Handle;
                }
                else
                {
                    dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                }

                WinForms.Integration.ElementHost.EnableModelessKeyboardInterop(dialog);
                dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                AddinLog.Info("HcDialog 失败，回退系统弹窗: " + ex);
                WinForms.MessageBox.Show(
                    message,
                    string.IsNullOrWhiteSpace(title) ? AddinOptions.ButtonText : title,
                    WinForms.MessageBoxButtons.OK,
                    ToWinFormsIcon(kind));
            }
        }

        private static WinForms.MessageBoxIcon ToWinFormsIcon(HcMessageKind kind)
        {
            switch (kind)
            {
                case HcMessageKind.Warning:
                    return WinForms.MessageBoxIcon.Warning;
                case HcMessageKind.Info:
                    return WinForms.MessageBoxIcon.Information;
                default:
                    return WinForms.MessageBoxIcon.Error;
            }
        }
    }
}
