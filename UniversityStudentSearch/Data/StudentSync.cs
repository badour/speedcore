using System;
using System.Collections.Generic;
using System.Globalization;

namespace UniversityStudentSearch.Data
{
    public sealed class SyncStudent
    {
        public long RowID { get; set; }
        public long StudentID { get; set; }
        public string StudentName { get; set; }
        public string Department { get; set; }
    }

    public sealed class IdentityListResult
    {
        public List<SyncStudent> Rows { get; set; }
        public string DepartmentColumn { get; set; }
        public string ErrorMessage { get; set; }

        public bool Succeeded
        {
            get { return string.IsNullOrEmpty(ErrorMessage); }
        }
    }

    public enum SyncKind
    {
        OnlyOnDevice,
        OnlyInDatabase,
        NameDiffers,
        DepartmentDiffers,
        BothDiffer
    }

    [Serializable]
    public sealed class SyncDifference
    {
        public SyncKind Kind { get; set; }
        public string StudentId { get; set; }
        public long? RowID { get; set; }
        public string DatabaseName { get; set; }
        public string DeviceName { get; set; }
        public string DatabaseDepartment { get; set; }
        public string DeviceDepartment { get; set; }

        public string StatusText
        {
            get
            {
                switch (Kind)
                {
                    case SyncKind.OnlyOnDevice:
                        return "Only on the device";
                    case SyncKind.OnlyInDatabase:
                        return "Only in the database";
                    case SyncKind.NameDiffers:
                        return "Name differs";
                    case SyncKind.DepartmentDiffers:
                        return "Department differs";
                    default:
                        return "Name and department differ";
                }
            }
        }
    }

    [Serializable]
    public sealed class SyncSnapshot
    {
        public string DeviceId { get; set; }
        public int MatchCount { get; set; }
        public List<SyncDifference> Differences { get; set; }
    }

    public static class StudentSync
    {
        public static SyncSnapshot Compare(IList<SyncStudent> databaseRows, IList<DevicePerson> devicePeople)
        {
            var database = new Dictionary<string, SyncStudent>(StringComparer.Ordinal);
            if (databaseRows != null)
            {
                foreach (SyncStudent row in databaseRows)
                {
                    database[Canonical(row.StudentID.ToString(CultureInfo.InvariantCulture))] = row;
                }
            }

            var devices = new Dictionary<string, DevicePerson>(StringComparer.Ordinal);
            if (devicePeople != null)
            {
                foreach (DevicePerson person in devicePeople)
                {
                    string key = Canonical(person.Id);
                    if (key.Length > 0)
                    {
                        devices[key] = person;
                    }
                }
            }

            var differences = new List<SyncDifference>();
            int matches = 0;
            foreach (var pair in database)
            {
                DevicePerson person;
                if (!devices.TryGetValue(pair.Key, out person))
                {
                    differences.Add(FromDatabase(pair.Value, pair.Key, SyncKind.OnlyInDatabase));
                    continue;
                }

                bool nameDiffers = !Same(pair.Value.StudentName, person.Name);
                bool departmentDiffers = !Same(pair.Value.Department, person.Department);
                if (!nameDiffers && !departmentDiffers)
                {
                    matches++;
                    continue;
                }

                SyncKind kind = nameDiffers && departmentDiffers
                    ? SyncKind.BothDiffer
                    : nameDiffers ? SyncKind.NameDiffers : SyncKind.DepartmentDiffers;
                differences.Add(new SyncDifference
                {
                    Kind = kind,
                    StudentId = pair.Key,
                    RowID = pair.Value.RowID,
                    DatabaseName = pair.Value.StudentName,
                    DeviceName = person.Name,
                    DatabaseDepartment = pair.Value.Department,
                    DeviceDepartment = person.Department
                });
            }

            foreach (var pair in devices)
            {
                if (database.ContainsKey(pair.Key))
                {
                    continue;
                }

                differences.Add(new SyncDifference
                {
                    Kind = SyncKind.OnlyOnDevice,
                    StudentId = pair.Key,
                    DeviceName = pair.Value.Name,
                    DeviceDepartment = pair.Value.Department
                });
            }

            differences.Sort(delegate(SyncDifference left, SyncDifference right)
            {
                int kind = left.Kind.CompareTo(right.Kind);
                return kind != 0 ? kind : string.Compare(left.StudentId, right.StudentId, StringComparison.Ordinal);
            });

            return new SyncSnapshot
            {
                Differences = differences,
                MatchCount = matches
            };
        }

        public static string Canonical(string id)
        {
            id = (id ?? string.Empty).Trim();
            long number;
            if (long.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
            {
                return number.ToString(CultureInfo.InvariantCulture);
            }

            return id;
        }

        private static SyncDifference FromDatabase(SyncStudent row, string id, SyncKind kind)
        {
            return new SyncDifference
            {
                Kind = kind,
                StudentId = id,
                RowID = row.RowID,
                DatabaseName = row.StudentName,
                DatabaseDepartment = row.Department
            };
        }

        private static bool Same(string left, string right)
        {
            return string.Equals(Fold(left), Fold(right), StringComparison.OrdinalIgnoreCase);
        }

        private static string Fold(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var builder = new System.Text.StringBuilder(value.Length);
            foreach (char ch in value.Trim())
            {
                int code = ch;
                if (code == 0x0640 || (code >= 0x064B && code <= 0x0652) || code == 0x0670)
                {
                    continue;
                }

                if (code == 0x0623 || code == 0x0625 || code == 0x0622 || code == 0x0671)
                {
                    builder.Append('\u0627');
                }
                else if (code == 0x0649)
                {
                    builder.Append('\u064A');
                }
                else if (code == 0x0629)
                {
                    builder.Append('\u0647');
                }
                else if (code == 0x0624)
                {
                    builder.Append('\u0648');
                }
                else if (code == 0x0626)
                {
                    builder.Append('\u064A');
                }
                else if (!char.IsWhiteSpace(ch))
                {
                    builder.Append(char.ToLowerInvariant(ch));
                }
            }

            return builder.ToString();
        }
    }
}
