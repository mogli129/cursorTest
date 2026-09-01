using System;
using System.Collections.Generic;
using System.Reflection;

namespace SwCheckinConflictButtonAddin
{
    /// <summary>
    /// 在已加载的 PDM/PLM 程序集中查找权限服务，按文档/文件夹对象探测读写权限。
    /// 本环境没有 TS2024 / 后端源码，因此按常见方法名约定探测。
    /// </summary>
    internal static class PermissionServiceProbe
    {
        private static readonly object Gate = new object();
        private static bool _scanned;
        private static readonly List<object> Services = new List<object>();

        public static void Prepare(object sample)
        {
            EnsureScan(sample);
        }

        public static string TryResolve(object target, bool read)
        {
            if (target == null)
            {
                return null;
            }

            EnsureScan(target);
            string fromTarget = TryMethodsOn(target, target, read);
            if (fromTarget != null)
            {
                return fromTarget;
            }

            foreach (object service in Services)
            {
                string value = TryMethodsOn(service, target, read);
                if (value != null)
                {
                    return value;
                }
            }

            return null;
        }

        private static void EnsureScan(object sample)
        {
            lock (Gate)
            {
                if (_scanned)
                {
                    return;
                }

                _scanned = true;
                var assemblies = new List<Assembly>();
                if (sample != null)
                {
                    assemblies.Add(sample.GetType().Assembly);
                }

                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (!assemblies.Contains(assembly) && IsInterestingAssembly(assembly))
                    {
                        assemblies.Add(assembly);
                    }
                }

                foreach (Assembly assembly in assemblies)
                {
                    ScanAssembly(assembly);
                }

                AddinLog.Info("权限服务扫描完成: assemblies=" + assemblies.Count
                    + " services=" + Services.Count);
            }
        }

