using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Hosting;
using System.Xml.Serialization;

namespace UniversityStudentSearch.Data
{
    public sealed class GateDevice
    {
        public string Serial { get; set; }
        public string LastSeen { get; set; }
        public int Waiting { get; set; }
    }

    public sealed class SyncQueueResult
    {
        public int StudentCount { get; set; }
        public int DeviceCount { get; set; }
        public int SkippedWithoutName { get; set; }
        public string ErrorMessage { get; set; }

        public bool Succeeded
        {
            get { return string.IsNullOrEmpty(ErrorMessage); }
        }
    }

    public sealed class GateCommand
    {
        public long Id { get; set; }
        public string Body { get; set; }
    }

    public static class GateStore
    {
        private static readonly object Gate = new object();
        private static readonly XmlSerializer Serializer = new XmlSerializer(typeof(IclockState));

        public static string PathOverride { get; set; }

        public static SyncQueueResult QueueLatest(IList<SyncStudent> students)
        {
            if (students == null)
            {
                students = new SyncStudent[0];
            }

            lock (Gate)
            {
                IclockState state = Load();
                int queued = 0;
                int skipped = 0;
                foreach (SyncStudent student in students)
                {
                    if (student == null || student.StudentID <= 0 || student.RowID <= 0)
                    {
                        continue;
                    }

                    string studentId = student.StudentID.ToString(CultureInfo.InvariantCulture);
                    string name = Clean(student.StudentName, 64);
                    if (name.Length == 0)
                    {
                        skipped++;
                        continue;
                    }

                    RosterEntry existing = FindRoster(state, studentId);
                    if (existing != null && existing.RowID >= student.RowID)
                    {
                        continue;
                    }

                    var entry = new RosterEntry
                    {
                        StudentId = studentId,
                        RowID = student.RowID,
                        Command = UserInfoCommand(studentId, name, Clean(student.Department, 40))
                    };
                    if (existing == null)
                    {
                        state.Roster.Add(entry);
                    }
                    else
                    {
                        existing.RowID = entry.RowID;
                        existing.Command = entry.Command;
                    }

                    foreach (DeviceState device in state.Devices)
                    {
                        Enqueue(state, device, entry);
                    }

                    queued++;
                }

                Save(state);
                return new SyncQueueResult
                {
                    StudentCount = queued,
                    DeviceCount = state.Devices.Count,
                    SkippedWithoutName = skipped
                };
            }
        }

        public static List<GateDevice> ListDevices()
        {
            lock (Gate)
            {
                IclockState state = Load();
                var devices = new List<GateDevice>();
                foreach (DeviceState device in state.Devices)
                {
                    devices.Add(new GateDevice
                    {
                        Serial = device.Serial,
                        LastSeen = device.LastSeenUtc,
                        Waiting = device.Pending.Count
                    });
                }

                devices.Sort(delegate(GateDevice left, GateDevice right)
                {
                    return string.Compare(left.Serial, right.Serial, StringComparison.OrdinalIgnoreCase);
                });
                return devices;
            }
        }

        public static List<GateCommand> Pending(string serial, int limit)
        {
            if (limit < 1)
            {
                limit = 1;
            }

            if (limit > 50)
            {
                limit = 50;
            }

            lock (Gate)
            {
                IclockState state = Load();
                DeviceState device = EnsureDevice(state, serial);
                device.LastSeenUtc = Now();
                Backfill(state, device);
                var commands = new List<GateCommand>();
                int count = Math.Min(limit, device.Pending.Count);
                for (int i = 0; i < count; i++)
                {
                    PendingCommand command = device.Pending[i];
                    commands.Add(new GateCommand { Id = command.Id, Body = command.Body });
                }

                Save(state);
                return commands;
            }
        }

        public static void Acknowledge(string serial, long commandId, bool succeeded)
        {
            lock (Gate)
            {
                IclockState state = Load();
                DeviceState device = FindDevice(state, serial);
                if (device == null)
                {
                    return;
                }

                device.LastSeenUtc = Now();
                if (succeeded)
                {
                    device.Pending.RemoveAll(delegate(PendingCommand command) { return command.Id == commandId; });
                }

                Save(state);
            }
        }

        public static void Touch(string serial)
        {
            lock (Gate)
            {
                IclockState state = Load();
                EnsureDevice(state, serial);
                Save(state);
            }
        }

        public static string UserInfoCommand(string studentId, string name, string department)
        {
            // backupnum 0 updates the person and does not send a face photo.
            return "{\"cmd\":\"setuserinfo\",\"enrollid\":" + studentId
                + ",\"name\":" + JsonQuote(name)
                + ",\"department\":" + JsonQuote(department)
                + ",\"backupnum\":0,\"admin\":0,\"record\":\"0\",\"enable\":true}";
        }

