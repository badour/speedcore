# Speed Core student search

ASP.NET Web Forms site for Alsheikh Altoosi University. It searches `AllStudentValueTb` in `UniversityDBiap` by student ID, student name, stage, or department.

## Open and run

1. Open `UniversityStudentSearch.sln` in Visual Studio 2019 or 2022.
2. Set `UniversityStudentSearch` as the startup project and press F5.
3. IIS Express runs as your Windows user. `Trusted_Connection` uses that user, so the login needs read access to `UniversityDBiap`.

The connection string in `UniversityStudentSearch/Web.config` is:

```
Server=.;Database=UniversityDBiap;Trusted_Connection=True;TrustServerCertificate=True
```

## What the search returns

Each match shows student name, student ID, year name, total, paid, remaining, and access. If a student has more than one row, the grid keeps the row with the greatest `RowID`.

Access on that row is a switch. Green means allowed and writes `IsAccessAllowed = 1`. Gray means not set and writes `IsAccessAllowed = 0`.

Student ID is an exact match. Name, stage, and department match any part of the value, including common Arabic letter variants. A student matches when any of their rows contains that name. The grid still shows the latest row, and Stage and Department show the latest non-empty name. Columns such as `StageName`, `DepartmentName`, and `DeptName` are used when those hold the names, and a stage or department lookup table is used when the student row stores an id. `Database/AllStudentValueTb.sql` adds `Stage`, `Department`, `YearName`, and `RowID` when those columns are missing. The Windows login needs permission to read the table and to update `IsAccessAllowed`.

## Device sync

The Device sync page compares student name, student ID, and department in `AllStudentValueTb` with people enrolled on an AYTE AI-X1 terminal. Students added on the terminal itself appear as only on the device. Students missing from the terminal appear as only in the database.

Each terminal is saved by IP address, port, and device password. These terminals answer the person API on port 8090. Department is written to the person remark and is not used to delete a person or replace a face photo. Saving into the database inserts a student or updates the latest row.

## Database script

`Database/AllStudentValueTb.sql` creates `AllStudentValueTb` only when it is missing, then adds any missing search columns. It does not delete existing rows. A new `RowID` column is an identity, so existing rows receive numbers automatically.
