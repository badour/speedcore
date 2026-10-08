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

Each match shows student name, student ID, total, paid, remaining, and whether access is allowed.

Student ID is an exact match. Name, stage, and department match any part of the value. Stage and Department are not in the original field list. `Database/AllStudentValueTb.sql` adds those two nullable columns when you run it. Until then, student ID and student name still search the existing table.

## Database script

`Database/AllStudentValueTb.sql` creates `AllStudentValueTb` only when it is missing, then adds `Stage` and `Department` if those columns are not there. It does not delete existing rows.
