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

Device sync sends student name, student ID, and department from `AllStudentValueTb` to every AYTE AI-X1 gate. Each student contributes the row with the greatest `RowID`. Pressing Sync queues students whose latest row is newer than the last sync. The first sync queues every current student.

The page does not ask for a device IP address. On each terminal, set the server host and port shown on the page. The terminal calls this site at `/iclock/cdata`, `/iclock/getrequest`, and `/iclock/devicecmd`. Sync queues the new students for every terminal that has already called, and a terminal that calls later receives the same roster. The device command updates the user record, so face photos and attendance logs already on the terminal stay in place.

## Database script

`Database/AllStudentValueTb.sql` creates `AllStudentValueTb` only when it is missing, then adds any missing search columns. It does not delete existing rows. A new `RowID` column is an identity, so existing rows receive numbers automatically.
