using System;
using System.Collections;
using System.Collections.Generic;
using System.Web.Script.Serialization;

namespace SwCheckinConflictButtonAddin
{
    internal static class JsonUtil
    {
        private static readonly JavaScriptSerializer Serializer = CreateSerializer();

        private static JavaScriptSerializer CreateSerializer()
        {
            return new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        }

        public static string Serialize(object value)
        {
            return Serializer.Serialize(value);
        }

        public static Dictionary<string, object> ParseObject(string json)
        {
            object value = Serializer.DeserializeObject(json);
            return AsObject(value);
        }

        public static Dictionary<string, object> AsObject(object value)
        {
            if (value is Dictionary<string, object> map)
            {
                return map;
            }

            return null;
        }

        public static IList AsList(object value)
        {
            return value as IList;
        }

        public static string GetString(IDictionary<string, object> map, params string[] names)
        {
            if (map == null)
            {
                return string.Empty;
            }

            foreach (string name in names)
            {
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                foreach (KeyValuePair<string, object> pair in map)
                {
                    if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
                    {
                        return ToText(pair.Value);
                    }
                }
            }

            return string.Empty;
        }

        public static bool GetBool(IDictionary<string, object> map, params string[] names)
        {
            string text = GetString(map, names);
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            return text == "1"
                || text.Equals("true", StringComparison.OrdinalIgnoreCase)
                || text.Equals("success", StringComparison.OrdinalIgnoreCase);
        }

        public static IList GetList(IDictionary<string, object> map, params string[] names)
        {
            if (map == null)
            {
                return null;
            }

            foreach (string name in names)
            {
                foreach (KeyValuePair<string, object> pair in map)
                {
                    if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
                    {
                        return AsList(pair.Value);
                    }
                }
            }

            return null;
        }

        public static Dictionary<string, object> GetObject(IDictionary<string, object> map, params string[] names)
        {
            if (map == null)
            {
                return null;
            }

            foreach (string name in names)
            {
                foreach (KeyValuePair<string, object> pair in map)
                {
                    if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
                    {
                        return AsObject(pair.Value);
                    }
                }
            }

            return null;
        }

        public static string ToText(object value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            if (value is bool flag)
            {
                return flag ? "true" : "false";
            }

            return Convert.ToString(value);
        }
    }
}