        public static string JsonQuote(string value)
        {
            if (value == null)
            {
                return "\"\"";
            }

            var builder = new StringBuilder(value.Length + 2);
            builder.Append('"');
            foreach (char ch in value)
            {
                switch (ch)
                {
                    case '\\':
                    case '"':
                        builder.Append('\\');
                        builder.Append(ch);
                        break;
                    case '\b':
                        builder.Append("\\b");
                        break;
                    case '\f':
                        builder.Append("\\f");
                        break;
                    default:
                        if (ch < 32)
                        {
                            builder.Append("\\u");
                            builder.Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append(ch);
                        }
                        break;
                }
            }

            builder.Append('"');
            return builder.ToString();
        }

        private static void Backfill(IclockState state, DeviceState device)
        {
            foreach (RosterEntry entry in state.Roster)
            {
                Enqueue(state, device, entry);
            }
        }

        private static void Enqueue(IclockState state, DeviceState device, RosterEntry entry)
        {
            string key = entry.StudentId + ":" + entry.RowID.ToString(CultureInfo.InvariantCulture);
            if (device.Delivered.Contains(key))
            {
                return;
            }

            string prefix = entry.StudentId + ":";
            device.Delivered.RemoveAll(delegate(string item)
            {
                return item != null && item.StartsWith(prefix, StringComparison.Ordinal);
            });
            device.Pending.RemoveAll(delegate(PendingCommand command) { return command.StudentId == entry.StudentId; });
            device.Pending.Add(new PendingCommand
            {
                Id = ++state.NextCommandId,
                StudentId = entry.StudentId,
                Body = entry.Command
            });
            device.Delivered.Add(key);
        }

        private static DeviceState EnsureDevice(IclockState state, string serial)
        {
            serial = (serial ?? string.Empty).Trim();
            DeviceState device = FindDevice(state, serial);
            if (device != null)
            {
                device.LastSeenUtc = Now();
                return device;
            }

            device = new DeviceState
            {
                Serial = serial,
                LastSeenUtc = Now()
            };
            state.Devices.Add(device);
            return device;
        }

        private static DeviceState FindDevice(IclockState state, string serial)
        {
            foreach (DeviceState device in state.Devices)
            {
                if (string.Equals(device.Serial, serial, StringComparison.OrdinalIgnoreCase))
                {
                    return device;
                }
            }

            return null;
        }

        private static RosterEntry FindRoster(IclockState state, string studentId)
        {
            foreach (RosterEntry entry in state.Roster)
            {
                if (entry.StudentId == studentId)
                {
                    return entry;
                }
            }

            return null;
        }

        private static string Clean(string value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(value.Length);
            foreach (char ch in value.Trim())
            {
                if (ch == '\t' || ch == '\r' || ch == '\n')
                {
                    builder.Append(' ');
                }
                else
                {
                    builder.Append(ch);
                }
            }

            string text = builder.ToString().Trim();
            if (text.Length > maxLength)
            {
                text = text.Substring(0, maxLength);
            }

            return text;
        }

        private static string Now()
        {
            return DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        private static IclockState Load()
        {
            string path = StatePath();
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return new IclockState();
            }

            using (var stream = File.OpenRead(path))
            {
                var state = Serializer.Deserialize(stream) as IclockState;
                return state ?? new IclockState();
            }
        }

        private static void Save(IclockState state)
        {
            string path = StatePath();
            string folder = Path.GetDirectoryName(path);
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            string temp = path + ".tmp";
            using (var stream = File.Create(temp))
            {
                Serializer.Serialize(stream, state);
            }

            if (File.Exists(path))
            {
                File.Delete(path);
            }

            File.Move(temp, path);
        }

        private static string StatePath()
        {
            if (!string.IsNullOrEmpty(PathOverride))
            {
                return PathOverride;
            }

            return HostingEnvironment.MapPath("~/App_Data/gate-state.xml");
        }
    }

    public sealed class IclockState
    {
        public long NextCommandId { get; set; }
        public List<RosterEntry> Roster { get; set; }
        public List<DeviceState> Devices { get; set; }

        public IclockState()
        {
            Roster = new List<RosterEntry>();
            Devices = new List<DeviceState>();
        }
    }

    public sealed class RosterEntry
    {
        public string StudentId { get; set; }
        public long RowID { get; set; }
        public string Command { get; set; }
    }

    public sealed class DeviceState
    {
        public string Serial { get; set; }
        public string LastSeenUtc { get; set; }
        public List<string> Delivered { get; set; }
        public List<PendingCommand> Pending { get; set; }

        public DeviceState()
        {
            Delivered = new List<string>();
            Pending = new List<PendingCommand>();
        }
    }

    public sealed class PendingCommand
    {
        public long Id { get; set; }
        public string StudentId { get; set; }
        public string Body { get; set; }
    }
}
