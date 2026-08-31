using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SwCheckinConflictButtonAddin
{
    [ComVisible(true)]
    [Guid("C8E4D514-7B21-4A8F-9C33-1E6A0B4D5148")]
    [ProgId("SwCheckinConflictButtonAddin.SwAddin")]
    [ClassInterface(ClassInterfaceType.None)]
    [ComDefaultInterface(typeof(ISwAddin))]
    [DisplayName("检入冲突窗口按钮")]
    [Description("在“检入文档冲突处理”窗口右上角添加自定义按钮")]
    public class SwAddin : ISwAddin
    {
        private const string AddinKeyTemplate = @"SOFTWARE\SolidWorks\Addins\{{{0}}}";
        private const string StartupKeyTemplate = @"Software\SolidWorks\AddInsStartup\{{{0}}}";

        private object _swApp;
        private ConflictWindowWatcher _watcher;

        public bool ConnectToSW(object thisSw, int cookie)
        {
            _swApp = thisSw;
            AddinLog.Info("ConnectToSW cookie=" + cookie);

            try
            {
                Application.EnableVisualStyles();
            }
            catch
            {
                // SW 可能已经启用过视觉样式
            }

            _watcher = new ConflictWindowWatcher();
            _watcher.Start();
            return true;
        }

        public bool DisconnectFromSW()
        {
            AddinLog.Info("DisconnectFromSW");
            if (_watcher != null)
            {
                _watcher.Dispose();
                _watcher = null;
            }

            if (_swApp != null && Marshal.IsComObject(_swApp))
            {
                Marshal.ReleaseComObject(_swApp);
            }

            _swApp = null;
            GC.Collect();
            GC.WaitForPendingFinalizers();
            return true;
        }

        [ComRegisterFunction]
        public static void RegisterFunction(Type t)
        {
            string title = GetDisplayName(t);
            string description = GetDescription(t);
            string guid = t.GUID.ToString("B");

            WriteAddinKey(Registry.CurrentUser, guid, title, description);
            try
            {
                WriteAddinKey(Registry.LocalMachine, guid, title, description);
            }
            catch (Exception ex)
            {
                AddinLog.Info("写入 HKLM Addins 失败（可用 HKCU）: " + ex.Message);
            }

            using (var startup = Registry.CurrentUser.CreateSubKey(string.Format(StartupKeyTemplate, t.GUID)))
            {
                if (startup != null)
                {
                    startup.SetValue(null, 1, RegistryValueKind.DWord);
                }
            }
        }

        [ComUnregisterFunction]
        public static void UnregisterFunction(Type t)
        {
            TryDeleteSubKey(Registry.CurrentUser, string.Format(AddinKeyTemplate, t.GUID));
            TryDeleteSubKey(Registry.CurrentUser, string.Format(StartupKeyTemplate, t.GUID));
            TryDeleteSubKey(Registry.LocalMachine, string.Format(AddinKeyTemplate, t.GUID));
        }

        private static void WriteAddinKey(RegistryKey hive, string guid, string title, string description)
        {
            using (var key = hive.CreateSubKey(@"SOFTWARE\SolidWorks\Addins\" + guid))
            {
                if (key == null)
                {
                    return;
                }

                key.SetValue(null, 0);
                key.SetValue("Title", title);
                key.SetValue("Description", description);
            }
        }

        private static void TryDeleteSubKey(RegistryKey hive, string path)
        {
            try
            {
                hive.DeleteSubKeyTree(path, false);
            }
            catch
            {
            }
        }

        private static string GetDisplayName(Type t)
        {
            var att = (DisplayNameAttribute)Attribute.GetCustomAttribute(t, typeof(DisplayNameAttribute));
            return att != null ? att.DisplayName : t.Name;
        }

        private static string GetDescription(Type t)
        {
            var att = (DescriptionAttribute)Attribute.GetCustomAttribute(t, typeof(DescriptionAttribute));
            return att != null ? att.Description : t.FullName;
        }
    }
}
