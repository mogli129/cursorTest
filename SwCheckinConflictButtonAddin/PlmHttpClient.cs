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
                    throw new InvalidOperationException("后端 HTTP " + (int)response.StatusCode + " " + Trim(text, 300));
                }

                return text;
            }
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
