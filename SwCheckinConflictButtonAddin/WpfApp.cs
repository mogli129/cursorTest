using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;

namespace SwCheckinConflictButtonAddin
{
    internal static class WpfApp
    {
        private static bool _resolveRegistered;
        private static int _themeThreadId;

        public static void RegisterAssemblyResolve()
        {
            if (_resolveRegistered)
            {
                return;
            }

            AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
            _resolveRegistered = true;
        }

        /// <summary>
        /// Must run on the same STA thread that creates and shows the WPF window.
        /// </summary>
        public static void Ensure()
        {
            RegisterAssemblyResolve();
            EnsureHandyControlLoaded();

            Application app = Application.Current;
            if (app != null && !app.Dispatcher.CheckAccess())
            {
                AddinLog.Info("WPF Application belongs to another thread, skip app-level theme");
                return;
            }

            if (app == null)
            {
                try
                {
                    if (Application.ResourceAssembly == null)
                    {
                        Application.ResourceAssembly = typeof(WpfApp).Assembly;
                    }
                }
                catch (Exception ex)
                {
                    AddinLog.Info("Application.ResourceAssembly: " + ex.Message);
                }

                app = new Application
                {
                    ShutdownMode = ShutdownMode.OnExplicitShutdown
                };
            }
        }

        /// <summary>
        /// Reload HandyControl dictionaries only when this STA thread did not populate the cache.
        /// Clearing on every window open can hang the second ShowDialog.
        /// </summary>
        public static void PrepareHandyControlThemeOnThisThread()
        {
            int tid = Thread.CurrentThread.ManagedThreadId;
            if (_themeThreadId == tid)
            {
                return;
            }

            if (_themeThreadId != 0)
            {
                ResetHandyControlThemeCache();
            }

            _themeThreadId = tid;
        }

        private static void ResetHandyControlThemeCache()
        {
            string[] typeNames =
            {
                "HandyControl.Themes.SharedResourceDictionary, HandyControl",
                "HandyControl.Tools.SharedResourceDictionary, HandyControl"
            };

            foreach (string typeName in typeNames)
            {
                Type type = Type.GetType(typeName, false);
                if (type == null)
                {
                    continue;
                }

                object cache = null;
                PropertyInfo property = type.GetProperty("SharedDictionaries", BindingFlags.Public | BindingFlags.Static);
                if (property != null)
                {
                    cache = property.GetValue(null, null);
                }
                else
                {
                    FieldInfo field = type.GetField("SharedDictionaries", BindingFlags.Public | BindingFlags.Static);
                    if (field != null)
                    {
                        cache = field.GetValue(null);
                    }
                }

                var dictionary = cache as IDictionary;
                if (dictionary != null)
                {
                    dictionary.Clear();
                    AddinLog.Info("Cleared HandyControl SharedDictionaries on " + type.FullName);
                }
            }
        }

        private static Assembly OnAssemblyResolve(object sender, ResolveEventArgs args)
        {
            string name = new AssemblyName(args.Name).Name;
            if (string.IsNullOrEmpty(name) || name.EndsWith(".resources", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            foreach (Assembly loaded in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (string.Equals(loaded.GetName().Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return loaded;
                }
            }

            string path = Path.Combine(AddinDirectory(), name + ".dll");
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                AddinLog.Info("AssemblyResolve load " + name + " from " + path);
                return Assembly.LoadFrom(path);
            }
            catch (Exception ex)
            {
                AddinLog.Info("AssemblyResolve failed " + name + ": " + ex);
                return null;
            }
        }

        private static void EnsureHandyControlLoaded()
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (string.Equals(assembly.GetName().Name, "HandyControl", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            string path = Path.Combine(AddinDirectory(), "HandyControl.dll");
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    "未找到 HandyControl.dll。请重新运行管理员 install.bat，确认安装目录已复制该文件。",
                    path);
            }

            Assembly.LoadFrom(path);
            AddinLog.Info("Loaded HandyControl from " + path);
        }

        private static string AddinDirectory()
        {
            string location = typeof(WpfApp).Assembly.Location;
            return string.IsNullOrEmpty(location)
                ? AppDomain.CurrentDomain.BaseDirectory
                : Path.GetDirectoryName(location);
        }
    }
}
