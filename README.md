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

Access can be changed on that row. Allowed writes `IsAccessAllowed = 1`. Not set writes `IsAccessAllowed = 0`.

Student ID is an exact match. Name, stage, and department match any part of the value. `Database/AllStudentValueTb.sql` adds `Stage`, `Department`, `YearName`, and `RowID` when those columns are missing. The Windows login needs permission to read the table and to update `IsAccessAllowed`.

## Database script

`Database/AllStudentValueTb.sql` creates `AllStudentValueTb` only when it is missing, then adds any missing search columns. It does not delete existing rows. A new `RowID` column is an identity, so existing rows receive numbers automatically.
