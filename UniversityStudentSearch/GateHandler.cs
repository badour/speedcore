using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web;
using System.Web.Script.Serialization;
using UniversityStudentSearch.Data;

namespace UniversityStudentSearch
{
    public sealed class GateHandler : IHttpHandler
    {
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();

        public bool IsReusable
        {
            get { return true; }
        }

        public void ProcessRequest(HttpContext context)
        {
            HttpRequest request = context.Request;
            HttpResponse response = context.Response;
            response.ContentType = "application/json";
            response.ContentEncoding = new UTF8Encoding(false);
            response.Charset = "utf-8";
            response.TrySkipIisCustomErrors = true;

            string path = (request.Path ?? string.Empty).ToLowerInvariant();
            string op = (request.QueryString["op"] ?? string.Empty).Trim().ToLowerInvariant();
            try
            {
                if (op == "access" || path.EndsWith("/access"))
                {
                    WriteAccess(request, response);
                    return;
                }

                if (op == "commands" || path.EndsWith("/commands"))
                {
                    WriteCommands(request, response);
                    return;
                }

                if (op == "ack" || path.EndsWith("/ack"))
                {
                    WriteAck(request, response);
                    return;
                }
            }
            catch (Exception)
            {
                response.StatusCode = 500;
                response.Write("{\"access\":0,\"message\":" + GateStore.JsonQuote("Not allowed") + "}");
                return;
            }

            response.StatusCode = 404;
            response.Write("{\"ok\":false}");
        }

        private static void WriteAccess(HttpRequest request, HttpResponse response)
        {
            Dictionary<string, object> body = ReadJson(request);
            long studentId = ReadInt(body, "enrollid");
            GateDecision decision = StudentRepository.DecideAccess(studentId);
            int access = decision.Succeeded && decision.Allowed ? 1 : 0;
            string message = string.IsNullOrEmpty(decision.DeviceMessage)
                ? (access == 1 ? "Allowed" : "Not allowed")
                : decision.DeviceMessage;
            response.Write("{\"access\":" + access.ToString(CultureInfo.InvariantCulture)
                + ",\"message\":" + GateStore.JsonQuote(message)
                + ",\"name\":" + GateStore.JsonQuote(decision.StudentName)
                + ",\"department\":" + GateStore.JsonQuote(decision.Department)
                + "}");
        }

        private static void WriteCommands(HttpRequest request, HttpResponse response)
        {
            string serial = (request.QueryString["sn"] ?? string.Empty).Trim();
            if (!IsSerial(serial))
            {
                response.StatusCode = 400;
                response.Write("{\"commands\":[]}");
                return;
            }

            List<GateCommand> commands = GateStore.Pending(serial, 20);
            var builder = new StringBuilder();
            builder.Append("{\"commands\":[");
            for (int i = 0; i < commands.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }

                builder.Append("{\"id\":");
                builder.Append(commands[i].Id.ToString(CultureInfo.InvariantCulture));
                builder.Append(",\"body\":");
                builder.Append(string.IsNullOrEmpty(commands[i].Body) ? "null" : commands[i].Body);
                builder.Append('}');
            }

            builder.Append("]}");
            response.Write(builder.ToString());
        }

        private static void WriteAck(HttpRequest request, HttpResponse response)
        {
            Dictionary<string, object> body = ReadJson(request);
            string serial = ReadString(body, "sn");
            long commandId = ReadInt(body, "id");
            bool succeeded = ReadBool(body, "result");
            if (!IsSerial(serial) || commandId <= 0)
            {
                response.StatusCode = 400;
                response.Write("{\"ok\":false}");
                return;
            }

            GateStore.Acknowledge(serial, commandId, succeeded);
            response.Write("{\"ok\":true}");
        }

        private static Dictionary<string, object> ReadJson(HttpRequest request)
        {
            string text = string.Empty;
            if (request.InputStream != null && request.InputStream.CanRead)
            {
                using (var reader = new StreamReader(request.InputStream, Encoding.UTF8))
                {
                    char[] buffer = new char[8000];
                    int read = reader.Read(buffer, 0, buffer.Length);
                    if (read > 0)
                    {
                        text = new string(buffer, 0, read);
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            }

            var parsed = Json.DeserializeObject(text) as Dictionary<string, object>;
            if (parsed == null)
            {
                return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            }

            var map = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, object> pair in parsed)
            {
                map[pair.Key] = pair.Value;
            }

            return map;
        }

        private static string ReadString(Dictionary<string, object> body, string name)
        {
            object value;
            if (body == null || !body.TryGetValue(name, out value) || value == null)
            {
                return string.Empty;
            }

            return Convert.ToString(value, CultureInfo.InvariantCulture).Trim();
        }

        private static long ReadInt(Dictionary<string, object> body, string name)
        {
            object value;
            if (body == null || !body.TryGetValue(name, out value) || value == null)
            {
                return 0;
            }

            long parsed;
            if (long.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
            {
                return parsed;
            }

            return 0;
        }

        private static bool ReadBool(Dictionary<string, object> body, string name)
        {
            object value;
            if (body == null || !body.TryGetValue(name, out value) || value == null)
            {
                return false;
            }

            if (value is bool)
            {
                return (bool)value;
            }

            string text = Convert.ToString(value, CultureInfo.InvariantCulture);
            return text == "1" || text.Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSerial(string serial)
        {
            if (string.IsNullOrEmpty(serial) || serial.Length > 64)
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
