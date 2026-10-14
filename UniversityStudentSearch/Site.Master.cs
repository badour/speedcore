using System;
using System.Web.UI;

namespace UniversityStudentSearch
{
    public partial class SiteMaster : MasterPage
    {
        protected string NavClass(string page)
        {
            string path = Request.AppRelativeCurrentExecutionFilePath ?? string.Empty;
            return path.EndsWith(page, StringComparison.OrdinalIgnoreCase) ? "is-current" : string.Empty;
        }
    }
}
