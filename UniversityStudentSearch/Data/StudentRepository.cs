using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Text;

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
                    string schema;
                    var columns = LoadColumns(connection, out schema);
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

                    var stageColumns = new List<string>();
                    var departmentColumns = new List<string>();
                    NameLookup stageLookup = null;
                    NameLookup departmentLookup = null;
                    if (schemaKnown)
                    {
                        stageColumns = ResolveTextColumns(columns, NameKind.Stage);
                        departmentColumns = ResolveTextColumns(columns, NameKind.Department);
                        var catalog = LoadForeignColumns(connection, schema);
                        stageLookup = FindLookup(catalog, columns, NameKind.Stage);
                        departmentLookup = FindLookup(catalog, columns, NameKind.Department);
                    }
                    else
                    {
                        if (field == SearchField.Stage)
                        {
                            stageColumns.Add("Stage");
                        }

                        if (field == SearchField.Department)
                        {
                            departmentColumns.Add("Department");
                        }
                    }

                    bool hasStage = stageColumns.Count > 0 || stageLookup != null;
                    bool hasDepartment = departmentColumns.Count > 0 || departmentLookup != null;
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
                    int total = Count(connection, field, term, columns, selectStage, selectDepartment, selectYearName, stageColumns, departmentColumns, stageLookup, departmentLookup);

                    int pages = total == 0 ? 1 : (int)Math.Ceiling(total / (double)PageSize);
                    if (pageIndex >= pages)
                    {
                        pageIndex = pages - 1;
                    }

                    var rows = total == 0
                        ? new List<StudentRow>()
                        : FetchPage(connection, field, term, columns, selectStage, selectDepartment, selectYearName, stageColumns, departmentColumns, stageLookup, departmentLookup, pageIndex);

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

        private enum NameKind
        {
            Stage,
            Department
        }

        private sealed class NameLookup
        {
            public string SchemaName;
            public string TableName;
            public string IdColumn;
            public string NameColumn;
            public string StudentKeyColumn;
        }

        private sealed class ForeignColumn
        {
            public string SchemaName;
            public string TableName;
            public string ColumnName;
            public string DataType;
        }

        private static readonly string[] StageNameColumns = new[]
        {
            "StageName",
            "Stage_Name",
            "StageNm",
            "StagName",
            "AcademicStage",
            "StudyStage",
            "Stage"
        };

        private static readonly string[] DepartmentNameColumns = new[]
        {
            "DepartmentName",
            "Department_Name",
            "DeptName",
            "Dept_Name",
            "DepName",
            "Dep_Name",
            "Department",
            "Dept"
        };

        private static readonly string[] StageKeyColumns = new[]
        {
            "StageID",
            "Stage_ID",
            "StgID",
            "StageNo",
            "StageNumber"
        };

        private static readonly string[] DepartmentKeyColumns = new[]
        {
            "DepartmentID",
            "Department_ID",
            "DeptID",
            "Dept_ID",
            "DepID",
            "Dep_ID",
            "DepartmentNo",
            "DeptNo"
        };

        private static readonly string[] StageTableNames = new[]
        {
            "stage",
            "stages",
            "stagetb",
            "tblstage",
            "tblstages",
            "academicstage",
            "studystage",
            "stageinfo"
        };

        private static readonly string[] DepartmentTableNames = new[]
        {
            "department",
            "departments",
            "departmenttb",
            "tbldepartment",
            "tbldepartments",
            "dept",
            "depttb",
            "tbldept",
            "departmentinfo"
        };

        private static readonly string[] GenericNameColumns = new[]
        {
            "Name",
            "Title",
            "ArabicName",
            "ArName",
            "NameAr"
        };

        private static readonly int[] FoldFrom = new[]
        {
            0x0623, 0x0625, 0x0622, 0x0671, 0x0649, 0x0629, 0x0624, 0x0626
        };

        private static readonly int[] FoldTo = new[]
        {
            0x0627, 0x0627, 0x0627, 0x0627, 0x064A, 0x0647, 0x0648, 0x064A
        };

        private static readonly int[] StripMarks = new[]
        {
            0x0640, 0x064B, 0x064C, 0x064D, 0x064E, 0x064F, 0x0650, 0x0651, 0x0652, 0x0670
        };

        private static Dictionary<string, string> LoadColumns(SqlConnection connection)
        {
            string schema;
            return LoadColumns(connection, out schema);
        }

        private static Dictionary<string, string> LoadColumns(SqlConnection connection, out string schema)
        {
            var bySchema = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            using (var command = new SqlCommand(@"
SELECT TABLE_SCHEMA, COLUMN_NAME, DATA_TYPE
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = N'AllStudentValueTb'", connection))
            {
                command.CommandTimeout = 30;
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string tableSchema = reader.GetString(0);
                        string actual = reader.GetString(1);
                        string dataType = reader.GetString(2);
                        Dictionary<string, string> bucket;
                        if (!bySchema.TryGetValue(tableSchema, out bucket))
                        {
                            bucket = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                            bySchema[tableSchema] = bucket;
                        }

                        bucket[actual] = dataType;
                    }
                }
            }

            schema = "dbo";
            if (bySchema.Count == 0)
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            if (bySchema.ContainsKey("dbo"))
            {
                return bySchema["dbo"];
            }

            foreach (var pair in bySchema)
            {
                schema = pair.Key;
                return pair.Value;
            }

            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        private static List<ForeignColumn> LoadForeignColumns(SqlConnection connection, string schema)
        {
            var columns = new List<ForeignColumn>();
            using (var command = new SqlCommand(@"
SELECT TABLE_SCHEMA, TABLE_NAME, COLUMN_NAME, DATA_TYPE
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_SCHEMA = @Schema
  AND TABLE_NAME <> N'AllStudentValueTb'
  AND (
        LOWER(TABLE_NAME) LIKE N'%stage%'
        OR LOWER(TABLE_NAME) LIKE N'%dept%'
        OR LOWER(TABLE_NAME) LIKE N'%department%'
      )", connection))
            {
                command.CommandTimeout = 30;
                command.Parameters.Add("@Schema", SqlDbType.NVarChar, 128).Value = schema ?? "dbo";
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        columns.Add(new ForeignColumn
                        {
                            SchemaName = reader.GetString(0),
                            TableName = reader.GetString(1),
                            ColumnName = reader.GetString(2),
                            DataType = reader.GetString(3)
                        });
                    }
                }
            }

            return columns;
        }

        private static int Count(
            SqlConnection connection,
            SearchField field,
            string term,
            IDictionary<string, string> columns,
            bool selectStage,
            bool selectDepartment,
            bool selectYearName,
            IList<string> stageColumns,
            IList<string> departmentColumns,
            NameLookup stageLookup,
            NameLookup departmentLookup)
        {
            string sql = "SELECT COUNT(*) FROM (" + LatestRowsSql(columns, field, selectStage, selectDepartment, selectYearName, false, stageColumns, departmentColumns, stageLookup, departmentLookup) + ") AS latestRows";
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
            IList<string> stageColumns,
            IList<string> departmentColumns,
            NameLookup stageLookup,
            NameLookup departmentLookup,
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
    FROM (" + LatestRowsSql(columns, field, selectStage, selectDepartment, selectYearName, true, stageColumns, departmentColumns, stageLookup, departmentLookup) + @"
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
            bool selectYearName,
            bool includeLabels,
            IList<string> stageColumns,
            IList<string> departmentColumns,
            NameLookup stageLookup,
            NameLookup departmentLookup)
        {
            string name = Expr(columns, "StudentName");
            string id = Expr(columns, "StudentID");
            string rowId = Expr(columns, "RowID");
            string total = Expr(columns, "TotalValue");
            string paid = Expr(columns, "PaidValue");
            string remaining = Expr(columns, "RemainingValue");
            string access = Expr(columns, "IsAccessAllowed");
            string yearName = selectYearName ? Expr(columns, "YearName") : "CAST(NULL AS nvarchar(400))";
            string stage = selectStage && includeLabels
                ? LatestLabel("ranked", columns, stageColumns, stageLookup)
                : "CAST(NULL AS nvarchar(400))";
            string department = selectDepartment && includeLabels
                ? LatestLabel("ranked", columns, departmentColumns, departmentLookup)
                : "CAST(NULL AS nvarchar(400))";
            string studentKey = "CASE WHEN " + id + " IS NULL THEN CAST(" + name + " AS nvarchar(400)) ELSE CONVERT(nvarchar(30), " + id + ") END";

            // Any row can match the search. Money, access, and RowID stay on the latest row.
            return @"
SELECT
    ranked.StudentName,
    ranked.StudentID,
    ranked.YearName,
    ranked.TotalValue,
    ranked.PaidValue,
    ranked.RemainingValue,
    ranked.IsAccessAllowed,
    " + stage + @" AS Stage,
    " + department + @" AS Department,
    ranked.RowID
FROM (
    SELECT
        " + name + @" AS StudentName,
        " + id + @" AS StudentID,
        " + yearName + @" AS YearName,
        " + total + @" AS TotalValue,
        " + paid + @" AS PaidValue,
        " + remaining + @" AS RemainingValue,
        " + access + @" AS IsAccessAllowed,
        " + rowId + @" AS RowID,
        ROW_NUMBER() OVER (PARTITION BY " + studentKey + " ORDER BY " + rowId + @" DESC) AS StudentRank
    FROM [AllStudentValueTb]
) AS ranked
WHERE ranked.StudentRank = 1
  AND " + StudentFilter(field, columns, stageColumns, departmentColumns, stageLookup, departmentLookup);
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
                termValue = "%" + EscapeLike(NormalizeSearchText(term)) + "%";
            }

            command.Parameters.Add("@StudentID", SqlDbType.BigInt).Value = idValue;
            var termParameter = command.Parameters.Add("@Term", SqlDbType.NVarChar, 420);
            termParameter.Value = termValue;
            command.Parameters.Add("@Offset", SqlDbType.Int).Value = offset;
            command.Parameters.Add("@EndRow", SqlDbType.Int).Value = offset + PageSize;
        }

        private static string StudentFilter(
            SearchField field,
            IDictionary<string, string> columns,
            IList<string> stageColumns,
            IList<string> departmentColumns,
            NameLookup stageLookup,
            NameLookup departmentLookup)
        {
            switch (field)
            {
                case SearchField.StudentId:
                    return "EXISTS (SELECT 1 FROM [AllStudentValueTb] AS matched WHERE "
                        + SameStudent("matched", "ranked", columns)
                        + " AND matched." + Expr(columns, "StudentID") + " = @StudentID)";
                case SearchField.StudentName:
                    return "EXISTS (SELECT 1 FROM [AllStudentValueTb] AS matched WHERE "
                        + SameStudent("matched", "ranked", columns)
                        + " AND " + LikeExpr("matched." + Expr(columns, "StudentName")) + ")";
                case SearchField.Stage:
                    return ExistsLike(columns, stageColumns, stageLookup);
                case SearchField.Department:
                    return ExistsLike(columns, departmentColumns, departmentLookup);
                default:
                    throw new ArgumentOutOfRangeException("field");
            }
        }

        private static string ExistsLike(IDictionary<string, string> columns, IList<string> textColumns, NameLookup lookup)
        {
            var likes = new List<string>();
            if (textColumns != null)
            {
                foreach (string column in textColumns)
                {
                    likes.Add(LikeExpr("matched." + Quote(column)));
                }
            }

            string join = string.Empty;
            if (lookup != null)
            {
                join = " LEFT JOIN " + Quote(lookup.SchemaName) + "." + Quote(lookup.TableName)
                    + " AS nameLookup ON nameLookup." + Quote(lookup.IdColumn)
                    + " = matched." + Quote(lookup.StudentKeyColumn);
                likes.Add(LikeExpr("nameLookup." + Quote(lookup.NameColumn)));
            }

            if (likes.Count == 0)
            {
                return "1 = 0";
            }

            return "EXISTS (SELECT 1 FROM [AllStudentValueTb] AS matched" + join + " WHERE "
                + SameStudent("matched", "ranked", columns)
                + " AND (" + string.Join(" OR ", likes.ToArray()) + "))";
        }

        private static string RowLabel(IDictionary<string, string> columns, IList<string> textColumns, NameLookup lookup)
        {
            var parts = new List<string>();
            if (textColumns != null)
            {
                foreach (string column in textColumns)
                {
                    parts.Add("NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(400), " + Expr(columns, column) + "))), N'')");
                }
            }

            if (lookup != null)
            {
                string lkName = "lk." + Quote(lookup.NameColumn);
                string table = Quote(lookup.SchemaName) + "." + Quote(lookup.TableName);
                parts.Add("(SELECT TOP 1 NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(400), " + lkName + "))), N'') FROM "
                    + table + " AS lk WHERE lk." + Quote(lookup.IdColumn)
                    + " = [AllStudentValueTb]." + Quote(lookup.StudentKeyColumn) + ")");
            }

            if (parts.Count == 0)
            {
                return "CAST(NULL AS nvarchar(400))";
            }

            if (parts.Count == 1)
            {
                return parts[0];
            }

            return "COALESCE(" + string.Join(", ", parts.ToArray()) + ")";
        }

        private static string LatestLabel(string rankedAlias, IDictionary<string, string> columns, IList<string> textColumns, NameLookup lookup)
        {
            var parts = new List<string>();
            string rowId = Expr(columns, "RowID");
            if (textColumns != null)
            {
                foreach (string column in textColumns)
                {
                    string quoted = "src." + Quote(column);
                    parts.Add("(SELECT TOP 1 CONVERT(nvarchar(400), " + quoted + ") FROM [AllStudentValueTb] AS src WHERE "
                        + SameStudent("src", rankedAlias, columns)
                        + " AND NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(400), " + quoted + "))), N'') IS NOT NULL ORDER BY src."
                        + rowId + " DESC)");
                }
            }

            if (lookup != null)
            {
                string lkName = "lk." + Quote(lookup.NameColumn);
                string table = Quote(lookup.SchemaName) + "." + Quote(lookup.TableName);
                parts.Add("(SELECT TOP 1 CONVERT(nvarchar(400), " + lkName + ") FROM [AllStudentValueTb] AS src INNER JOIN "
                    + table + " AS lk ON lk." + Quote(lookup.IdColumn) + " = src." + Quote(lookup.StudentKeyColumn)
                    + " WHERE " + SameStudent("src", rankedAlias, columns)
                    + " AND NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(400), " + lkName + "))), N'') IS NOT NULL ORDER BY src."
                    + rowId + " DESC)");
            }

            if (parts.Count == 0)
            {
                return "CAST(NULL AS nvarchar(400))";
            }

            if (parts.Count == 1)
            {
                return parts[0];
            }

            return "COALESCE(" + string.Join(", ", parts.ToArray()) + ")";
        }

        private static string SameStudent(string sourceAlias, string rankedAlias, IDictionary<string, string> columns)
        {
            string sourceId = sourceAlias + "." + Expr(columns, "StudentID");
            string rankedId = rankedAlias + ".StudentID";
            string sourceName = "CAST(" + sourceAlias + "." + Expr(columns, "StudentName") + " AS nvarchar(400))";
            string rankedName = "CAST(" + rankedAlias + ".StudentName AS nvarchar(400))";
            return "((" + sourceId + " IS NOT NULL AND " + sourceId + " = " + rankedId + ") OR ("
                + sourceId + " IS NULL AND " + rankedId + " IS NULL AND " + sourceName + " = " + rankedName + "))";
        }

        private static string LikeExpr(string expression)
        {
            return NormalizedSql(expression) + " LIKE @Term ESCAPE N'\\'";
        }

        private static string NormalizedSql(string expression)
        {
            string result = "CONVERT(nvarchar(400), " + expression + ")";
            for (int i = 0; i < FoldFrom.Length; i++)
            {
                result = "REPLACE(" + result + ", NCHAR(" + FoldFrom[i].ToString(CultureInfo.InvariantCulture) + "), NCHAR(" + FoldTo[i].ToString(CultureInfo.InvariantCulture) + "))";
            }

            for (int i = 0; i < StripMarks.Length; i++)
            {
                result = "REPLACE(" + result + ", NCHAR(" + StripMarks[i].ToString(CultureInfo.InvariantCulture) + "), N'')";
            }

            return result;
        }

        private static string NormalizeSearchText(string value)
        {
            var builder = new StringBuilder(value.Length);
            foreach (char ch in value)
            {
                int code = ch;
                if (Array.IndexOf(StripMarks, code) >= 0)
                {
                    continue;
                }

                int fold = Array.IndexOf(FoldFrom, code);
                builder.Append(fold >= 0 ? (char)FoldTo[fold] : ch);
            }

            return builder.ToString();
        }

        private static List<string> ResolveTextColumns(IDictionary<string, string> columns, NameKind kind)
        {
            string[] preferred = kind == NameKind.Stage ? StageNameColumns : DepartmentNameColumns;
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (columns == null)
            {
                return result;
            }

            foreach (string candidate in preferred)
            {
                string actual = FindKey(columns, candidate);
                if (actual == null || !IsTextColumn(columns, actual) || !seen.Add(actual))
                {
                    continue;
                }

                result.Add(actual);
            }

            var extras = new List<string>();
            foreach (string actual in columns.Keys)
            {
                if (seen.Contains(actual) || !IsTextColumn(columns, actual) || !IsExtraName(actual, kind))
                {
                    continue;
                }

                extras.Add(actual);
            }

            extras.Sort(StringComparer.OrdinalIgnoreCase);
            result.AddRange(extras);
            return result;
        }

        private static bool IsExtraName(string columnName, NameKind kind)
        {
            string compact = Compact(columnName);
            if (compact.EndsWith("id") || compact.EndsWith("no") || compact.EndsWith("number") || compact.EndsWith("code"))
            {
                return false;
            }

            if (kind == NameKind.Stage)
            {
                return compact.Contains("stage");
            }

            return compact.Contains("department") || compact.Contains("dept") || (compact.StartsWith("dep") && compact.Contains("name"));
        }

        private static NameLookup FindLookup(IList<ForeignColumn> catalog, IDictionary<string, string> columns, NameKind kind)
        {
            string[] keyCandidates = kind == NameKind.Stage ? StageKeyColumns : DepartmentKeyColumns;
            string studentKey = FindIntegerColumn(columns, keyCandidates);
            if (studentKey == null)
            {
                string[] plainKeys = kind == NameKind.Stage
                    ? new[] { "Stage" }
                    : new[] { "Department", "Dept", "Dep" };
                studentKey = FindIntegerColumn(columns, plainKeys);
            }

            if (studentKey == null || catalog == null || catalog.Count == 0)
            {
                return null;
            }

            string[] tableNames = kind == NameKind.Stage ? StageTableNames : DepartmentTableNames;
            string[] nameColumns = kind == NameKind.Stage ? StageNameColumns : DepartmentNameColumns;
            string topic = kind == NameKind.Stage ? "stage" : "department";
            var tables = new Dictionary<string, List<ForeignColumn>>(StringComparer.OrdinalIgnoreCase);
            foreach (ForeignColumn column in catalog)
            {
                string key = column.SchemaName + "\n" + column.TableName;
                List<ForeignColumn> bucket;
                if (!tables.TryGetValue(key, out bucket))
                {
                    bucket = new List<ForeignColumn>();
                    tables[key] = bucket;
                }

                bucket.Add(column);
            }

            NameLookup best = null;
            int bestScore = 0;
            foreach (var pair in tables)
            {
                List<ForeignColumn> tableColumns = pair.Value;
                if (tableColumns.Count == 0)
                {
                    continue;
                }

                string compactTable = Compact(tableColumns[0].TableName);
                if (compactTable.Contains("student") || compactTable.Contains("value"))
                {
                    continue;
                }

                int exact = Array.IndexOf(tableNames, compactTable);
                bool loose = exact < 0 && (compactTable.Contains(topic) || (kind == NameKind.Department && compactTable.Contains("dept")));
                if (exact < 0 && !loose)
                {
                    continue;
                }

                string idColumn = FindLookupId(tableColumns, studentKey);
                string nameColumn = FindLookupName(tableColumns, nameColumns, exact >= 0);
                if (idColumn == null || nameColumn == null)
                {
                    continue;
                }

                int score = exact >= 0 ? 1000 - exact : 100;
                if (string.Equals(idColumn, studentKey, StringComparison.OrdinalIgnoreCase))
                {
                    score += 20;
                }

                if (score <= bestScore)
                {
                    continue;
                }

                bestScore = score;
                best = new NameLookup
                {
                    SchemaName = tableColumns[0].SchemaName,
                    TableName = tableColumns[0].TableName,
                    IdColumn = idColumn,
                    NameColumn = nameColumn,
                    StudentKeyColumn = studentKey
                };
            }

            return best;
        }

        private static string FindLookupId(IList<ForeignColumn> tableColumns, string studentKey)
        {
            foreach (ForeignColumn column in tableColumns)
            {
                if (string.Equals(column.ColumnName, studentKey, StringComparison.OrdinalIgnoreCase) && IsIntegerType(column.DataType))
                {
                    return column.ColumnName;
                }
            }

            foreach (ForeignColumn column in tableColumns)
            {
                if (string.Equals(column.ColumnName, "ID", StringComparison.OrdinalIgnoreCase) && IsIntegerType(column.DataType))
                {
                    return column.ColumnName;
                }
            }

            string onlyId = null;
            foreach (ForeignColumn column in tableColumns)
            {
                if (!IsIntegerType(column.DataType) || !Compact(column.ColumnName).EndsWith("id"))
                {
                    continue;
                }

                if (onlyId != null)
                {
                    return null;
                }

                onlyId = column.ColumnName;
            }

            return onlyId;
        }

        private static string FindLookupName(IList<ForeignColumn> tableColumns, string[] preferred, bool allowGeneric)
        {
            foreach (string candidate in preferred)
            {
                if (!allowGeneric && IsGenericName(candidate))
                {
                    continue;
                }

                foreach (ForeignColumn column in tableColumns)
                {
                    if (string.Equals(column.ColumnName, candidate, StringComparison.OrdinalIgnoreCase) && IsTextType(column.DataType))
                    {
                        return column.ColumnName;
                    }
                }
            }

            return null;
        }

        private static bool IsGenericName(string columnName)
        {
            foreach (string generic in GenericNameColumns)
            {
                if (string.Equals(columnName, generic, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string FindIntegerColumn(IDictionary<string, string> columns, string[] candidates)
        {
            foreach (string candidate in candidates)
            {
                string actual = FindKey(columns, candidate);
                if (actual != null && IsTextColumn(columns, actual) == false && IsIntegerColumn(columns, actual))
                {
                    return actual;
                }
            }

            return null;
        }

        private static bool IsIntegerColumn(IDictionary<string, string> columns, string actual)
        {
            string dataType;
            return columns != null && columns.TryGetValue(actual, out dataType) && IsIntegerType(dataType);
        }

        private static bool IsTextColumn(IDictionary<string, string> columns, string actual)
        {
            string dataType;
            if (columns == null || !columns.TryGetValue(actual, out dataType) || string.IsNullOrEmpty(dataType))
            {
                return true;
            }

            return IsTextType(dataType);
        }

        private static bool IsTextType(string dataType)
        {
            switch ((dataType ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "char":
                case "nchar":
                case "varchar":
                case "nvarchar":
                case "text":
                case "ntext":
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsIntegerType(string dataType)
        {
            switch ((dataType ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "bigint":
                case "int":
                case "smallint":
                case "tinyint":
                    return true;
                default:
                    return false;
            }
        }

        private static string FindKey(IDictionary<string, string> columns, string name)
        {
            if (columns == null)
            {
                return null;
            }

            foreach (string key in columns.Keys)
            {
                if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
                {
                    return key;
                }
            }

            return null;
        }

        private static string Compact(string name)
        {
            var builder = new StringBuilder();
            foreach (char ch in name ?? string.Empty)
            {
                if (ch == '_' || ch == ' ' || ch == '-')
                {
                    continue;
                }

                builder.Append(char.ToLowerInvariant(ch));
            }

            return builder.ToString();
        }

        private static string Expr(IDictionary<string, string> columns, string name)
        {
            string actual = FindKey(columns, name);
            return Quote(actual ?? name);
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

        public static IdentityListResult ListIdentities()
        {
            var settings = ConfigurationManager.ConnectionStrings[ConnectionName];
            if (settings == null || string.IsNullOrWhiteSpace(settings.ConnectionString))
            {
                return IdentityFail("The UniversityDBiap connection string is missing from Web.config.");
            }

            try
            {
                using (var connection = new SqlConnection(settings.ConnectionString))
                {
                    connection.Open();
                    string schema;
                    var columns = LoadColumns(connection, out schema);
                    if (columns.Count > 0)
                    {
                        if (!columns.ContainsKey("StudentName") || !columns.ContainsKey("StudentID"))
                        {
                            return IdentityFail("AllStudentValueTb is missing StudentName or StudentID.");
                        }

                        if (!columns.ContainsKey("RowID"))
                        {
                            return IdentityFail("AllStudentValueTb needs a RowID column so each student can show the latest row. Run Database/AllStudentValueTb.sql.");
                        }
                    }

                    var departmentColumns = columns.Count > 0
                        ? ResolveTextColumns(columns, NameKind.Department)
                        : new List<string>();
                    NameLookup departmentLookup = columns.Count > 0
                        ? FindLookup(LoadForeignColumns(connection, schema), columns, NameKind.Department)
                        : null;
                    string departmentColumn = departmentColumns.Count > 0 ? departmentColumns[0] : null;
                    string name = Expr(columns, "StudentName");
                    string id = Expr(columns, "StudentID");
                    string rowId = Expr(columns, "RowID");
                    string studentKey = "CASE WHEN " + id + " IS NULL THEN CAST(" + name + " AS nvarchar(400)) ELSE CONVERT(nvarchar(30), " + id + ") END";
                    string departmentExpr = RowLabel(columns, departmentColumns, departmentLookup);
                    string sql = @"
SELECT ranked.StudentName, ranked.StudentID, ranked.Department, ranked.RowID
FROM (
    SELECT
        " + name + @" AS StudentName,
        " + id + @" AS StudentID,
        " + rowId + @" AS RowID,
        " + departmentExpr + @" AS Department,
        ROW_NUMBER() OVER (PARTITION BY " + studentKey + " ORDER BY " + rowId + @" DESC) AS StudentRank
    FROM [AllStudentValueTb]
    WHERE " + id + @" IS NOT NULL
) AS ranked
WHERE ranked.StudentRank = 1
ORDER BY ranked.StudentName, ranked.StudentID";

                    var rows = new List<SyncStudent>();
                    using (var command = new SqlCommand(sql, connection))
                    {
                        command.CommandTimeout = 60;
                        using (var reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                long? studentId = ReadInt64(reader, "StudentID");
                                long? identity = ReadInt64(reader, "RowID");
                                if (!studentId.HasValue || !identity.HasValue)
                                {
                                    continue;
                                }

                                rows.Add(new SyncStudent
                                {
                                    RowID = identity.Value,
                                    StudentID = studentId.Value,
                                    StudentName = ReadText(reader, "StudentName"),
                                    Department = ReadText(reader, "Department")
                                });
                            }
                        }
                    }

                    return new IdentityListResult
                    {
                        Rows = rows,
                        DepartmentColumn = departmentColumn,
                        ErrorMessage = null
                    };
                }
            }
            catch (Exception ex)
            {
                if (IsConnectionFailure(ex))
                {
                    return IdentityFail("SQL Server was not found on this computer. Start the local SQL Server service, then try again.");
                }

                var sql = ex as SqlException;
                if (sql != null)
                {
                    return IdentityFail(Describe(sql, SearchField.Department));
                }

                return IdentityFail("The student list could not be read. " + ex.Message);
            }
        }

        public static string SaveIdentity(long? rowId, long studentId, string name, string department)
        {
            name = (name ?? string.Empty).Trim();
            department = string.IsNullOrWhiteSpace(department) ? null : department.Trim();
            if (name.Length == 0)
            {
                return "Student " + studentId.ToString(CultureInfo.InvariantCulture) + " has no name to save.";
            }

            if (name.Length > 400)
            {
                name = name.Substring(0, 400);
            }

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
                    string schema;
                    var columns = LoadColumns(connection, out schema);
                    if (columns.Count > 0 && !columns.ContainsKey("RowID"))
                    {
                        return "AllStudentValueTb needs a RowID column so each student can show the latest row. Run Database/AllStudentValueTb.sql.";
                    }

                    var departmentColumns = columns.Count > 0
                        ? ResolveTextColumns(columns, NameKind.Department)
                        : new List<string> { "Department" };
                    string departmentColumn = departmentColumns.Count > 0 ? departmentColumns[0] : null;
                    if (department != null && departmentColumn == null)
                    {
                        return "AllStudentValueTb has no department column, so the department name cannot be saved.";
                    }

                    if (department != null && departmentColumn != null)
                    {
                        int max = ColumnMaxLength(connection, schema, departmentColumn);
                        if (max > 0 && department.Length > max)
                        {
                            return "The department is longer than " + departmentColumn + " allows.";
                        }
                    }

                    string sql;
                    if (rowId.HasValue)
                    {
                        sql = "UPDATE [AllStudentValueTb] SET " + Expr(columns, "StudentName") + " = @StudentName";
                        if (departmentColumn != null)
                        {
                            sql += ", " + Quote(departmentColumn) + " = @Department";
                        }

                        sql += " WHERE " + Expr(columns, "RowID") + " = @RowID AND " + Expr(columns, "StudentID") + " = @StudentID";
                    }
                    else
                    {
                        sql = "INSERT INTO [AllStudentValueTb] (" + Expr(columns, "StudentName") + ", " + Expr(columns, "StudentID");
                        string values = "@StudentName, @StudentID";
                        if (departmentColumn != null)
                        {
                            sql += ", " + Quote(departmentColumn);
                            values += ", @Department";
                        }

                        sql += ") VALUES (" + values + ")";
                    }

                    using (var command = new SqlCommand(sql, connection))
                    {
                        command.CommandTimeout = 30;
                        command.Parameters.Add("@StudentName", SqlDbType.NVarChar, -1).Value = name;
                        command.Parameters.Add("@StudentID", SqlDbType.BigInt).Value = studentId;
                        command.Parameters.Add("@Department", SqlDbType.NVarChar, -1).Value = (object)department ?? DBNull.Value;
                        if (rowId.HasValue)
                        {
                            command.Parameters.Add("@RowID", SqlDbType.BigInt).Value = rowId.Value;
                        }

                        int affected = command.ExecuteNonQuery();
                        if (rowId.HasValue && affected == 0)
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
                    return "SQL Server was not found on this computer. Start the local SQL Server service, then try again.";
                }

                var sql = ex as SqlException;
                if (sql != null)
                {
                    return Describe(sql, SearchField.Department);
                }

                return "The student could not be saved. " + ex.Message;
            }

            return null;
        }

        private static int ColumnMaxLength(SqlConnection connection, string schema, string column)
        {
            using (var command = new SqlCommand(@"
SELECT CHARACTER_MAXIMUM_LENGTH
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_SCHEMA = @Schema
  AND TABLE_NAME = N'AllStudentValueTb'
  AND COLUMN_NAME = @Column", connection))
            {
                command.Parameters.Add("@Schema", SqlDbType.NVarChar, 128).Value = schema ?? "dbo";
                command.Parameters.Add("@Column", SqlDbType.NVarChar, 128).Value = column;
                object value = command.ExecuteScalar();
                if (value == null || value == DBNull.Value)
                {
                    return -1;
                }

                return Convert.ToInt32(value, CultureInfo.InvariantCulture);
            }
        }

        private static IdentityListResult IdentityFail(string message)
        {
            return new IdentityListResult
            {
                Rows = new List<SyncStudent>(),
                ErrorMessage = message
            };
        }

        private static string MissingOptionalColumn(string column)
        {
            if (string.Equals(column, "Stage", StringComparison.OrdinalIgnoreCase))
            {
                return "No stage name was found on AllStudentValueTb. The search uses Stage, StageName, or a stage lookup table. Search by student ID or name, or add one of those.";
            }

            return "No department name was found on AllStudentValueTb. The search uses Department, DepartmentName, DeptName, or a department lookup table. Search by student ID or name, or add one of those.";
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
