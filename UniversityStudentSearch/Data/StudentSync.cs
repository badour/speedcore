using System.Collections.Generic;

namespace UniversityStudentSearch.Data
{
    public sealed class SyncStudent
    {
        public long RowID { get; set; }
        public long StudentID { get; set; }
        public string StudentName { get; set; }
        public string YearName { get; set; }
        public string Department { get; set; }
    }

    public sealed class IdentityListResult
    {
        public List<SyncStudent> Rows { get; set; }
        public string DepartmentColumn { get; set; }
        public string ErrorMessage { get; set; }
        public bool Truncated { get; set; }

        public bool Succeeded
        {
            get { return string.IsNullOrEmpty(ErrorMessage); }
        }
    }

    public sealed class GateDecision
    {
        public bool Found { get; set; }
        public bool Allowed { get; set; }
        public string StudentName { get; set; }
        public string Department { get; set; }
        public string DeviceMessage { get; set; }
        public string ErrorMessage { get; set; }

        public bool Succeeded
        {
            get { return string.IsNullOrEmpty(ErrorMessage); }
        }
    }
}