        private static bool IsInterestingAssembly(Assembly assembly)
        {
            string name = assembly.GetName().Name ?? string.Empty;
            if (name.StartsWith("System", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("mscorlib", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("netstandard", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("SolidWorks", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("Presentation", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("WindowsBase", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("SwCheckin", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string[] keys =
            {
                "Pdm", "Plm", "Cad", "Doc", "Vault", "Folder", "Product", "Project",
                "Permission", "Privilege", "Access", "Acl", "Security", "Ts", "Team",
                "Km", "Inte", "Client", "Business", "Domain", "Library"
            };
            foreach (string key in keys)
            {
                if (name.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static void ScanAssembly(Assembly assembly)
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types;
            }
            catch
            {
                return;
            }

            int found = 0;
            foreach (Type type in types)
            {
                if (type == null || !type.IsClass || type.IsAbstract)
                {
                    continue;
                }

                string typeName = type.Name;
                if (typeName.IndexOf("Permission", StringComparison.OrdinalIgnoreCase) < 0
                    && typeName.IndexOf("Privilege", StringComparison.OrdinalIgnoreCase) < 0
                    && typeName.IndexOf("AccessControl", StringComparison.OrdinalIgnoreCase) < 0
                    && typeName.IndexOf("Acl", StringComparison.OrdinalIgnoreCase) < 0
                    && typeName.IndexOf("SecurityService", StringComparison.OrdinalIgnoreCase) < 0
                    && typeName.IndexOf("AuthService", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                object instance = TryGetInstance(type);
                if (instance == null)
                {
                    continue;
                }

                Services.Add(instance);
                found++;
                if (found <= 12)
                {
                    AddinLog.Info("权限服务 " + type.FullName + " from " + assembly.GetName().Name);
                }

                if (Services.Count >= 24)
                {
                    return;
                }
            }
        }

        private static object TryGetInstance(Type type)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            foreach (string name in new[] { "Instance", "Current", "Default", "Singleton" })
            {
                PropertyInfo property = type.GetProperty(name, flags);
                if (property != null)
                {
                    try
                    {
                        object value = property.GetValue(null, null);
                        if (value != null)
                        {
                            return value;
                        }
                    }
                    catch
                    {
                        // ignore
                    }
                }

                FieldInfo field = type.GetField(name, flags);
                if (field != null)
                {
                    try
                    {
                        object value = field.GetValue(null);
                        if (value != null)
                        {
                            return value;
                        }
                    }
                    catch
                    {
                        // ignore
                    }
                }
            }

            try
            {
                ConstructorInfo ctor = type.GetConstructor(Type.EmptyTypes);
                if (ctor != null)
                {
                    return ctor.Invoke(null);
                }
            }
            catch
            {
                // ignore
            }

            return null;
        }

        private static string TryMethodsOn(object host, object target, bool read)
        {
            if (host == null)
            {
                return null;
            }

            string[] tokens = read
                ? new[] { "Read", "View", "Download", "读取", "查看" }
                : new[] { "Write", "Modify", "Edit", "Update", "修改", "写入", "更新" };

            MethodInfo[] methods;
            try
            {
                methods = host.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public);
            }
            catch
            {
                return null;
            }

            int tried = 0;
            foreach (MethodInfo method in methods)
            {
                if (tried > 40)
                {
                    break;
                }

                if (method.ContainsGenericParameters)
                {
                    continue;
                }

                string name = method.Name;
                if (name.StartsWith("set_", StringComparison.Ordinal) || name.StartsWith("get_", StringComparison.Ordinal)
                    || name == "ToString" || name == "GetHashCode" || name == "Equals" || name == "GetType")
                {
                    continue;
                }

                bool nameLooks = ContainsAny(name, "Can", "Has", "Check", "Get")
                    && ContainsAny(name, tokens);
                if (!nameLooks && !(ContainsAny(name, "Permission", "Privilege", "Access") && ContainsAny(name, tokens)))
                {
                    continue;
                }

                if (method.ReturnType != typeof(bool) && method.ReturnType != typeof(string)
                    && method.ReturnType != typeof(int) && !method.ReturnType.IsEnum)
                {
                    continue;
                }

                ParameterInfo[] parameters = method.GetParameters();
                object[] args = BuildArgs(parameters, host == target ? null : target, read, tokens);
                if (args == null && parameters.Length > 0)
                {
                    continue;
                }

                tried++;
                try
                {
                    object result = method.Invoke(host, args ?? new object[0]);
                    string formatted = ReflectionValue.FormatPrivilege(result);
                    if (formatted == "有" || formatted == "无")
                    {
                        return formatted;
                    }
                }
                catch
                {
                    // ignore
                }
            }

            return null;
        }

        private static object[] BuildArgs(ParameterInfo[] parameters, object target, bool read, string[] tokens)
        {
            if (parameters.Length == 0)
            {
                return new object[0];
            }

            if (parameters.Length > 3)
            {
                return null;
            }

            var args = new object[parameters.Length];
            bool usedTarget = false;
            for (int i = 0; i < parameters.Length; i++)
            {
                Type type = parameters[i].ParameterType;
                if (target != null && !usedTarget && (type.IsInstanceOfType(target) || type == typeof(object)))
                {
                    args[i] = target;
                    usedTarget = true;
                    continue;
                }

                if (type == typeof(string))
                {
                    string id = target == null
                        ? null
                        : FirstNonEmpty(
                            ReflectionValue.GetString(target, "Id", "Oid", "ObjectId", "DocId", "FolderId", "InnerId"),
                            null);
                    string paramName = parameters[i].Name ?? string.Empty;
                    if (paramName.IndexOf("id", StringComparison.OrdinalIgnoreCase) >= 0
                        || paramName.IndexOf("oid", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        args[i] = string.IsNullOrEmpty(id) ? Convert.ToString(target) : id;
                    }
                    else
                    {
                        args[i] = tokens[0];
                    }

                    continue;
                }

                if (type == typeof(int) || type.IsEnum)
                {
                    args[i] = read ? 1 : 2;
                    continue;
                }

                if (type == typeof(bool))
                {
                    args[i] = read;
                    continue;
                }

                if (target != null && type.IsInstanceOfType(target))
                {
                    args[i] = target;
                    usedTarget = true;
                    continue;
                }

                return null;
            }

            return args;
        }

        private static bool ContainsAny(string text, params string[] tokens)
        {
            foreach (string token in tokens)
            {
                if (text.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static string FirstNonEmpty(string left, string right)
        {
            if (!string.IsNullOrWhiteSpace(left))
            {
                return left;
            }

            return right ?? string.Empty;
        }
    }
}
