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

namespace StudentSyncBridge
{
    public sealed class BridgeServer
    {
        private const string SocketGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
        private readonly string middleware;
        private readonly int port;
        private readonly string paymentHost;
        private readonly int paymentPort;
        private readonly JavaScriptSerializer json = new JavaScriptSerializer();
        private TcpListener listener;

        public BridgeServer(string middleware, int port, string paymentAddress)
        {
            this.middleware = middleware;
            this.port = port;
            string host;
            int payment;
            if (TryParsePayment(paymentAddress, out host, out payment))
            {
                paymentHost = host;
                paymentPort = payment;
            }
        }

        public bool PassesPaymentToGateBridge
        {
            get { return paymentHost != null; }
        }

        public string PaymentAddress
        {
            get { return paymentHost == null ? string.Empty : paymentHost + ":" + paymentPort.ToString(CultureInfo.InvariantCulture); }
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
                string requestPath = link.RequestPath;
                PaymentLink payment = OpenPayment(requestPath);
                link.Payment = payment;
                if (payment != null)
                {
                    Console.WriteLine("Payment checks stay with GateDeviceBridge at " + PaymentAddress);
                    ThreadPool.QueueUserWorkItem(delegate { PumpPayment(link, payment); });
                }

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
                        ForwardToPayment(link, frame.Opcode, frame.Payload);
                        break;
                    }

                    if (frame.Opcode == 9)
                    {
                        if (link.Payment != null)
                        {
                            ForwardToPayment(link, frame.Opcode, frame.Payload);
                        }
                        else
                        {
                            SendFrame(link, 10, frame.Payload);
                        }

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
                    bool handled = HandleText(link, text);
                    if (!handled)
                    {
                        ForwardToPayment(link, 1, Encoding.UTF8.GetBytes(text));
                    }
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
                if (link.Payment != null)
                {
                    link.Payment.Open = false;
                    link.Payment.Client.Close();
                }

                client.Close();
                if (!string.IsNullOrEmpty(link.Serial))
                {
                    Console.WriteLine("Terminal offline " + link.Serial);
                }
            }
        }

