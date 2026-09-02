using System.Windows.Forms;

namespace SwCheckinConflictButtonAddin
{
    internal static class AntdUiApp
    {
        private static bool _ready;

        public static void Ensure()
        {
            if (_ready)
            {
                return;
            }

            AntdUI.Config.IsLight = true;
            AntdUI.Config.ShowInWindow = true;
            _ready = true;
        }

        public static void Alert(IWin32Window owner, string title, string text, AntdUI.TType type)
        {
            Ensure();
            var form = owner as Form;
            if (form != null && !form.IsDisposed)
            {
                AntdUI.Modal.open(form, title, text, type);
                return;
            }

            MessageBox.Show(owner, text, title, MessageBoxButtons.OK, MapIcon(type));
        }

        private static MessageBoxIcon MapIcon(AntdUI.TType type)
        {
            switch (type)
            {
                case AntdUI.TType.Error:
                    return MessageBoxIcon.Error;
                case AntdUI.TType.Warn:
                    return MessageBoxIcon.Warning;
                case AntdUI.TType.Success:
                    return MessageBoxIcon.Information;
                default:
                    return MessageBoxIcon.Information;
            }
        }
    }
}
