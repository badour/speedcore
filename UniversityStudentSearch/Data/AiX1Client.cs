using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Web;
using System.Web.Script.Serialization;

namespace UniversityStudentSearch.Data
{
    public sealed class DevicePerson
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Department { get; set; }
    }

    public sealed class DeviceReadResult
    {
        public List<DevicePerson> People { get; set; }
        public int ReportedTotal { get; set; }
        public string ErrorMessage { get; set; }

        public bool Succeeded
        {
            get { return string.IsNullOrEmpty(ErrorMessage); }
        }
    }

    public static class AiX1Client
    {
        private const int PageLength = 200;
        private const int MaxPages = 100;

        public static bool IsSafeHost(string host)
        {
            if (string.IsNullOrWhiteSpace(host) || host.Length > 253)
            {
                return false;
            }

            host = host.Trim();
            if (host.IndexOf("://", StringComparison.Ordinal) >= 0 || host.IndexOf('/') >= 0 || host.IndexOf('\\') >= 0 || host.IndexOf('@') >= 0 || host.IndexOf(' ') >= 0 || host.IndexOf(':') >= 0)
            {
                return false;
            }

            return Uri.CheckHostName(host) == UriHostNameType.Dns || Uri.CheckHostName(host) == UriHostNameType.IPv4;
        }

        public static DeviceReadResult ListPeople(DeviceRecord device)
        {
            var people = new List<DevicePerson>();
            int reported = 0;
            for (int index = 0; index < MaxPages; index++)
            {
                string query = "pass=" + Encode(device.Password) + "&personId=-1&length=" + PageLength.ToString(CultureInfo.InvariantCulture) + "&index=" + index.ToString(CultureInfo.InvariantCulture);
                DeviceResponse response = Send(device, "GET", "person/findByPage", query, null);
                bool usedFullList = false;
                if (!response.Succeeded && index == 0)
                {
                    response = Send(device, "GET", "person/find", "pass=" + Encode(device.Password) + "&id=-1", null);
                    usedFullList = response.Succeeded;
                }

                if (!response.Succeeded)
                {
                    return new DeviceReadResult
                    {
                        People = new List<DevicePerson>(),
                        ErrorMessage = response.ErrorMessage
                    };
                }

                int pageCount = AddPeople(people, response.Payload, ref reported);
                if (usedFullList || pageCount < PageLength)
                {
                    break;
                }
            }

            if (reported < people.Count)
            {
                reported = people.Count;
            }

            return new DeviceReadResult
            {
                People = people,
                ReportedTotal = reported,
                ErrorMessage = null
            };
        }

        public static string SavePerson(DeviceRecord device, string id, string name, string department, bool exists)
        {
            id = (id ?? string.Empty).Trim();
            name = (name ?? string.Empty).Trim();
            department = (department ?? string.Empty).Trim();
            if (id.Length == 0 || name.Length == 0)
            {
                return "The device needs a student ID and a name.";
            }

            string path = exists ? "person/update" : "person/create";
            DeviceResponse saved = SendPerson(device, path, id, name, department, true);
            if (saved.Succeeded)
            {
                return null;
            }

            if (saved.Unreachable)
            {
                return saved.ErrorMessage;
            }

            DeviceResponse plain = SendPerson(device, path, id, name, department, false);
            if (plain.Succeeded)
            {
                return department.Length == 0
                    ? null
                    : "The device saved the name and ID, but did not accept the department.";
            }

            string other = exists ? "person/create" : "person/update";
            DeviceResponse retry = SendPerson(device, other, id, name, department, true);
            if (retry.Succeeded)
            {
                return null;
            }

            return saved.ErrorMessage;
        }

        private static DeviceResponse SendPerson(DeviceRecord device, string path, string id, string name, string department, bool includeDepartment)
        {
            var person = new Dictionary<string, object>();
            person["id"] = id;
            person["name"] = name;
            if (includeDepartment)
            {
                person["tag"] = department;
            }

            var serializer = new JavaScriptSerializer();
            string json = serializer.Serialize(person);
            string body = "pass=" + Encode(device.Password) + "&person=" + Encode(json);
            return Send(device, "POST", path, null, body);
        }

        private static int AddPeople(List<DevicePerson> people, object payload, ref int reported)
        {
            IEnumerable rows = PeopleRows(payload, ref reported);
            if (rows == null)
            {
                return 0;
            }

            int count = 0;
            foreach (object row in rows)
            {
                var record = row as Dictionary<string, object>;
                if (record == null)
                {
                    continue;
                }

                string id = Text(record, "id");
                if (id.Length == 0 || id == "-1")
                {
                    continue;
                }

                people.Add(new DevicePerson
                {
                    Id = id,
                    Name = Text(record, "name"),
                    Department = FirstText(record, "department", "deptName", "departmentName", "tag", "remark")
                });
                count++;
            }

            return count;
        }

        private static IEnumerable PeopleRows(object payload, ref int reported)
        {
            var root = payload as Dictionary<string, object>;
            if (root == null || !root.ContainsKey("data"))
            {
                return null;
            }

            IEnumerable direct = AsRows(root["data"]);
            if (direct != null && !(root["data"] is Dictionary<string, object>))
            {
                reported = CountRows(direct);
                return direct;
            }

            var data = root["data"] as Dictionary<string, object>;
            if (data == null)
            {
                return null;
            }

            var page = data.ContainsKey("pageInfo") ? data["pageInfo"] as Dictionary<string, object> : null;
            if (page != null && page.ContainsKey("total"))
            {
                int total;
                if (int.TryParse(Convert.ToString(page["total"], CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out total))
                {
                    reported = total;
                }
            }

            return data.ContainsKey("personInfos") ? AsRows(data["personInfos"]) : null;
        }

        private static IEnumerable AsRows(object value)
        {
            if (value is string || value == null)
            {
                return null;
            }

            if (value is ArrayList || value is object[])
            {
                return (IEnumerable)value;
            }

            return null;
        }

        private static int CountRows(IEnumerable rows)
        {
            int count = 0;
            foreach (object ignored in rows)
            {
                count++;
            }

            return count;
        }

        private static DeviceResponse Send(DeviceRecord device, string method, string path, string query, string body)
        {
            if (device == null || !IsSafeHost(device.Host) || device.Port < 1 || device.Port > 65535)
            {
                return DeviceResponse.Fail("Enter a valid device address.");
            }

            var builder = new UriBuilder("http", device.Host.Trim(), device.Port, path);
            if (!string.IsNullOrEmpty(query))
            {
                builder.Query = query;
            }

            try
            {
                var request = (HttpWebRequest)WebRequest.Create(builder.Uri);
                request.Method = method;
                request.Timeout = 15000;
                request.ServicePoint.Expect100Continue = false;
                request.ReadWriteTimeout = 15000;
                request.AllowAutoRedirect = false;
                if (body != null)
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(body);
                    request.ContentType = "application/x-www-form-urlencoded; charset=utf-8";
                    request.ContentLength = bytes.Length;
                    using (Stream stream = request.GetRequestStream())
                    {
                        stream.Write(bytes, 0, bytes.Length);
                    }
                }

                using (var response = (HttpWebResponse)request.GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                {
                    return Parse(reader.ReadToEnd());
                }
            }
            catch (WebException ex)
            {
                var http = ex.Response as HttpWebResponse;
                if (http != null)
                {
                    using (var reader = new StreamReader(http.GetResponseStream(), Encoding.UTF8))
                    {
                        DeviceResponse parsed = Parse(reader.ReadToEnd());
                        if (!parsed.Succeeded && string.IsNullOrEmpty(parsed.ErrorMessage))
                        {
                            return DeviceResponse.Fail("The device returned HTTP " + ((int)http.StatusCode).ToString(CultureInfo.InvariantCulture) + ".");
                        }

                        return parsed;
                    }
                }

                return DeviceResponse.UnreachableFail("The device did not answer at " + device.Host + ":" + device.Port.ToString(CultureInfo.InvariantCulture) + ". Check the address, port 8090, and that this computer can reach the AI-X1.");
            }
        }

        private static DeviceResponse Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return DeviceResponse.Fail("The device returned an empty response.");
            }

            object payload;
            try
            {
                var serializer = new JavaScriptSerializer();
                serializer.MaxJsonLength = 8 * 1024 * 1024;
                payload = serializer.DeserializeObject(json);
            }
            catch (ArgumentException)
            {
                return DeviceResponse.Fail("The device did not return person data.");
            }

            var root = payload as Dictionary<string, object>;
            if (root == null)
            {
                return DeviceResponse.Fail("The device did not return person data.");
            }

            bool success = root.ContainsKey("success")
                ? IsTrue(root["success"])
                : root.ContainsKey("result") && Convert.ToString(root["result"], CultureInfo.InvariantCulture) == "1";
            if (!success)
            {
                string message = root.ContainsKey("msg") ? Convert.ToString(root["msg"], CultureInfo.InvariantCulture) : "The device rejected the request.";
                return DeviceResponse.Fail(string.IsNullOrWhiteSpace(message) ? "The device rejected the request." : message.Trim());
            }

            return DeviceResponse.Ok(payload);
        }

        private static string FirstText(Dictionary<string, object> record, params string[] keys)
        {
            foreach (string key in keys)
            {
                string value = Text(record, key);
                if (value.Length > 0)
                {
                    return value;
                }
            }

            return string.Empty;
        }

        private static string Text(Dictionary<string, object> record, string key)
        {
            foreach (string name in record.Keys)
            {
                if (!string.Equals(name, key, StringComparison.OrdinalIgnoreCase) || record[name] == null)
                {
                    continue;
                }

                return Convert.ToString(record[name], CultureInfo.InvariantCulture).Trim();
            }

            return string.Empty;
        }

        private static bool IsTrue(object value)
        {
            if (value is bool)
            {
                return (bool)value;
            }

            string text = Convert.ToString(value, CultureInfo.InvariantCulture);
            return text == "1" || string.Equals(text, "true", StringComparison.OrdinalIgnoreCase);
        }

        private static string Encode(string value)
        {
            return HttpUtility.UrlEncode(value ?? string.Empty);
        }

        private sealed class DeviceResponse
        {
            public bool Succeeded;
            public bool Unreachable;
            public string ErrorMessage;
            public object Payload;

            public static DeviceResponse Ok(object payload)
            {
                return new DeviceResponse { Succeeded = true, Payload = payload };
            }

            public static DeviceResponse Fail(string message)
            {
                return new DeviceResponse { ErrorMessage = message };
            }

            public static DeviceResponse UnreachableFail(string message)
            {
                return new DeviceResponse { Unreachable = true, ErrorMessage = message };
            }
        }
    }
}
