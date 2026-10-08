using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;

namespace UniversityStudentSearch.Data
{
    public enum SearchField
    {
        StudentId,
        StudentName,
        Stage,
        Department
    }

    public sealed class StudentRow
    {
        public string StudentName { get; set; }
        public long? StudentID { get; set; }
        public decimal? TotalValue { get; set; }
        public decimal? PaidValue { get; set; }
        public decimal? RemainingValue { get; set; }
        public bool? IsAccessAllowed { get; set; }
        public string Stage { get; set; }
        public string Department { get; set; }
    }

    public sealed class StudentSearchResult
    {
        public List<StudentRow> Rows { get; set; }
        public int TotalCount { get; set; }
        public int PageIndex { get; set; }
        public bool HasStage { get; set; }
        public bool HasDepartment { get; set; }
        public string ErrorMessage { get; set; }

        public bool Succeeded
        {
            get { return string.IsNullOrEmpty(ErrorMessage); }
        }
    }

    public static class StudentRepository
    {
        public const int PageSize = 15;
        public const string ConnectionName = "UniversityDBiap";

        private static readonly string[] KnownColumns = new[]
        {
            "StudentName",
            "StudentID",
            "TotalValue",
            "PaidValue",
            "RemainingValue",
            "IsAccessAllowed",
            "Stage",
            "Department"
        };

        private static readonly string[] RequiredColumns = new[]
        {
            "StudentName",
            "StudentID",
            "TotalValue",
            "PaidValue",
            "RemainingValue",
            "IsAccessAllowed"
        };

