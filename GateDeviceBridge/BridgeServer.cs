using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace GateDeviceBridge
{
    public sealed class BridgeServer
    {
        private const string SocketGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
        private readonly string middleware;
        private readonly int port;
        private readonly JavaScriptSerializer json = new JavaScriptSerializer();
        private TcpListener listener;

        public BridgeServer(string middleware, int port)
        {
            this.middleware = middleware;
            this.port = port;
        }

        public void Start()
        {
            listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            ThreadPool.QueueUserWorkItem(delegate { AcceptLoop(); });
        }

        private void AcceptLoop()
        {
            while (true)
            {
                TcpClient client;
                try
                {
                    client = listener.AcceptTcpClient();
                }
                catch (Exception)
                {
                    return;
                }

                ThreadPool.QueueUserWorkItem(delegate { Serve(client); });
            }
        }

        private void Serve(TcpClient client)
        {
            var link = new DeviceLink(client);
            try
            {
                client.ReceiveTimeout = 0;
                if (!AcceptUpgrade(link))
                {
                    return;
                }

                Console.WriteLine("Terminal connected");
                var timer = new Timer(delegate
                {
                    try
                    {
                        Flush(link);
                    }
                    catch (Exception)
                    {
                    }
                }, null, 2000, 2000);
                var pendingText = new StringBuilder();
                try
                {
                while (link.Open)
                {
                    WsFrame frame = ReadFrame(link.Stream);
                    if (frame == null)
                    {
                        break;
                    }

                    if (frame.Opcode == 8)
                    {
                        break;
                    }

                    if (frame.Opcode == 9)
                    {
                        SendFrame(link, 10, frame.Payload);
                        continue;
                    }

                    if (frame.Opcode != 1)
                    {
                        continue;
                    }

                    pendingText.Append(Encoding.UTF8.GetString(frame.Payload));
                    if (!frame.Final)
                    {
                        continue;
                    }

                    string text = pendingText.ToString();
                    pendingText.Length = 0;
                    HandleText(link, text);
                }
                }
                finally
                {
                    timer.Dispose();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Terminal closed: " + ex.Message);
            }
            finally
            {
                link.Open = false;
                client.Close();
                if (!string.IsNullOrEmpty(link.Serial))
                {
                    Console.WriteLine("Terminal offline " + link.Serial);
                }
            }
        }

        private void HandleText(DeviceLink link, string text)
        {
            Dictionary<string, object> message = Parse(text);
            if (message == null)
            {
                return;
            }

            if (message.ContainsKey("cmd"))
            {
                string cmd = AsString(message, "cmd");
                if (cmd == "reg")
                {
                    link.Serial = AsString(message, "sn");
                    Console.WriteLine("Terminal registered " + link.Serial);
                    SendJson(link, "{\"ret\":\"reg\",\"result\":true,\"cloudtime\":" + Quote(Now()) + "}");
                    SendJson(link, "{\"cmd\":\"setdevinfo\",\"server_verify\":1}");
                    Flush(link);
                    return;
                }

                if (cmd == "sendlog")
                {
                    AnswerAccess(link, message);
                    return;
                }

                if (cmd == "senduser")
                {
                    SendJson(link, "{\"ret\":\"senduser\",\"result\":true,\"cloudtime\":" + Quote(Now()) + "}");
                    return;
                }

                SendJson(link, "{\"ret\":" + Quote(cmd) + ",\"result\":true,\"cloudtime\":" + Quote(Now()) + "}");
                return;
            }

            if (message.ContainsKey("ret"))
            {
                string ret = AsString(message, "ret");
                bool ok = AsBool(message, "result");
                if (link.InFlight > 0 && ret == "setuserinfo")
                {
                    long id = link.InFlight;
                    link.InFlight = 0;
                    if (ok)
                    {
                        Post("/gate/ack", "{\"sn\":" + Quote(link.Serial) + ",\"id\":" + id.ToString(CultureInfo.InvariantCulture) + ",\"result\":true}");
                    }

                    Flush(link);
                }
            }
        }

        private void AnswerAccess(DeviceLink link, Dictionary<string, object> message)
        {
            long enrollId = FirstEnrollId(message);
            string raw = Post("/gate/access", "{\"sn\":" + Quote(link.Serial) + ",\"enrollid\":" + enrollId.ToString(CultureInfo.InvariantCulture) + "}");
            int access = 0;
            string deviceMessage = "Not allowed";
            Dictionary<string, object> decision = Parse(raw);
            if (decision != null)
            {
                access = AsBool(decision, "access") || AsString(decision, "access") == "1" ? 1 : 0;
                if (decision.ContainsKey("message"))
                {
                    deviceMessage = AsString(decision, "message");
                }
            }

            int count = (int)AsLong(message, "count");
            long logIndex = AsLong(message, "logindex");
            SendJson(link, "{\"ret\":\"sendlog\",\"result\":true,\"cloudtime\":" + Quote(Now())
                + ",\"access\":" + access.ToString(CultureInfo.InvariantCulture)
                + ",\"message\":" + Quote(deviceMessage)
                + ",\"count\":" + count.ToString(CultureInfo.InvariantCulture)
                + ",\"logindex\":" + logIndex.ToString(CultureInfo.InvariantCulture) + "}");
        }

        private void Flush(DeviceLink link)
        {
            if (!link.Open || string.IsNullOrEmpty(link.Serial) || link.InFlight != 0)
            {
                return;
            }

            string raw = Get("/gate/commands?sn=" + Uri.EscapeDataString(link.Serial));
            Dictionary<string, object> payload = Parse(raw);
            if (payload == null || !payload.ContainsKey("commands"))
            {
                return;
            }

            IEnumerable commands = payload["commands"] as IEnumerable;
            if (commands == null)
            {
                return;
            }

            foreach (object item in commands)
            {
                var command = item as Dictionary<string, object>;
                if (command == null || !command.ContainsKey("body"))
                {
                    continue;
                }

                long id = AsLong(command, "id");
                string body = json.Serialize(command["body"]);
                link.InFlight = id;
                SendJson(link, body);
                return;
            }
        }

        private string Get(string path)
        {
            try
            {
                var request = (HttpWebRequest)WebRequest.Create(middleware + path);
                request.Method = "GET";
                request.Timeout = 15000;
                using (var response = (HttpWebResponse)request.GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                {
                    return reader.ReadToEnd();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Middleware read failed: " + ex.Message);
                return string.Empty;
            }
        }

        private string Post(string path, string body)
        {
            try
            {
                var request = (HttpWebRequest)WebRequest.Create(middleware + path);
                request.Method = "POST";
                request.ContentType = "application/json";
                request.Timeout = 15000;
                byte[] bytes = Encoding.UTF8.GetBytes(body);
                request.ContentLength = bytes.Length;
                using (Stream stream = request.GetRequestStream())
                {
                    stream.Write(bytes, 0, bytes.Length);
                }

                using (var response = (HttpWebResponse)request.GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                {
                    return reader.ReadToEnd();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Middleware write failed: " + ex.Message);
                return string.Empty;
            }
        }

        private Dictionary<string, object> Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            try
            {
                var parsed = json.DeserializeObject(text) as Dictionary<string, object>;
                if (parsed == null)
                {
                    return null;
                }

                var map = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                foreach (KeyValuePair<string, object> pair in parsed)
                {
                    map[pair.Key] = pair.Value;
                }

                return map;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static long FirstEnrollId(Dictionary<string, object> message)
        {
            object records;
            if (!message.TryGetValue("record", out records))
            {
                return AsLong(message, "enrollid");
            }

            var list = records as IEnumerable;
            if (list == null || records is string)
            {
                return AsLong(message, "enrollid");
            }

            foreach (object item in list)
            {
                var row = item as Dictionary<string, object>;
                if (row != null)
                {
                    return AsLong(row, "enrollid");
                }
            }

            return 0;
        }

        private static string AsString(Dictionary<string, object> message, string name)
        {
            object value;
            if (message == null || !message.TryGetValue(name, out value) || value == null)
            {
                return string.Empty;
            }

            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static long AsLong(Dictionary<string, object> message, string name)
        {
            long parsed;
            if (long.TryParse(AsString(message, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
            {
                return parsed;
            }

            return 0;
        }

        private static bool AsBool(Dictionary<string, object> message, string name)
        {
            object value;
            if (message == null || !message.TryGetValue(name, out value) || value == null)
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

        private static string Now()
        {
            return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        private static string Quote(string value)
        {
            if (value == null)
            {
                return "\"\"";
            }

            var builder = new StringBuilder(value.Length + 2);
            builder.Append('"');
            foreach (char ch in value)
            {
                if (ch == '\\' || ch == '"')
                {
                    builder.Append('\\');
                    builder.Append(ch);
                }
                else if (ch < 32)
                {
                    builder.Append("\\u");
                    builder.Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                }
                else
                {
                    builder.Append(ch);
                }
            }

            builder.Append('"');
            return builder.ToString();
        }

        private void SendJson(DeviceLink link, string jsonText)
        {
            SendFrame(link, 1, Encoding.UTF8.GetBytes(jsonText));
        }

        private static void SendFrame(DeviceLink link, int opcode, byte[] payload)
        {
            lock (link.SendLock)
            {
                if (!link.Open)
                {
                    return;
                }

                var header = new byte[payload.Length < 126 ? 2 : 4];
                header[0] = (byte)(0x80 | opcode);
                if (payload.Length < 126)
                {
                    header[1] = (byte)payload.Length;
                }
                else
                {
                    header[1] = 126;
                    header[2] = (byte)((payload.Length >> 8) & 0xff);
                    header[3] = (byte)(payload.Length & 0xff);
                }

                link.Stream.Write(header, 0, header.Length);
                link.Stream.Write(payload, 0, payload.Length);
                link.Stream.Flush();
            }
        }

        private static bool AcceptUpgrade(DeviceLink link)
        {
            var buffer = new byte[8192];
            int count = 0;
            while (count < buffer.Length)
            {
                int read = link.Stream.Read(buffer, count, buffer.Length - count);
                if (read <= 0)
                {
                    return false;
                }

                count += read;
                if (Encoding.ASCII.GetString(buffer, 0, count).Contains("\r\n\r\n"))
                {
                    break;
                }
            }

            string header = Encoding.ASCII.GetString(buffer, 0, count);
            string key = HeaderValue(header, "Sec-WebSocket-Key");
            if (key.Length == 0)
            {
                return false;
            }

            string accept;
            using (SHA1 sha = SHA1.Create())
            {
                accept = Convert.ToBase64String(sha.ComputeHash(Encoding.ASCII.GetBytes(key + SocketGuid)));
            }

            string response = "HTTP/1.1 101 Switching Protocols\r\n"
                + "Upgrade: websocket\r\n"
                + "Connection: Upgrade\r\n"
                + "Sec-WebSocket-Accept: " + accept + "\r\n\r\n";
            byte[] bytes = Encoding.ASCII.GetBytes(response);
            link.Stream.Write(bytes, 0, bytes.Length);
            link.Stream.Flush();
            return true;
        }

        private static string HeaderValue(string header, string name)
        {
            string[] lines = header.Split(new[] { "\r\n" }, StringSplitOptions.None);
            foreach (string line in lines)
            {
                int split = line.IndexOf(':');
                if (split <= 0)
                {
                    continue;
                }

                if (line.Substring(0, split).Trim().Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    return line.Substring(split + 1).Trim();
                }
            }

            return string.Empty;
        }

        private static WsFrame ReadFrame(NetworkStream stream)
        {
            int first = stream.ReadByte();
            int second = stream.ReadByte();
            if (first < 0 || second < 0)
            {
                return null;
            }

            bool final = (first & 0x80) != 0;
            int opcode = first & 0x0f;
            bool masked = (second & 0x80) != 0;
            int length = second & 0x7f;
            if (length == 126)
            {
                int hi = stream.ReadByte();
                int lo = stream.ReadByte();
                if (hi < 0 || lo < 0)
                {
                    return null;
                }

                length = (hi << 8) | lo;
            }
            else if (length == 127)
            {
                return null;
            }

            if (length > 2 * 1024 * 1024)
            {
                return null;
            }

            byte[] mask = new byte[4];
            if (masked && ReadExact(stream, mask, 4) != 4)
            {
                return null;
            }

            var payload = new byte[length];
            if (length > 0 && ReadExact(stream, payload, length) != length)
            {
                return null;
            }

            if (masked)
            {
                for (int i = 0; i < payload.Length; i++)
                {
                    payload[i] = (byte)(payload[i] ^ mask[i % 4]);
                }
            }

            return new WsFrame { Final = final, Opcode = opcode, Payload = payload };
        }

        private static int ReadExact(NetworkStream stream, byte[] buffer, int length)
        {
            int count = 0;
            while (count < length)
            {
                int read = stream.Read(buffer, count, length - count);
                if (read <= 0)
                {
                    return count;
                }

                count += read;
            }

            return count;
        }

        private sealed class DeviceLink
        {
            public DeviceLink(TcpClient client)
            {
                Client = client;
                Stream = client.GetStream();
                Open = true;
            }

            public TcpClient Client { get; private set; }
            public NetworkStream Stream { get; private set; }
            public object SendLock = new object();
            public string Serial = string.Empty;
            public long InFlight;
            public bool Open;
        }

        private sealed class WsFrame
        {
            public bool Final;
            public int Opcode;
            public byte[] Payload;
        }
    }
}
