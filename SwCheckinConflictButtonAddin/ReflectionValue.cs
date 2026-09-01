using System;
using System.Collections;
using System.Reflection;

namespace SwCheckinConflictButtonAddin
{
    internal static class ReflectionValue
    {
        public static object Get(object target, params string[] names)
        {
            if (target == null || names == null)
            {
                return null;
            }

            foreach (string name in names)
            {
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                object value = GetSingle(target, name);
                if (value != null && !(value is string && string.IsNullOrWhiteSpace((string)value)))
                {
                    return value;
                }
            }

            return null;
        }

        public static string GetString(object target, params string[] names)
        {
            object value = Get(target, names);
            return value == null ? string.Empty : Convert.ToString(value);
        }

        public static string FormatPrivilege(object value)
        {
            if (value == null)
            {
                return "未知";
            }

            if (value is bool flag)
            {
                return flag ? "有" : "无";
            }

            string text = Convert.ToString(value);
            if (string.IsNullOrWhiteSpace(text))
            {
                return "未知";
            }

            text = text.Trim();
            if (text == "1" || text.Equals("true", StringComparison.OrdinalIgnoreCase)
                || text == "Y" || text == "是" || text == "有" || text == "允许")
            {
                return "有";
            }

            if (text == "0" || text.Equals("false", StringComparison.OrdinalIgnoreCase)
                || text == "N" || text == "否" || text == "无" || text == "拒绝")
            {
                return "无";
            }

            return text;
        }

        private static object GetSingle(object target, string name)
        {
            Type type = target.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.IgnoreCase;

            PropertyInfo property = type.GetProperty(name, flags);
            if (property != null && property.GetIndexParameters().Length == 0)
            {
                try
                {
                    return property.GetValue(target, null);
                }
                catch
                {
                    return null;
                }
            }

            FieldInfo field = type.GetField(name, flags);
            if (field != null)
            {
                try
                {
                    return field.GetValue(target);
                }
                catch
                {
                    return null;
                }
            }

            return null;
        }

        public static string DescribeObject(object target, int maxProps)
        {
            if (target == null)
            {
                return "null";
            }

            if (target is string || target.GetType().IsPrimitive)
            {
                return Convert.ToString(target);
            }

            var parts = new System.Collections.Generic.List<string>();
            foreach (PropertyInfo property in target.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (property.GetIndexParameters().Length != 0 || parts.Count >= maxProps)
                {
                    continue;
                }

                try
                {
                    object value = property.GetValue(target, null);
                    if (value is IEnumerable && !(value is string))
                    {
                        parts.Add(property.Name + "=[...]");
                    }
                    else
                    {
                        string text = value == null ? "null" : Convert.ToString(value);
                        if (text != null && text.Length > 80)
                        {
                            text = text.Substring(0, 80) + "...";
                        }

                        parts.Add(property.Name + "=" + text);
                    }
                }
                catch
                {
                    parts.Add(property.Name + "=<err>");
                }
            }

            return target.GetType().FullName + " { " + string.Join("; ", parts.ToArray()) + " }";
        }
    }
}
