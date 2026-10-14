/*
    UniversityDBiap student value table.

    The live search reads AllStudentValueTb. Create it only when the table
    is not already in the database. Do not drop an existing table.
*/

IF OBJECT_ID(N'dbo.AllStudentValueTb', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AllStudentValueTb
    (
        StudentName nvarchar(max) NULL,
        StudentID bigint NULL,
        TotalValue money NULL,
        PaidValue money NULL,
        RemainingValue money NULL,
        IsAccessAllowed bit NULL,
        YearName nvarchar(200) NULL,
        RowID bigint IDENTITY(1,1) NOT NULL
    );
END
GO

/*
    Stage and Department are not in the original field list.
    These nullable columns let the stage and department searches return rows.
    Existing rows stay valid. Running this again does not add the columns twice.
    The site also reads StageName, DepartmentName, DeptName, and a stage or
    department lookup table when those already hold the names.
*/
IF COL_LENGTH(N'dbo.AllStudentValueTb', N'Stage') IS NULL
    ALTER TABLE dbo.AllStudentValueTb ADD Stage nvarchar(200) NULL;
GO

IF COL_LENGTH(N'dbo.AllStudentValueTb', N'Department') IS NULL
    ALTER TABLE dbo.AllStudentValueTb ADD Department nvarchar(200) NULL;
GO

IF COL_LENGTH(N'dbo.AllStudentValueTb', N'YearName') IS NULL
    ALTER TABLE dbo.AllStudentValueTb ADD YearName nvarchar(200) NULL;
GO

/*
    RowID identifies each value row. When a student has several rows,
    the search keeps the one with the greatest RowID.
*/
IF COL_LENGTH(N'dbo.AllStudentValueTb', N'RowID') IS NULL
    ALTER TABLE dbo.AllStudentValueTb ADD RowID bigint IDENTITY(1,1) NOT NULL;
GO

/*
    Optional sample row. Uncomment after the table exists.

INSERT INTO dbo.AllStudentValueTb
    (StudentName, StudentID, TotalValue, PaidValue, RemainingValue, IsAccessAllowed)
VALUES
    (N'Example Student', 1001, 1500000, 500000, 1000000, 1);
*/
