using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Web.Hosting;
using System.Xml;

namespace UniversityStudentSearch.Data
{
    public sealed class DeviceRecord
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Host { get; set; }
        public int Port { get; set; }
        public string Password { get; set; }
    }

    public static class DeviceCatalog
    {
        public static List<DeviceRecord> Load()
        {
            return Load(MapPath());
        }

        public static List<DeviceRecord> Load(string path)
        {
            var devices = new List<DeviceRecord>();
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return devices;
            }

            var document = new XmlDocument();
            document.Load(path);
            foreach (XmlElement node in document.GetElementsByTagName("device"))
            {
                int port;
                if (!int.TryParse(node.GetAttribute("port"), NumberStyles.Integer, CultureInfo.InvariantCulture, out port))
                {
                    port = 8090;
                }

                devices.Add(new DeviceRecord
                {
                    Id = node.GetAttribute("id"),
                    Name = node.GetAttribute("name"),
                    Host = node.GetAttribute("host"),
                    Port = port,
                    Password = node.GetAttribute("pass")
                });
            }

            return devices;
        }

        public static string Add(string name, string host, int port, string password)
        {
            name = (name ?? string.Empty).Trim();
            host = (host ?? string.Empty).Trim();
            password = password ?? string.Empty;
            if (name.Length == 0 || name.Length > 80)
            {
                return "Enter a device name of 80 characters or fewer.";
            }

            if (!AiX1Client.IsSafeHost(host))
            {
                return "Enter the device IP address or host name, without a web address.";
            }

            if (port < 1 || port > 65535)
            {
                return "Enter a port from 1 to 65535.";
            }

            if (password.Length > 64)
            {
                return "Use a device password of 64 characters or fewer.";
            }

            var devices = Load();
            foreach (var device in devices)
            {
                if (string.Equals(device.Host, host, StringComparison.OrdinalIgnoreCase) && device.Port == port)
                {
                    return "That address is already in the list.";
                }
            }

            devices.Add(new DeviceRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = name,
                Host = host,
                Port = port,
                Password = password
            });
            Save(devices);
            return null;
        }

        public static void Remove(string id)
        {
            var devices = Load();
            devices.RemoveAll(delegate(DeviceRecord device) { return device.Id == id; });
            Save(devices);
        }

        public static DeviceRecord Find(string id)
        {
            foreach (var device in Load())
            {
                if (device.Id == id)
                {
                    return device;
                }
            }

            return null;
        }

        private static void Save(List<DeviceRecord> devices)
        {
            string path = MapPath();
            string folder = Path.GetDirectoryName(path);
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            var settings = new XmlWriterSettings();
            settings.Indent = true;
            settings.Encoding = System.Text.Encoding.UTF8;
            string temp = path + ".tmp";
            using (var writer = XmlWriter.Create(temp, settings))
            {
                writer.WriteStartDocument();
                writer.WriteStartElement("devices");
                foreach (var device in devices)
                {
                    writer.WriteStartElement("device");
                    writer.WriteAttributeString("id", device.Id ?? string.Empty);
                    writer.WriteAttributeString("name", device.Name ?? string.Empty);
                    writer.WriteAttributeString("host", device.Host ?? string.Empty);
                    writer.WriteAttributeString("port", device.Port.ToString(CultureInfo.InvariantCulture));
                    writer.WriteAttributeString("pass", device.Password ?? string.Empty);
                    writer.WriteEndElement();
                }

                writer.WriteEndElement();
                writer.WriteEndDocument();
            }

            if (File.Exists(path))
            {
                File.Delete(path);
            }

            File.Move(temp, path);
        }

        private static string MapPath()
        {
            return HostingEnvironment.MapPath("~/App_Data/aix1-devices.xml");
        }
    }
}
