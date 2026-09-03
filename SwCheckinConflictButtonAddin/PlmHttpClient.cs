using System;
using System.IO;
using System.Net;
using System.Text;

namespace SwCheckinConflictButtonAddin
{
    internal static class PlmHttpClient
    {
        public static string PostJson(string url, string body, TsSession session)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                throw new InvalidOperationException("接口地址为空");
            }

            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            ServicePointManager.Expect100Continue = false;

            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "POST";
            request.ContentType = "application/json";
            request.Timeout = 120000;
            request.ReadWriteTimeout = 120000;
            request.KeepAlive = false;
            request.Headers["Accept-Language"] = "zh-CN";
            if (session != null && !string.IsNullOrEmpty(session.Token))
            {
                request.Headers["authorization"] = session.Token;
                request.Headers["ootb-auth-token"] = session.Token;
            }

            if (session != null && !string.IsNullOrEmpty(session.Cookie))
            {
                request.Headers["Cookie"] = session.Cookie;
            }

            byte[] bytes = Encoding.UTF8.GetBytes(body ?? "{}");
            request.ContentLength = bytes.Length;
            using (Stream stream = request.GetRequestStream())
            {
                stream.Write(bytes, 0, bytes.Length);
            }

            AddinLog.Info("POST " + url + " bytes=" + bytes.Length);
            HttpWebResponse response;
            try
            {
                response = (HttpWebResponse)request.GetResponse();
            }
            catch (WebException ex)
            {
                response = ex.Response as HttpWebResponse;
                if (response == null)
                {
                    throw new InvalidOperationException("调用后端失败: " + ex.Message, ex);
                }
            }

            using (response)
            using (var reader = new StreamReader(response.GetResponseStream() ?? Stream.Null, Encoding.UTF8))
            {
                string text = reader.ReadToEnd();
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    int code = (int)response.StatusCode;
                    AddinLog.Info("后端 HTTP " + code + " " + url + " body=" + Trim(text, 1500));
                    throw new InvalidOperationException(FormatHttpError(code, text));
                }

                return text;
            }
        }

        private static string FormatHttpError(int statusCode, string body)
        {
            string hint;
            switch (statusCode)
            {
                case 401:
                    hint = "未登录或登录已过期";
                    break;
                case 403:
                    hint = "没有权限";
                    break;
                case 404:
                    hint = "接口不存在";
                    break;
                case 500:
                    hint = "服务器内部错误";
                    break;
                case 502:
                    hint = "网关错误";
                    break;
                case 503:
                    hint = "服务不可用";
                    break;
                case 504:
                    hint = "网关超时";
                    break;
                default:
                    hint = "调用失败";
                    break;
            }

            string readable = ExtractUserMessage(body);
            if (string.IsNullOrEmpty(readable))
            {
                return "后端暂时不可用（HTTP " + statusCode + " " + hint
                    + "）。请稍后重试，若持续出现请联系管理员。";
            }

            return "后端暂时不可用（HTTP " + statusCode + " " + hint + "）：" + readable;
        }

        private static string ExtractUserMessage(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                return string.Empty;
            }

            string text = body.Trim();
            if (LooksLikeHtml(text))
            {
                return string.Empty;
            }

            return Trim(CollapseSpaces(text), 160);
        }

        private static bool LooksLikeHtml(string text)
        {
            if (text.Length > 0 && text[0] == '<')
            {
                return true;
            }

            return text.IndexOf("<html", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("<!DOCTYPE", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string CollapseSpaces(string text)
        {
            var builder = new StringBuilder(text.Length);
            bool space = false;
            foreach (char c in text)
            {
                if (char.IsWhiteSpace(c))
                {
                    if (!space)
                    {
                        builder.Append(' ');
                        space = true;
                    }
                }
                else
                {
                    builder.Append(c);
                    space = false;
                }
            }

            return builder.ToString().Trim();
        }

        private static string Trim(string text, int max)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= max)
            {
                return text ?? string.Empty;
            }

            return text.Substring(0, max) + "...";
        }
    }
}
