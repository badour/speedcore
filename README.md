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

The AI-X1 terminals do not connect to IIS. Each terminal uses Yunatt WebSocket and JSON on port **7792**, path `/pub/chat`, and connects to `GateDeviceBridge`. The bridge calls this site over HTTP JSON, and this site reads `UniversityDBiap`.

On each terminal, set the server request on, the server address to the computer running the bridge, and the server port to 7792. Device IP addresses are not used.

Pressing Sync queues student name, student ID, and department from the row with the greatest `RowID`. The first sync queues every current student. Later presses queue only students whose latest row is newer. The bridge sends `setuserinfo` to every connected terminal. That command does not include a face photo, and it does not delete people or attendance logs. A terminal that connects later receives the same students.

When a face is scanned, the terminal sends `sendlog`. The bridge asks `/gate/access`. The door opens only when the latest row has `IsAccessAllowed` set.

Run the bridge beside the site:

```
GateDeviceBridge.exe --middleware http://localhost:50931 --port 7792
```

Use the address of this site for `--middleware` when IIS is not on port 50931.

## Database script

`Database/AllStudentValueTb.sql` creates `AllStudentValueTb` only when it is missing, then adds any missing search columns. It does not delete existing rows. A new `RowID` column is an identity, so existing rows receive numbers automatically.
