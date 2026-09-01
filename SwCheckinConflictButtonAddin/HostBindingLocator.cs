using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Windows.Forms;

namespace SwCheckinConflictButtonAddin
{
    internal static class HostBindingLocator
    {
        public static IList FindConflictList(Form form)
        {
            if (form == null)
            {
                return null;
            }

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            Type type = form.GetType();
            IList best = null;
            int bestScore = -1;

            foreach (FieldInfo field in type.GetFields(flags))
            {
                object value;
                try
                {
                    value = field.GetValue(form);
                }
                catch
                {
                    continue;
                }

                ScoreList(field.Name, value, ref best, ref bestScore);
            }

            foreach (PropertyInfo property in type.GetProperties(flags))
            {
                if (property.GetIndexParameters().Length != 0)
                {
                    continue;
                }

                object value;
                try
                {
                    value = property.GetValue(form, null);
                }
                catch
                {
                    continue;
                }

                ScoreList(property.Name, value, ref best, ref bestScore);
            }

            if (best != null)
            {
                AddinLog.Info("冲突列表绑定 Count=" + best.Count
                    + " itemType=" + (best.Count > 0 && best[0] != null ? best[0].GetType().FullName : "empty"));
            }

            return best;
        }

        private static void ScoreList(string name, object value, ref IList best, ref int bestScore)
        {
            IList list = AsList(value);
            if (list == null || list.Count == 0)
            {
                return;
            }

            if (list is Control.ControlCollection || name == "Controls")
            {
                return;
            }

            int score = list.Count;
            string lower = name ?? string.Empty;
            if (ContainsAny(lower, "Conflict", "冲突", "Document", "文档", "Cad", "File", "CheckIn", "检入"))
            {
                score += 100;
            }

            if (ContainsAny(lower, "Bind", "Source", "List", "Items", "Rows", "Data"))
            {
                score += 20;
            }

            if (score > bestScore)
            {
                bestScore = score;
                best = list;
            }
        }

        private static IList AsList(object value)
        {
            if (value is BindingSource source)
            {
                return source.List;
            }

            if (value is IList list)
            {
                return list;
            }

            return null;
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
    }
}