        public static bool TryParseField(string value, out SearchField field)
        {
            switch ((value ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "id":
                    field = SearchField.StudentId;
                    return true;
                case "name":
                    field = SearchField.StudentName;
                    return true;
                case "stage":
                    field = SearchField.Stage;
                    return true;
                case "department":
                    field = SearchField.Department;
                    return true;
                default:
                    field = SearchField.StudentName;
                    return false;
            }
        }

        public static StudentSearchResult Search(SearchField field, string term, int pageIndex)
        {
            term = (term ?? string.Empty).Trim();
            if (pageIndex < 0)
            {
                pageIndex = 0;
            }

            if (pageIndex > 100000)
            {
                pageIndex = 100000;
            }

            if (term.Length == 0)
            {
                return Fail("Enter a student ID, name, stage, or department.");
            }

            if (term.Length > 200)
            {
                return Fail("Use 200 characters or fewer.");
            }

            if (field == SearchField.StudentId)
            {
                long studentId;
                if (!long.TryParse(term, NumberStyles.Integer, CultureInfo.InvariantCulture, out studentId))
                {
                    return Fail("Enter the student ID as a whole number.");
                }
            }

            var settings = ConfigurationManager.ConnectionStrings[ConnectionName];
            if (settings == null || string.IsNullOrWhiteSpace(settings.ConnectionString))
            {
                return Fail("The UniversityDBiap connection string is missing from Web.config.");
            }

            try
            {
                using (var connection = new SqlConnection(settings.ConnectionString))
                {
                    connection.Open();
                    var columns = LoadColumns(connection);
                    bool schemaKnown = columns.Count > 0;

                    if (schemaKnown)
                    {
                        foreach (var required in RequiredColumns)
                        {
                            if (!columns.ContainsKey(required))
                            {
                                return Fail("AllStudentValueTb is missing the " + required + " column.");
                            }
                        }
                    }

                    bool hasStage = columns.ContainsKey("Stage");
                    bool hasDepartment = columns.ContainsKey("Department");

                    if (schemaKnown && field == SearchField.Stage && !hasStage)
                    {
                        return Fail(MissingOptionalColumn("Stage"));
                    }

                    if (schemaKnown && field == SearchField.Department && !hasDepartment)
                    {
                        return Fail(MissingOptionalColumn("Department"));
                    }

                    bool selectStage = hasStage || (!schemaKnown && field == SearchField.Stage);
                    bool selectDepartment = hasDepartment || (!schemaKnown && field == SearchField.Department);
                    int total = Count(connection, field, term, columns);

                    int pages = total == 0 ? 1 : (int)Math.Ceiling(total / (double)PageSize);
                    if (pageIndex >= pages)
                    {
                        pageIndex = pages - 1;
                    }

                    var rows = total == 0
                        ? new List<StudentRow>()
                        : FetchPage(connection, field, term, columns, selectStage, selectDepartment, pageIndex);

                    return new StudentSearchResult
                    {
                        Rows = rows,
                        TotalCount = total,
                        PageIndex = pageIndex,
                        HasStage = selectStage,
                        HasDepartment = selectDepartment,
                        ErrorMessage = null
                    };
                }
            }
            catch (Exception ex)
            {
                if (IsConnectionFailure(ex))
                {
                    return Fail("SQL Server was not found on this computer. Start the local SQL Server service, then search again.");
                }

                var sql = ex as SqlException;
                if (sql != null)
                {
                    return Fail(Describe(sql, field));
                }

                return Fail("The search could not be completed. " + ex.Message);
            }
        }

        private static bool IsConnectionFailure(Exception ex)
        {
            string text = ex.ToString();
            string[] needles = new[]
            {
                "Snix_Connect",
                "network-related",
                "server was not found",
                "Error Locating Server/Instance",
                "No such host",
                "Connection refused",
                "actively refused"
            };

            foreach (string needle in needles)
            {
                if (text.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static Dictionary<string, string> LoadColumns(SqlConnection connection)
        {
            var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            using (var command = new SqlCommand(@"
SELECT COLUMN_NAME
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = N'AllStudentValueTb'", connection))
            {
                command.CommandTimeout = 30;
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string actual = reader.GetString(0);
                        foreach (var known in KnownColumns)
                        {
                            if (string.Equals(actual, known, StringComparison.OrdinalIgnoreCase))
                            {
                                found[known] = actual;
                            }
                        }
                    }
                }
            }

            return found;
        }

        private static int Count(SqlConnection connection, SearchField field, string term, IDictionary<string, string> columns)
        {
            string sql = "SELECT COUNT(*) FROM [AllStudentValueTb] WHERE " + Where(field, columns);
            using (var command = new SqlCommand(sql, connection))
            {
                command.CommandTimeout = 30;
                AddParameters(command, field, term, 0);
                object value = command.ExecuteScalar();
                return Convert.ToInt32(value, CultureInfo.InvariantCulture);
            }
        }

        private static List<StudentRow> FetchPage(
            SqlConnection connection,
            SearchField field,
            string term,
            IDictionary<string, string> columns,
            bool selectStage,
            bool selectDepartment,
            int pageIndex)
        {
            string name = Expr(columns, "StudentName");
            string id = Expr(columns, "StudentID");
            string total = Expr(columns, "TotalValue");
            string paid = Expr(columns, "PaidValue");
            string remaining = Expr(columns, "RemainingValue");
            string access = Expr(columns, "IsAccessAllowed");
            string stage = selectStage ? Expr(columns, "Stage") : "CAST(NULL AS nvarchar(400))";
            string department = selectDepartment ? Expr(columns, "Department") : "CAST(NULL AS nvarchar(400))";

            string sql = @"
SELECT
    StudentName,
    StudentID,
    TotalValue,
    PaidValue,
    RemainingValue,
    IsAccessAllowed,
    Stage,
    Department
FROM (
    SELECT
        " + name + @" AS StudentName,
        " + id + @" AS StudentID,
        " + total + @" AS TotalValue,
        " + paid + @" AS PaidValue,
        " + remaining + @" AS RemainingValue,
        " + access + @" AS IsAccessAllowed,
        " + stage + @" AS Stage,
        " + department + @" AS Department,
        ROW_NUMBER() OVER (ORDER BY " + name + ", " + id + @") AS RowNum
    FROM [AllStudentValueTb]
    WHERE " + Where(field, columns) + @"
) AS numbered
WHERE numbered.RowNum > @Offset AND numbered.RowNum <= @EndRow
ORDER BY numbered.RowNum";

            var rows = new List<StudentRow>();
            using (var command = new SqlCommand(sql, connection))
            {
                command.CommandTimeout = 30;
                AddParameters(command, field, term, pageIndex * PageSize);
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        rows.Add(new StudentRow
                        {
                            StudentName = ReadText(reader, "StudentName"),
                            StudentID = ReadInt64(reader, "StudentID"),
                            TotalValue = ReadDecimal(reader, "TotalValue"),
                            PaidValue = ReadDecimal(reader, "PaidValue"),
                            RemainingValue = ReadDecimal(reader, "RemainingValue"),
                            IsAccessAllowed = ReadBool(reader, "IsAccessAllowed"),
                            Stage = ReadText(reader, "Stage"),
                            Department = ReadText(reader, "Department")
                        });
                    }
                }
            }

            return rows;
        }

        private static void AddParameters(SqlCommand command, SearchField field, string term, int offset)
        {
            object idValue = 0L;
            object termValue = DBNull.Value;
            if (field == SearchField.StudentId)
            {
                idValue = long.Parse(term, NumberStyles.Integer, CultureInfo.InvariantCulture);
            }
            else
            {
                termValue = "%" + EscapeLike(term) + "%";
            }

            command.Parameters.Add("@StudentID", SqlDbType.BigInt).Value = idValue;
            var termParameter = command.Parameters.Add("@Term", SqlDbType.NVarChar, 420);
            termParameter.Value = termValue;
            command.Parameters.Add("@Offset", SqlDbType.Int).Value = offset;
            command.Parameters.Add("@EndRow", SqlDbType.Int).Value = offset + PageSize;
        }

        private static string Where(SearchField field, IDictionary<string, string> columns)
        {
            switch (field)
            {
                case SearchField.StudentId:
                    return Expr(columns, "StudentID") + " = @StudentID";
                case SearchField.StudentName:
                    return Expr(columns, "StudentName") + " LIKE @Term ESCAPE N'\\'";
                case SearchField.Stage:
                    return Expr(columns, "Stage") + " LIKE @Term ESCAPE N'\\'";
                case SearchField.Department:
                    return Expr(columns, "Department") + " LIKE @Term ESCAPE N'\\'";
                default:
                    throw new ArgumentOutOfRangeException("field");
            }
        }

        private static string Expr(IDictionary<string, string> columns, string name)
        {
            string actual;
            if (columns != null && columns.TryGetValue(name, out actual))
            {
                return Quote(actual);
            }

            return Quote(name);
        }

        private static string Quote(string identifier)
        {
            return "[" + (identifier ?? string.Empty).Replace("]", "]]") + "]";
        }

        private static string EscapeLike(string value)
        {
            var builder = new System.Text.StringBuilder(value.Length);
            foreach (char ch in value)
            {
                if (ch == '\\' || ch == '%' || ch == '_' || ch == '[')
                {
                    builder.Append('\\');
                }

                builder.Append(ch);
            }

            return builder.ToString();
        }

        private static string ReadText(IDataRecord reader, string name)
        {
            int ordinal = reader.GetOrdinal(name);
            if (reader.IsDBNull(ordinal))
            {
                return null;
            }

            string text = Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }

        private static long? ReadInt64(IDataRecord reader, string name)
        {
            int ordinal = reader.GetOrdinal(name);
            if (reader.IsDBNull(ordinal))
            {
                return null;
            }

            return Convert.ToInt64(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
        }

        private static decimal? ReadDecimal(IDataRecord reader, string name)
        {
            int ordinal = reader.GetOrdinal(name);
            if (reader.IsDBNull(ordinal))
            {
                return null;
            }

            return Convert.ToDecimal(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
        }

        private static bool? ReadBool(IDataRecord reader, string name)
        {
            int ordinal = reader.GetOrdinal(name);
            if (reader.IsDBNull(ordinal))
            {
                return null;
            }

            return Convert.ToBoolean(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
        }

        private static string MissingOptionalColumn(string column)
        {
            return column + " is not a column on AllStudentValueTb. Search by student ID or student name, or run Database/AllStudentValueTb.sql to add Stage and Department.";
        }

        private static string Describe(SqlException ex, SearchField field)
        {
            switch (ex.Number)
            {
                case 207:
                    if (field == SearchField.Stage)
                    {
                        return MissingOptionalColumn("Stage");
                    }

                    if (field == SearchField.Department)
                    {
                        return MissingOptionalColumn("Department");
                    }

                    return "A column required for this search is missing from AllStudentValueTb. " + ex.Message;
                case 208:
                    return "The table AllStudentValueTb was not found in UniversityDBiap.";
                case 229:
                    return "This Windows login can reach UniversityDBiap, but it is not allowed to read AllStudentValueTb.";
                case 4060:
                    return "SQL Server refused to open the UniversityDBiap database for this Windows login.";
                case 18456:
                    return "Windows authentication was rejected. The site runs as the current Windows user, and that user needs access to UniversityDBiap.";
                case -1:
                case -2:
                case 2:
                case 53:
                    return "SQL Server was not found on this computer. Start the local SQL Server service, then search again.";
                default:
                    if (ex.Message != null && ex.Message.IndexOf("OFFSET", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return "The database server could not run this search. " + ex.Message;
                    }

                    return "The search could not be completed. " + ex.Message;
            }
        }

        private static StudentSearchResult Fail(string message)
        {
            return new StudentSearchResult
            {
                Rows = new List<StudentRow>(),
                TotalCount = 0,
                PageIndex = 0,
                ErrorMessage = message
            };
        }
    }
}