        private bool HandleText(DeviceLink link, string text)
        {
            Dictionary<string, object> message = Parse(text);
            if (message == null)
            {
                return link.Payment == null;
            }

            if (message.ContainsKey("cmd"))
            {
                string cmd = AsString(message, "cmd");
                if (cmd == "reg")
                {
                    link.Serial = AsString(message, "sn");
                    Console.WriteLine("Terminal registered " + link.Serial);
                    if (link.Payment != null)
                    {
                        return false;
                    }

                    SendJson(link, "{\"ret\":\"reg\",\"result\":true,\"cloudtime\":" + Quote(Now()) + "}");
                    SendJson(link, "{\"cmd\":\"setdevinfo\",\"server_verify\":1}");
                    Flush(link);
                    return true;
                }

                if (cmd == "sendlog")
                {
                    if (link.Payment != null)
                    {
                        if (!link.Payment.Open)
                        {
                            Console.WriteLine("Face scan was not passed on. GateDeviceBridge disconnected.");
                            return true;
                        }

                        return false;
                    }

                    if (!PassesPaymentToGateBridge)
                    {
                        AnswerAccess(link, message);
                        return true;
                    }

                    Console.WriteLine("Face scan was not answered here. Start GateDeviceBridge on " + PaymentAddress + " so it can send the payment result.");
                    return true;
                }

                if (link.Payment != null)
                {
                    return false;
                }

                if (cmd == "senduser")
                {
                    SendJson(link, "{\"ret\":\"senduser\",\"result\":true,\"cloudtime\":" + Quote(Now()) + "}");
                    return true;
                }

                SendJson(link, "{\"ret\":" + Quote(cmd) + ",\"result\":true,\"cloudtime\":" + Quote(Now()) + "}");
                return true;
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
                        Post("/Gate.ashx?op=ack", "{\"sn\":" + Quote(link.Serial) + ",\"id\":" + id.ToString(CultureInfo.InvariantCulture) + ",\"result\":true}");
                    }

                    Flush(link);
                    return true;
                }
            }

            return link.Payment == null;
        }

        private PaymentLink OpenPayment(string requestPath)
        {
            if (paymentHost == null)
            {
                return null;
            }

            TcpClient client = null;
            try
            {
                client = new TcpClient();
                client.Connect(paymentHost, paymentPort);
                NetworkStream stream = client.GetStream();
                if (!ClientUpgrade(stream, requestPath, paymentHost, paymentPort))
                {
                    client.Close();
                    Console.WriteLine("GateDeviceBridge at " + PaymentAddress + " did not accept the terminal.");
                    return null;
                }

                return new PaymentLink(client, stream);
            }
            catch (Exception ex)
            {
                if (client != null)
                {
                    client.Close();
                }

                Console.WriteLine("GateDeviceBridge is not running at " + PaymentAddress + ". " + ex.Message);
                return null;
            }
        }

        private void PumpPayment(DeviceLink link, PaymentLink payment)
        {
            try
            {
                while (link.Open && payment.Open)
                {
                    WsFrame frame = ReadFrame(payment.Stream);
                    if (frame == null || frame.Opcode == 8)
                    {
                        break;
                    }

                    SendFrame(link, frame.Opcode, frame.Payload);
                }
            }
            catch (Exception)
            {
            }
            finally
            {
                payment.Open = false;
            }
        }

        private static void ForwardToPayment(DeviceLink link, int opcode, byte[] payload)
        {
            PaymentLink payment = link.Payment;
            if (payment == null || !payment.Open)
            {
                return;
            }

            try
            {
                lock (payment.SendLock)
                {
                    if (!payment.Open)
                    {
                        return;
                    }

                    WriteClientFrame(payment.Stream, opcode, payload ?? new byte[0]);
                    payment.Stream.Flush();
                }
            }
            catch (Exception)
            {
                payment.Open = false;
            }
        }

        private static bool ClientUpgrade(NetworkStream stream, string requestPath, string host, int port)
        {
            byte[] keyBytes = new byte[16];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(keyBytes);
            }

            string key = Convert.ToBase64String(keyBytes);
            string path = string.IsNullOrEmpty(requestPath) ? "/pub/chat" : requestPath;
            string request = "GET " + path + " HTTP/1.1\r\n"
                + "Host: " + host + ":" + port.ToString(CultureInfo.InvariantCulture) + "\r\n"
                + "Upgrade: websocket\r\n"
                + "Connection: Upgrade\r\n"
                + "Sec-WebSocket-Key: " + key + "\r\n"
                + "Sec-WebSocket-Version: 13\r\n\r\n";
            byte[] bytes = Encoding.ASCII.GetBytes(request);
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush();

            var buffer = new byte[8192];
            int count = 0;
            while (count < buffer.Length)
            {
                int read = stream.Read(buffer, count, buffer.Length - count);
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

            string response = Encoding.ASCII.GetString(buffer, 0, count);
            return response.StartsWith("HTTP/1.1 101", StringComparison.OrdinalIgnoreCase);
        }

        private static void WriteClientFrame(NetworkStream stream, int opcode, byte[] payload)
        {
            byte[] mask = new byte[4];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(mask);
            }

            int headerLength = payload.Length < 126 ? 6 : 8;
            var frame = new byte[headerLength + payload.Length];
            frame[0] = (byte)(0x80 | opcode);
            if (payload.Length < 126)
            {
                frame[1] = (byte)(0x80 | payload.Length);
                Buffer.BlockCopy(mask, 0, frame, 2, 4);
            }
            else
            {
                frame[1] = (byte)(0x80 | 126);
                frame[2] = (byte)((payload.Length >> 8) & 0xff);
                frame[3] = (byte)(payload.Length & 0xff);
                Buffer.BlockCopy(mask, 0, frame, 4, 4);
            }

            int offset = headerLength;
            for (int i = 0; i < payload.Length; i++)
            {
                frame[offset + i] = (byte)(payload[i] ^ mask[i % 4]);
            }

            stream.Write(frame, 0, frame.Length);
        }

        private static bool TryParsePayment(string value, out string host, out int port)
        {
            host = null;
            port = 0;
            string text = (value ?? string.Empty).Trim();
            if (text.Length == 0 || text.Equals("off", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            int split = text.LastIndexOf(':');
            if (split <= 0 || split == text.Length - 1)
            {
                return false;
            }

            int parsed;
            if (!int.TryParse(text.Substring(split + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) || parsed < 1 || parsed > 65535)
            {
                return false;
            }

            host = text.Substring(0, split).Trim();
            port = parsed;
            return host.Length > 0;
        }

        private static string RequestPath(string header)
        {
            string line = header.Split(new[] { "\r\n" }, StringSplitOptions.None)[0];
            string[] parts = line.Split(' ');
            if (parts.Length >= 2 && parts[1].StartsWith("/", StringComparison.Ordinal))
            {
                return parts[1];
            }

            return "/pub/chat";
        }

        private void AnswerAccess(DeviceLink link, Dictionary<string, object> message)
        {
            long enrollId = FirstEnrollId(message);
            string raw = Post("/Gate.ashx?op=access", "{\"sn\":" + Quote(link.Serial) + ",\"enrollid\":" + enrollId.ToString(CultureInfo.InvariantCulture) + "}");
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

            string raw = Get("/Gate.ashx?op=commands&sn=" + Uri.EscapeDataString(link.Serial));
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
                Console.WriteLine("Middleware read failed " + middleware + path + ": " + ex.Message);
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
                Console.WriteLine("Middleware write failed " + middleware + path + ": " + ex.Message);
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
            link.RequestPath = RequestPath(header);
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
            public string RequestPath = "/pub/chat";
            public PaymentLink Payment;
            public long InFlight;
            public bool Open;
        }

        private sealed class PaymentLink
        {
            public PaymentLink(TcpClient client, NetworkStream stream)
            {
                Client = client;
                Stream = stream;
                Open = true;
            }

            public TcpClient Client { get; private set; }
            public NetworkStream Stream { get; private set; }
            public object SendLock = new object();
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
