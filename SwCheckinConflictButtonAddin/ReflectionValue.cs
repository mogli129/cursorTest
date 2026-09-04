using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
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

                object value = name.IndexOf('.') >= 0
                    ? GetDotted(target, name)
                    : GetSingle(target, name);
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

        public static bool Set(object target, string name, object value)
        {
            if (target == null || string.IsNullOrEmpty(name))
            {
                return false;
            }

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.IgnoreCase;
            try
            {
                PropertyInfo property = target.GetType().GetProperty(name, flags);
                if (property != null && property.CanWrite && property.GetIndexParameters().Length == 0)
                {
                    property.SetValue(target, value, null);
                    return true;
                }

                FieldInfo field = target.GetType().GetField(name, flags);
                if (field != null && !field.IsLiteral && !field.IsInitOnly)
                {
                    field.SetValue(target, value);
                    return true;
                }
            }
            catch (Exception ex)
            {
                AddinLog.Info("写入 " + target.GetType().Name + "." + name + " 失败: " + ex.Message);
            }

            return false;
        }

        public static object Call(object target, string methodName, params object[] args)
        {
            if (target == null || string.IsNullOrEmpty(methodName))
            {
                return null;
            }

            Type[] argTypes = args == null ? Type.EmptyTypes : Array.ConvertAll(args, a => a == null ? typeof(object) : a.GetType());
            MethodInfo method = target.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase,
                null,
                argTypes,
                null);
            if (method == null)
            {
                return null;
            }

            try
            {
                return method.Invoke(target, args);
            }
            catch
            {
                return null;
            }
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

            if (value is Enum)
            {
                string enumName = value.ToString();
                if (enumName.IndexOf("Allow", StringComparison.OrdinalIgnoreCase) >= 0
                    || enumName.IndexOf("Grant", StringComparison.OrdinalIgnoreCase) >= 0
                    || enumName.IndexOf("Read", StringComparison.OrdinalIgnoreCase) >= 0
                    || enumName.IndexOf("Write", StringComparison.OrdinalIgnoreCase) >= 0
                    || enumName.IndexOf("Modify", StringComparison.OrdinalIgnoreCase) >= 0
                    || enumName == "Yes" || enumName == "有" || enumName == "允许")
                {
                    if (enumName.IndexOf("Deny", StringComparison.OrdinalIgnoreCase) >= 0
                        || enumName.IndexOf("None", StringComparison.OrdinalIgnoreCase) >= 0
                        || enumName.IndexOf("Refuse", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return "无";
                    }

                    if (enumName.IndexOf("Allow", StringComparison.OrdinalIgnoreCase) >= 0
                        || enumName == "Yes" || enumName == "有" || enumName == "允许")
                    {
                        return "有";
                    }
                }

                if (enumName.IndexOf("Deny", StringComparison.OrdinalIgnoreCase) >= 0
                    || enumName.IndexOf("None", StringComparison.OrdinalIgnoreCase) >= 0
                    || enumName == "No" || enumName == "无" || enumName == "拒绝")
                {
                    return "无";
                }
            }

            string text = Convert.ToString(value);
            if (string.IsNullOrWhiteSpace(text))
            {
                return "未知";
            }

            text = text.Trim();
            if (text == "1" || text.Equals("true", StringComparison.OrdinalIgnoreCase)
                || text == "Y" || text == "是" || text == "有" || text == "允许"
                || text.Equals("allow", StringComparison.OrdinalIgnoreCase)
                || text.Equals("granted", StringComparison.OrdinalIgnoreCase))
            {
                return "有";
            }

            if (text == "0" || text.Equals("false", StringComparison.OrdinalIgnoreCase)
                || text == "N" || text == "否" || text == "无" || text == "拒绝"
                || text.Equals("deny", StringComparison.OrdinalIgnoreCase)
                || text.Equals("denied", StringComparison.OrdinalIgnoreCase))
            {
                return "无";
            }

            return text;
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

            if (target is DataRowView view)
            {
                return DescribeDataRow(view.Row, maxProps);
            }

            if (target is DataRow dataRow)
            {
                return DescribeDataRow(dataRow, maxProps);
            }

            var parts = new List<string>();
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

        private static string DescribeDataRow(DataRow row, int maxProps)
        {
            var parts = new List<string>();
            foreach (DataColumn column in row.Table.Columns)
            {
                if (parts.Count >= maxProps)
                {
                    break;
                }

                object value = row[column];
                string text = value == null || value == DBNull.Value ? "null" : Convert.ToString(value);
                if (text != null && text.Length > 80)
                {
                    text = text.Substring(0, 80) + "...";
                }

                parts.Add(column.ColumnName + "=" + text);
            }

            return "DataRow { " + string.Join("; ", parts.ToArray()) + " }";
        }

        private static object GetDotted(object target, string path)
        {
            object current = target;
            foreach (string part in path.Split('.'))
            {
                current = GetSingle(current, part);
                if (current == null)
                {
                    return null;
                }
            }

            return current;
        }

        private static object GetSingle(object target, string name)
        {
            if (target == null)
            {
                return null;
            }

            if (target is DataRowView view)
            {
                return GetDataColumn(view.Row, name);
            }

            if (target is DataRow row)
            {
                return GetDataColumn(row, name);
            }

            if (target is IDictionary dictionary)
            {
                if (dictionary.Contains(name))
                {
                    return dictionary[name];
                }

                foreach (DictionaryEntry entry in dictionary)
                {
                    if (Convert.ToString(entry.Key).Equals(name, StringComparison.OrdinalIgnoreCase))
                    {
                        return entry.Value;
                    }
                }
            }

            Type type = target.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.IgnoreCase;

            PropertyInfo property = type.GetProperty(name, flags);
            if (property != null && property.GetIndexParameters().Length == 0)
            {
                try
                {
                    return UnwrapDb(property.GetValue(target, null));
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
                    return UnwrapDb(field.GetValue(target));
                }
                catch
                {
                    return null;
                }
            }

            PropertyInfo indexer = type.GetProperty("Item", flags, null, null, new[] { typeof(string) }, null);
            if (indexer != null)
            {
                try
                {
                    return UnwrapDb(indexer.GetValue(target, new object[] { name }));
                }
                catch
                {
                    return null;
                }
            }

            return null;
        }

        private static object GetDataColumn(DataRow row, string name)
        {
            if (row == null || row.Table == null)
            {
                return null;
            }

            DataColumn match = null;
            foreach (DataColumn column in row.Table.Columns)
            {
                if (column.ColumnName.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    match = column;
                    break;
                }
            }

            if (match == null)
            {
                foreach (DataColumn column in row.Table.Columns)
                {
                    if (column.ColumnName.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0
                        || column.Caption.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        match = column;
                        break;
                    }
                }
            }

            if (match == null)
            {
                return null;
            }

            return UnwrapDb(row[match]);
        }

        private static object UnwrapDb(object value)
        {
            return value == DBNull.Value ? null : value;
        }
    }
}
