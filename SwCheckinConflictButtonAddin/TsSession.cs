using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace SwCheckinConflictButtonAddin
{
    /// <summary>
    /// 从 NetOpFactory.GetNetOp() → webPLM 取地址和 Token；
    /// 用户 OID 优先 SessionInfo.UserInfo（登录写入处），再 UserCookieControl._userInfo，最后 JWT。
    /// </summary>
    internal sealed class TsSession
    {
        public string EpmBaseUrl { get; set; }
        public string OriginUrl { get; set; }
        public string UserOid { get; set; }
        public string Token { get; set; }
        public string Cookie { get; set; }

        public bool IsUsable
        {
            get
            {
                return !string.IsNullOrWhiteSpace(EpmBaseUrl)
                    && !string.IsNullOrWhiteSpace(Token)
                    && !string.IsNullOrWhiteSpace(UserOid);
            }
        }
    }

    internal static class TsSessionLocator
    {
        public static TsSession Resolve(Form seedForm)
        {
            var session = new TsSession();
            object netOp = GetNetOp();
            if (netOp == null)
            {
                AddinLog.Info("未找到 NetOpFactory.GetNetOp");
                return session;
            }

            AddinLog.Info("NetOp 类型=" + netOp.GetType().FullName);
            object web = ReflectionValue.Get(netOp, "webPLM", "WebPLM", "WebPlm", "WebPlmMiddle");
            FillFromWebPlm(session, web);

            object cookieControl = ReflectionValue.Get(netOp,
                "UserCookieControl", "_userCookieControl", "CookieControl");
            if (cookieControl != null)
            {
                // UserInfo 属性只有 setter，必须读私有字段 _userInfo
                ApplyUserOid(session, ReflectionValue.Get(cookieControl, "_userInfo", "userInfo", "UserInfo"));
                string token = FirstText(cookieControl, "TsToken", "_strToken", "Token");
                if (!string.IsNullOrWhiteSpace(token))
                {
                    session.Token = token;
                }

                string cookie = FirstText(cookieControl, "UserCookie", "Cookie");
                if (!string.IsNullOrWhiteSpace(cookie))
                {
                    session.Cookie = cookie;
                }
            }
            else
            {
                AddinLog.Info("UserCookieControl 为空");
            }

            if (string.IsNullOrWhiteSpace(session.UserOid))
            {
                FillFromSessionInfo(session);
            }

            if (string.IsNullOrWhiteSpace(session.UserOid))
            {
                ApplyUserOidText(session, TryUserOidFromJwt(session.Token));
            }

            AddinLog.Info("TS 会话 oid=" + (session.UserOid ?? "")
                + " epm=" + session.EpmBaseUrl
                + " origin=" + session.OriginUrl
                + " token=" + Mask(session.Token));
            return session;
        }

        private static object GetNetOp()
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = TryGetNetOpFactory(assembly);
                if (type == null)
                {
                    continue;
                }

                MethodInfo method = type.GetMethod("GetNetOp",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                    null, Type.EmptyTypes, null);
                if (method == null)
                {
                    continue;
                }

                try
                {
                    object netOp = method.Invoke(null, null);
                    if (netOp != null)
                    {
                        return netOp;
                    }
                }
                catch (Exception ex)
                {
                    AddinLog.Info(type.FullName + ".GetNetOp 失败: " + ex.Message);
                }
            }

            return null;
        }

        private static Type TryGetNetOpFactory(Assembly assembly)
        {
            try
            {
                Type type = assembly.GetType("HustCAD.NetOp.NetOpFactory", false)
                    ?? assembly.GetType("HustCAD.CNetOp.NetOpFactory", false);
                if (type != null)
                {
                    return type;
                }
            }
            catch
            {
            }

            string name = assembly.GetName().Name ?? string.Empty;
            if (name != "CNetOp" && name != "HustCAD.NetOp" && name != "NetOp")
            {
                return null;
            }

            Type[] types;
            try
            {
                types = assembly.GetExportedTypes();
            }
            catch
            {
                return null;
            }

            foreach (Type type in types)
            {
                if (type != null && type.Name == "NetOpFactory")
                {
                    return type;
                }
            }

            return null;
        }

        private static void FillFromWebPlm(TsSession session, object webPlm)
        {
            if (webPlm == null)
            {
                AddinLog.Info("webPLM 为空");
                return;
            }

            string address = FirstText(webPlm, "AddressURL", "AddressUrl", "_AddressURL");
            if (!string.IsNullOrWhiteSpace(address))
            {
                ApplyEpmUrl(session, address);
            }

            string token = FirstText(webPlm, "UserTsToken", "TsToken", "_strUserTsToken");
            if (!string.IsNullOrWhiteSpace(token))
            {
                session.Token = token;
            }

            string cookie = FirstText(webPlm, "UserCookie");
            if (!string.IsNullOrWhiteSpace(cookie))
            {
                session.Cookie = cookie;
            }
        }

        private static void FillFromSessionInfo(TsSession session)
        {
            Type type = FindType("HustCAD.Session.SessionInfo");
            if (type == null)
            {
                AddinLog.Info("未找到 HustCAD.Session.SessionInfo");
                return;
            }

            object userInfo = GetStatic(type, "UserInfo");
            if (userInfo == null)
            {
                AddinLog.Info("SessionInfo.UserInfo 为空");
                return;
            }

            AddinLog.Info("SessionInfo.UserInfo 类型=" + userInfo.GetType().FullName);
            ApplyUserOid(session, userInfo);
            if (string.IsNullOrWhiteSpace(session.Token))
            {
                string token = FirstText(userInfo, "TsToken", "Token");
                if (!string.IsNullOrWhiteSpace(token))
                {
                    session.Token = token;
                }
            }

            if (string.IsNullOrWhiteSpace(session.Cookie))
            {
                string cookie = FirstText(userInfo, "UserCookie", "Cookie");
                if (!string.IsNullOrWhiteSpace(cookie))
                {
                    session.Cookie = cookie;
                }
            }
        }

        private static Type FindType(string fullName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    Type type = assembly.GetType(fullName, false);
                    if (type != null)
                    {
                        return type;
                    }
                }
                catch
                {
                }
            }

            return null;
        }

        private static object GetStatic(Type type, string name)
        {
            if (type == null || string.IsNullOrEmpty(name))
            {
                return null;
            }

            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Static | BindingFlags.IgnoreCase;
            try
            {
                PropertyInfo property = type.GetProperty(name, flags);
                if (property != null && property.CanRead && property.GetIndexParameters().Length == 0)
                {
                    return property.GetValue(null, null);
                }

                FieldInfo field = type.GetField(name, flags);
                if (field != null)
                {
                    return field.GetValue(null);
                }
            }
            catch (Exception ex)
            {
                AddinLog.Info("读取 " + type.FullName + "." + name + " 失败: " + ex.Message);
            }

            return null;
        }

        private static void ApplyUserOid(TsSession session, object user)
        {
            if (user == null || !string.IsNullOrWhiteSpace(session.UserOid))
            {
                return;
            }

            ApplyUserOidText(session, FirstText(user, "UserOID", "UserOid", "OID", "Oid"));
        }

        private static void ApplyUserOidText(TsSession session, string oid)
        {
            if (session == null || !string.IsNullOrWhiteSpace(session.UserOid) || string.IsNullOrWhiteSpace(oid))
            {
                return;
            }

            session.UserOid = oid.Trim();
        }

        private static string TryUserOidFromJwt(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return string.Empty;
            }

            string value = token.Trim();
            if (value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                value = value.Substring(7).Trim();
            }

            string[] parts = value.Split('.');
            if (parts.Length < 2)
            {
                return string.Empty;
            }

            try
            {
                Dictionary<string, object> payload = JsonUtil.ParseObject(DecodeBase64Url(parts[1]));
                return JsonUtil.GetString(payload,
                    "UserOID", "userOid", "userOID", "oid", "user_id", "userId", "uid");
            }
            catch (Exception ex)
            {
                AddinLog.Info("JWT 解析 UserOID 失败: " + ex.Message);
                return string.Empty;
            }
        }

        private static string DecodeBase64Url(string text)
        {
            string padded = (text ?? string.Empty).Replace('-', '+').Replace('_', '/');
            switch (padded.Length % 4)
            {
                case 2:
                    padded += "==";
                    break;
                case 3:
                    padded += "=";
                    break;
            }

            return Encoding.UTF8.GetString(Convert.FromBase64String(padded));
        }

        private static void ApplyEpmUrl(TsSession session, string epmUrl)
        {
            string url = (epmUrl ?? string.Empty).Trim().TrimEnd('/');
            if (url.IndexOf("http", StringComparison.OrdinalIgnoreCase) < 0)
            {
                url = "http://" + url.TrimStart('/');
            }

            if (url.IndexOf("/teamspace/rest/epm", StringComparison.OrdinalIgnoreCase) < 0)
            {
                url = url.TrimEnd('/') + "/teamspace/rest/epm";
            }

            session.EpmBaseUrl = url;
            const string suffix = "/teamspace/rest/epm";
            int index = url.IndexOf(suffix, StringComparison.OrdinalIgnoreCase);
            session.OriginUrl = index > 0 ? url.Substring(0, index) : url;
        }

        private static string FirstText(object target, params string[] names)
        {
            return ReflectionValue.GetString(target, names);
        }

        private static string Mask(string token)
        {
            if (string.IsNullOrEmpty(token))
            {
                return "(empty)";
            }

            if (token.Length <= 8)
            {
                return "***";
            }

            return token.Substring(0, 4) + "..." + token.Substring(token.Length - 4);
        }
    }
}
