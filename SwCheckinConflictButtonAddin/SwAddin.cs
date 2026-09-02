using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swpublished;

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

        private ISldWorks _swApp;
        private ConflictWindowWatcher _watcher;
        private Timer _deferredStart;

        public SwAddin()
        {
            AddinLog.Info(
                "SwAddin ctor bits=" + (IntPtr.Size * 8)
                + " dll=" + typeof(SwAddin).Assembly.Location);
            WpfApp.RegisterAssemblyResolve();
        }

        public bool ConnectToSW(object ThisSW, int Cookie)
        {
            try
            {
                AddinLog.Info("ConnectToSW begin cookie=" + Cookie);
                WpfApp.RegisterAssemblyResolve();
                _swApp = ThisSW as ISldWorks;
                if (_swApp == null)
                {
                    AddinLog.Info("ISldWorks 转换失败，仍继续加载");
                }

                _deferredStart = new Timer { Interval = 1500 };
                _deferredStart.Tick += OnDeferredStart;
                _deferredStart.Start();

                AddinLog.Info("ConnectToSW ok, watcher will start shortly");
                return true;
            }
            catch (Exception ex)
            {
                AddinLog.Info("ConnectToSW exception: " + ex);
                return false;
            }
        }

        public bool DisconnectFromSW()
        {
            AddinLog.Info("DisconnectFromSW");
            try
            {
                if (_deferredStart != null)
                {
                    _deferredStart.Stop();
                    _deferredStart.Tick -= OnDeferredStart;
                    _deferredStart.Dispose();
                    _deferredStart = null;
                }

                if (_watcher != null)
                {
                    _watcher.Dispose();
                    _watcher = null;
                }

                if (_swApp != null)
                {
                    Marshal.ReleaseComObject(_swApp);
                    _swApp = null;
                }
            }
            catch (Exception ex)
            {
                AddinLog.Info("DisconnectFromSW exception: " + ex);
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            return true;
        }

        private void OnDeferredStart(object sender, EventArgs e)
        {
            try
            {
                if (_deferredStart != null)
                {
                    _deferredStart.Stop();
                    _deferredStart.Tick -= OnDeferredStart;
                    _deferredStart.Dispose();
                    _deferredStart = null;
                }

                if (_watcher != null)
                {
                    return;
                }

                _watcher = new ConflictWindowWatcher();
                _watcher.Start();
                AddinLog.Info("watcher started");
            }
            catch (Exception ex)
            {
                AddinLog.Info("启动窗口监视失败（插件仍保持加载）: " + ex);
            }
        }

        [ComRegisterFunction]
        public static void RegisterFunction(Type t)
        {
            string title = GetDisplayName(t);
            string description = GetDescription(t);
            string guid = t.GUID.ToString("B");
            AddinLog.Info("RegisterFunction " + guid + " dll=" + t.Assembly.Location);

            WriteAddinKey(Registry.CurrentUser, guid, title, description);
            try
            {
                WriteAddinKey(Registry.LocalMachine, guid, title, description);
            }
            catch (Exception ex)
            {
                AddinLog.Info("写入 HKLM Addins 失败: " + ex.Message);
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

                key.SetValue(null, 1);
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
            var att = (DisplayNameAttribute)System.Attribute.GetCustomAttribute(t, typeof(DisplayNameAttribute));
            return att != null ? att.DisplayName : t.Name;
        }

        private static string GetDescription(Type t)
        {
            var att = (DescriptionAttribute)System.Attribute.GetCustomAttribute(t, typeof(DescriptionAttribute));
            return att != null ? att.Description : t.FullName;
        }
    }
}
