using System;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;
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
                LogSolidWorksVersion(ThisSW);

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

        /// <summary>
        /// 用 IDispatch 读 RevisionNumber，避免绑定某一年份的 ISldWorks。
        /// 主版本：26=2018 … 33=2025。
        /// </summary>
        private static void LogSolidWorksVersion(object thisSw)
        {
            if (thisSw == null)
            {
                AddinLog.Info("ConnectToSW ThisSW 为空");
                return;
            }

            try
            {
                object raw = thisSw.GetType().InvokeMember(
                    "RevisionNumber",
                    BindingFlags.GetProperty | BindingFlags.InvokeMethod
                    | BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase,
                    null,
                    thisSw,
                    null);
                string text = Convert.ToString(raw, CultureInfo.InvariantCulture) ?? string.Empty;
                int major = 0;
                int dot = text.IndexOf('.');
                string head = dot >= 0 ? text.Substring(0, dot) : text;
                int.TryParse(head, NumberStyles.Integer, CultureInfo.InvariantCulture, out major);
                string year = major >= 26 && major <= 33
                    ? (1992 + major).ToString(CultureInfo.InvariantCulture)
                    : "未知";
                AddinLog.Info("SOLIDWORKS RevisionNumber=" + text + " 对应年份=" + year
                    + (major >= 26 && major <= 33 ? "（已覆盖 2018-2025）" : "（未在 2018-2025 范围内，仍尝试加载）"));
            }
            catch (Exception ex)
            {
                AddinLog.Info("读取 SOLIDWORKS 版本失败，仍继续加载: " + ex.Message);
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
