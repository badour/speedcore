using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web;
using UniversityStudentSearch.Data;

namespace UniversityStudentSearch
{
    public sealed class IclockHandler : IHttpHandler
    {
        public bool IsReusable
        {
            get { return true; }
        }

        public void ProcessRequest(HttpContext context)
        {
            HttpRequest request = context.Request;
            HttpResponse response = context.Response;
            response.ContentType = "text/plain";
            response.ContentEncoding = new UTF8Encoding(false);
            response.Charset = "utf-8";
            response.TrySkipIisCustomErrors = true;

            string serial = (request.QueryString["SN"] ?? string.Empty).Trim();
            if (!IsSerial(serial))
            {
                response.StatusCode = 400;
                response.Write("OK");
                return;
            }

            string action = (request.Path ?? string.Empty).ToLowerInvariant();
            if (action.EndsWith("/getrequest"))
            {
                response.Write(IclockStore.NextCommands(serial, 30));
                return;
            }

            if (action.EndsWith("/devicecmd"))
            {
                List<KeyValuePair<long, bool>> acks = ReadAcks(request);
                if (acks.Count == 0)
                {
                    IclockStore.Touch(serial);
                }
                else
                {
                    foreach (KeyValuePair<long, bool> ack in acks)
                    {
                        IclockStore.Acknowledge(serial, ack.Key, ack.Value);
                    }
                }

                response.Write("OK");
                return;
            }

            if (action.EndsWith("/cdata") && string.Equals(request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
            {
                response.Write(IclockStore.Options(serial));
                return;
            }

            IclockStore.Touch(serial);
            response.Write("OK");
        }

        private static List<KeyValuePair<long, bool>> ReadAcks(HttpRequest request)
        {
            var acks = new List<KeyValuePair<long, bool>>();
            string body = string.Empty;
            if (request.InputStream != null && request.InputStream.CanRead)
            {
                using (var reader = new StreamReader(request.InputStream, Encoding.UTF8))
                {
                    char[] buffer = new char[8000];
                    int read = reader.Read(buffer, 0, buffer.Length);
                    if (read > 0)
                    {
                        body = new string(buffer, 0, read);
                    }
                }
            }

            string combined = request.Url.Query + "&" + body.Replace("\r", "&").Replace("\n", "&");
            string[] parts = combined.Split('&');
            long commandId = 0;
            bool haveId = false;
            foreach (string part in parts)
            {
                int split = part.IndexOf('=');
                if (split <= 0)
                {
                    continue;
                }

                string key = part.Substring(0, split).Trim().TrimStart('?');
                string raw = part.Substring(split + 1).Trim();
                string value = raw;
                try
                {
                    value = Uri.UnescapeDataString(raw.Replace("+", " "));
                }
                catch (UriFormatException)
                {
                    value = raw;
                }

                if (key.Equals("ID", StringComparison.OrdinalIgnoreCase))
                {
                    if (haveId)
                    {
                        acks.Add(new KeyValuePair<long, bool>(commandId, false));
                    }

                    haveId = long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out commandId) && commandId > 0;
                }
                else if (key.Equals("Return", StringComparison.OrdinalIgnoreCase) && haveId)
                {
                    acks.Add(new KeyValuePair<long, bool>(commandId, value == "0"));
                    haveId = false;
                }
            }

            if (haveId)
            {
                acks.Add(new KeyValuePair<long, bool>(commandId, false));
            }

            return acks;
        }

        private static bool IsSerial(string serial)
        {
            if (serial.Length == 0 || serial.Length > 32)
            {
                return false;
            }

            foreach (char ch in serial)
            {
                if (!char.IsLetterOrDigit(ch) && ch != '-' && ch != '_')
                {
                    return false;
                }
            }

            return true;
        }
    }
}
