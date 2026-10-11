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
        public string YearName { get; set; }
        public long? RowID { get; set; }
    }

    public sealed class StudentSearchResult
    {
        public List<StudentRow> Rows { get; set; }
        public int TotalCount { get; set; }
        public int PageIndex { get; set; }
        public bool HasStage { get; set; }
        public bool HasDepartment { get; set; }
        public bool HasYearName { get; set; }
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
            "Department",
            "YearName",
            "RowID"
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

        public static string UpdateAccess(long rowId, bool isAccessAllowed)
        {
            var settings = ConfigurationManager.ConnectionStrings[ConnectionName];
            if (settings == null || string.IsNullOrWhiteSpace(settings.ConnectionString))
            {
                return "The UniversityDBiap connection string is missing from Web.config.";
            }

            try
            {
                using (var connection = new SqlConnection(settings.ConnectionString))
                {
                    connection.Open();
                    var columns = LoadColumns(connection);
                    if (columns.Count > 0 && !columns.ContainsKey("RowID"))
                    {
                        return "AllStudentValueTb needs a RowID column so access can be saved.";
                    }

                    if (columns.Count > 0 && !columns.ContainsKey("IsAccessAllowed"))
                    {
                        return "AllStudentValueTb is missing the IsAccessAllowed column.";
                    }

                    string sql = "UPDATE [AllStudentValueTb] SET " + Expr(columns, "IsAccessAllowed")
                        + " = @IsAccessAllowed WHERE " + Expr(columns, "RowID") + " = @RowID";
                    using (var command = new SqlCommand(sql, connection))
                    {
                        command.CommandTimeout = 30;
                        command.Parameters.Add("@IsAccessAllowed", SqlDbType.Bit).Value = isAccessAllowed;
                        command.Parameters.Add("@RowID", SqlDbType.BigInt).Value = rowId;
                        int affected = command.ExecuteNonQuery();
                        if (affected == 0)
                        {
                            return "That student row was not found.";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (IsConnectionFailure(ex))
                {
                    return "SQL Server was not found on this computer. Start the local SQL Server service, then search again.";
                }

                var sqlError = ex as SqlException;
                if (sqlError != null)
                {
                    return Describe(sqlError, SearchField.StudentId);
                }

                return "Access could not be saved. " + ex.Message;
            }

            return null;
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
                    bool hasYearName = columns.ContainsKey("YearName");
                    bool hasRowId = columns.ContainsKey("RowID");

                    if (schemaKnown && !hasRowId)
                    {
                        return Fail("AllStudentValueTb needs a RowID column so each student can show the latest row. Run Database/AllStudentValueTb.sql.");
                    }

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
                    bool selectYearName = hasYearName || !schemaKnown;
                    int total = Count(connection, field, term, columns, selectStage, selectDepartment, selectYearName);

                    int pages = total == 0 ? 1 : (int)Math.Ceiling(total / (double)PageSize);
                    if (pageIndex >= pages)
                    {
                        pageIndex = pages - 1;
                    }

                    var rows = total == 0
                        ? new List<StudentRow>()
                        : FetchPage(connection, field, term, columns, selectStage, selectDepartment, selectYearName, pageIndex);

                    return new StudentSearchResult
                    {
                        Rows = rows,
                        TotalCount = total,
                        PageIndex = pageIndex,
                        HasStage = selectStage,
                        HasDepartment = selectDepartment,
                        HasYearName = selectYearName,
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

        private static int Count(
            SqlConnection connection,
            SearchField field,
            string term,
            IDictionary<string, string> columns,
            bool selectStage,
            bool selectDepartment,
            bool selectYearName)
        {
            string sql = "SELECT COUNT(*) FROM (" + LatestRowsSql(columns, field, selectStage, selectDepartment, selectYearName) + ") AS latestRows";
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
            bool selectYearName,
            int pageIndex)
        {
            string sql = @"
SELECT
    StudentName,
    StudentID,
    YearName,
    TotalValue,
    PaidValue,
    RemainingValue,
    IsAccessAllowed,
    Stage,
    Department,
    RowID
FROM (
    SELECT
        latestRows.*,
        ROW_NUMBER() OVER (ORDER BY CAST(latestRows.StudentName AS nvarchar(400)), latestRows.StudentID) AS RowNum
    FROM (" + LatestRowsSql(columns, field, selectStage, selectDepartment, selectYearName) + @"
    ) AS latestRows
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
                            YearName = ReadText(reader, "YearName"),
                            TotalValue = ReadDecimal(reader, "TotalValue"),
                            PaidValue = ReadDecimal(reader, "PaidValue"),
                            RemainingValue = ReadDecimal(reader, "RemainingValue"),
                            IsAccessAllowed = ReadBool(reader, "IsAccessAllowed"),
                            Stage = ReadText(reader, "Stage"),
                            Department = ReadText(reader, "Department"),
                            RowID = ReadInt64(reader, "RowID")
                        });
                    }
                }
            }

            return rows;
        }

        private static string LatestRowsSql(
            IDictionary<string, string> columns,
            SearchField field,
            bool selectStage,
            bool selectDepartment,
            bool selectYearName)
        {
            string name = Expr(columns, "StudentName");
            string id = Expr(columns, "StudentID");
            string rowId = Expr(columns, "RowID");
            string total = Expr(columns, "TotalValue");
            string paid = Expr(columns, "PaidValue");
            string remaining = Expr(columns, "RemainingValue");
            string access = Expr(columns, "IsAccessAllowed");
            string stage = selectStage ? Expr(columns, "Stage") : "CAST(NULL AS nvarchar(400))";
            string department = selectDepartment ? Expr(columns, "Department") : "CAST(NULL AS nvarchar(400))";
            string yearName = selectYearName ? Expr(columns, "YearName") : "CAST(NULL AS nvarchar(400))";
            string studentKey = "CASE WHEN " + id + " IS NULL THEN CAST(" + name + " AS nvarchar(400)) ELSE CONVERT(nvarchar(30), " + id + ") END";

            return @"
SELECT
    StudentName,
    StudentID,
    YearName,
    TotalValue,
    PaidValue,
    RemainingValue,
    IsAccessAllowed,
    Stage,
    Department,
    RowID
FROM (
    SELECT
        " + name + @" AS StudentName,
        " + id + @" AS StudentID,
        " + yearName + @" AS YearName,
        " + total + @" AS TotalValue,
        " + paid + @" AS PaidValue,
        " + remaining + @" AS RemainingValue,
        " + access + @" AS IsAccessAllowed,
        " + stage + @" AS Stage,
        " + department + @" AS Department,
        " + rowId + @" AS RowID,
        ROW_NUMBER() OVER (PARTITION BY " + studentKey + " ORDER BY " + rowId + @" DESC) AS StudentRank
    FROM [AllStudentValueTb]
    WHERE " + Where(field, columns) + @"
) AS ranked
WHERE ranked.StudentRank = 1";
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
                    string missing = ex.Message ?? string.Empty;
                    if (missing.IndexOf("RowID", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return "AllStudentValueTb needs a RowID column so each student can show the latest row. Run Database/AllStudentValueTb.sql.";
                    }

                    if (missing.IndexOf("YearName", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return "AllStudentValueTb needs a YearName column. Run Database/AllStudentValueTb.sql.";
                    }

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
                    return "This Windows login can reach UniversityDBiap, but it is not allowed to read or update AllStudentValueTb.";
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
