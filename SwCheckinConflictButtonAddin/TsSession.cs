using System;
using System.Collections.Generic;
using System.Globalization;
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
        public string UserName { get; set; }
        public string Token { get; set; }
        public string Cookie { get; set; }

        public void CopyFrom(TsSession other)
        {
            if (other == null)
            {
                return;
            }

            EpmBaseUrl = other.EpmBaseUrl;
            OriginUrl = other.OriginUrl;
            UserOid = other.UserOid;
            UserName = other.UserName;
            Token = other.Token;
            Cookie = other.Cookie;
        }

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
                ApplyUserIdentity(session, ReflectionValue.Get(cookieControl, "_userInfo", "userInfo", "UserInfo"));
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

            FillFromSessionInfo(session);

            if (string.IsNullOrWhiteSpace(session.UserOid))
            {
                ApplyUserOidText(session, JwtClaim(session.Token,
                    "UserOID", "userOid", "userOID", "oid", "user_id", "userId", "uid"));
            }

            if (string.IsNullOrWhiteSpace(session.UserName))
            {
                ApplyUserName(session, JwtClaim(session.Token,
                    "user_name", "username", "preferred_username", "UserName", "name"));
            }

            AddinLog.Info("TS 会话 user=" + (session.UserName ?? "")
                + " oid=" + (session.UserOid ?? "")
                + " epm=" + session.EpmBaseUrl
                + " origin=" + session.OriginUrl
                + " token=" + Mask(session.Token));
            return session;
        }

        public static void EnsureValidToken(TsSession session, Form seedForm)
        {
            if (session == null)
            {
                throw new LoginExpiredException();
            }

            bool expired = IsJwtExpired(session.Token);
            TokenProbeResult probe = expired ? TokenProbeResult.Invalid : ProbeRefreshToken(session);
            if (probe == TokenProbeResult.Valid)
            {
                return;
            }

            if (probe == TokenProbeResult.Unknown && !expired)
            {
                AddinLog.Info("refreshToken 无法确认（如 404），JWT 未过期，继续使用当前 Token");
                return;
            }

            AddinLog.Info("Token 无效或已过期，尝试 TeamSpace 静默重登");
            ReloginOrThrow(session, seedForm);
        }

        public static bool TryRelogin(TsSession session, Form seedForm)
        {
            try
            {
                ReloginOrThrow(session, seedForm);
                return true;
            }
            catch (LoginExpiredException)
            {
                return false;
            }
        }

        private static void ReloginOrThrow(TsSession session, Form seedForm)
        {
            if (!InvokeTsRelogin())
            {
                throw new LoginExpiredException();
            }

            session.CopyFrom(Resolve(seedForm));
            if (string.IsNullOrWhiteSpace(session.Token))
            {
                throw new LoginExpiredException();
            }

            if (IsJwtExpired(session.Token))
            {
                throw new LoginExpiredException();
            }

            TokenProbeResult probe = ProbeRefreshToken(session);
            if (probe == TokenProbeResult.Invalid)
            {
                throw new LoginExpiredException();
            }
        }

        private static bool InvokeTsRelogin()
        {
            object netOp = GetNetOp();
            if (netOp == null)
            {
                AddinLog.Info("静默重登失败：未找到 NetOp");
                return false;
            }

            string message;
            object cookieControl = ReflectionValue.Get(netOp,
                "UserCookieControl", "_userCookieControl", "CookieControl");
            if (TryInvokeRefString(cookieControl, "Update", out message))
            {
                AddinLog.Info("UserCookieControl.Update 成功 " + message);
                return true;
            }

            if (!string.IsNullOrEmpty(message))
            {
                AddinLog.Info("UserCookieControl.Update: " + message);
            }

            object user = cookieControl == null
                ? null
                : ReflectionValue.Get(cookieControl, "_userInfo", "userInfo", "UserInfo");
            if (user == null)
            {
                Type type = FindType("HustCAD.Session.SessionInfo");
                user = GetStatic(type, "UserInfo");
            }

            string userName = FirstText(user, "UserName", "UserID");
            string password = FirstText(user, "UserPWD", "UserPwd", "Password");
            if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password))
            {
                AddinLog.Info("静默重登失败：没有可用的用户名或密码");
                return false;
            }

            if (!TryInvokeLoginSys(netOp, userName, password, out message))
            {
                AddinLog.Info("LoginSys 失败: " + message);
                return false;
            }

            AddinLog.Info("LoginSys 成功");
            return true;
        }

        private static bool TryInvokeRefString(object target, string methodName, out string message)
        {
            message = string.Empty;
            if (target == null || string.IsNullOrEmpty(methodName))
            {
                return false;
            }

            try
            {
                MethodInfo method = target.GetType().GetMethod(methodName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase,
                    null, new[] { typeof(string).MakeByRefType() }, null);
                if (method == null)
                {
                    return false;
                }

                object[] args = { string.Empty };
                object result = method.Invoke(target, args);
                message = args[0] as string ?? string.Empty;
                return result is bool && (bool)result;
            }
            catch (Exception ex)
            {
                AddinLog.Info(target.GetType().Name + "." + methodName + " 失败: " + ex.Message);
                return false;
            }
        }

        private static bool TryInvokeLoginSys(object netOp, string userName, string password, out string message)
        {
            message = string.Empty;
            try
            {
                MethodInfo method = netOp.GetType().GetMethod("LoginSys",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase,
                    null, new[] { typeof(string), typeof(string), typeof(string).MakeByRefType() }, null);
                if (method == null)
                {
                    AddinLog.Info(netOp.GetType().Name + " 没有 LoginSys(string,string,ref string)");
                    return false;
                }

                object[] args = { userName, password, string.Empty };
                object result = method.Invoke(netOp, args);
                message = args[2] as string ?? string.Empty;
                return result is bool && (bool)result;
            }
            catch (Exception ex)
            {
                AddinLog.Info("LoginSys 调用失败: " + ex.Message);
                return false;
            }
        }

        private static TokenProbeResult ProbeRefreshToken(TsSession session)
        {
            if (session == null || string.IsNullOrWhiteSpace(session.OriginUrl) || string.IsNullOrWhiteSpace(session.Token))
            {
                return TokenProbeResult.Invalid;
            }

            string url = session.OriginUrl.TrimEnd('/') + "/rest/userService/v1/user/refreshToken";
            HttpCallResult call = PlmHttpClient.TryGet(url, session);
            if (call.StatusCode == 401 || call.StatusCode == 403)
            {
                return TokenProbeResult.Invalid;
            }

            if (call.StatusCode == 200)
            {
                ApplyRefreshedToken(session, call.Body);
                return TokenProbeResult.Valid;
            }

            AddinLog.Info("refreshToken 探测 HTTP " + call.StatusCode + "，不当作 Token 有效");
            return TokenProbeResult.Unknown;
        }

        private static void ApplyRefreshedToken(TsSession session, string body)
        {
            Dictionary<string, object> map;
            try
            {
                map = JsonUtil.ParseObject(body);
            }
            catch
            {
                return;
            }

            if (map == null)
            {
                return;
            }

            string token = JsonUtil.GetString(map, "token", "accessToken", "tsToken");
            Dictionary<string, object> data = JsonUtil.GetObject(map, "data");
            if (string.IsNullOrWhiteSpace(token) && data != null)
            {
                token = JsonUtil.GetString(data, "token", "accessToken", "tsToken");
            }

            if (string.IsNullOrWhiteSpace(token))
            {
                token = JsonUtil.GetString(map, "data");
            }

            if (string.IsNullOrWhiteSpace(token))
            {
                return;
            }

            session.Token = token.Trim();
            WriteTokenToTs(session.Token);
            AddinLog.Info("已写入 refreshToken 返回的新 Token " + Mask(session.Token));
        }

        private static void WriteTokenToTs(string token)
        {
            object netOp = GetNetOp();
            if (netOp == null)
            {
                return;
            }

            object web = ReflectionValue.Get(netOp, "webPLM", "WebPLM", "WebPlm", "WebPlmMiddle");
            ReflectionValue.Set(web, "UserTsToken", token);
            ReflectionValue.Set(web, "_strUserTsToken", token);
            object cookieControl = ReflectionValue.Get(netOp,
                "UserCookieControl", "_userCookieControl", "CookieControl");
            ReflectionValue.Set(cookieControl, "TsToken", token);
            ReflectionValue.Set(cookieControl, "_strToken", token);
        }

        private static bool IsJwtExpired(string token)
        {
            Dictionary<string, object> payload = ParseJwtPayload(token);
            if (payload == null)
            {
                return false;
            }

            string expText = JsonUtil.GetString(payload, "exp");
            if (string.IsNullOrWhiteSpace(expText))
            {
                return false;
            }

            double exp;
            if (!double.TryParse(expText, NumberStyles.Any, CultureInfo.InvariantCulture, out exp))
            {
                return false;
            }

            if (exp > 1000000000000d)
            {
                exp = exp / 1000d;
            }

            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            bool expired = exp + 5 < now;
            if (expired)
            {
                AddinLog.Info("JWT exp=" + expText + " 已过期");
            }

            return expired;
        }

        private static string JwtClaim(string token, params string[] names)
        {
            Dictionary<string, object> payload = ParseJwtPayload(token);
            return payload == null ? string.Empty : JsonUtil.GetString(payload, names);
        }

        private static Dictionary<string, object> ParseJwtPayload(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return null;
            }

            string value = token.Trim();
            if (value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                value = value.Substring(7).Trim();
            }

            string[] parts = value.Split('.');
            if (parts.Length < 2)
            {
                return null;
            }

            try
            {
                return JsonUtil.ParseObject(DecodeBase64Url(parts[1]));
            }
            catch (Exception ex)
            {
                AddinLog.Info("JWT 解析失败: " + ex.Message);
                return null;
            }
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
            ApplyUserIdentity(session, userInfo);
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

        private static void ApplyUserIdentity(TsSession session, object user)
        {
            if (user == null)
            {
                return;
            }

            ApplyUserOidText(session, FirstText(user, "UserOID", "UserOid", "OID", "Oid"));
            if (string.IsNullOrWhiteSpace(session.UserName))
            {
                ApplyUserName(session, FirstText(user, "UserName", "UserID"));
            }
        }

        private static void ApplyUserName(TsSession session, string name)
        {
            if (session == null || !string.IsNullOrWhiteSpace(session.UserName) || string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            session.UserName = name.Trim();
        }

        private static void ApplyUserOidText(TsSession session, string oid)
        {
            if (session == null || !string.IsNullOrWhiteSpace(session.UserOid) || string.IsNullOrWhiteSpace(oid))
            {
                return;
            }

            session.UserOid = oid.Trim();
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

    internal enum TokenProbeResult
    {
        Valid,
        Invalid,
        Unknown
    }

    internal sealed class LoginExpiredException : InvalidOperationException
    {
        public const string DefaultMessage = "登录已失效，请在 TeamSpace 中重新登录后再试。";

        public LoginExpiredException()
            : base(DefaultMessage)
        {
        }
    }
}
