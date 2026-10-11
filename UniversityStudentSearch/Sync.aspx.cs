using System;
using System.Globalization;
using System.Web;
using System.Web.UI;
using System.Web.UI.WebControls;
using UniversityStudentSearch.Data;

namespace UniversityStudentSearch
{
    public partial class SyncPage : Page
    {
        protected string MiddlewareAddress
        {
            get { return Request.Url.GetLeftPart(UriPartial.Authority); }
        }

        protected void Page_Load(object sender, EventArgs e)
        {
            Server.ScriptTimeout = 180;
            if (!IsPostBack)
            {
                BindDevices();
            }
        }

        protected void btnSync_Click(object sender, EventArgs e)
        {
            IdentityListResult students = StudentRepository.ListIdentities();
            if (!students.Succeeded)
            {
                ShowMessage(students.ErrorMessage, true);
                BindDevices();
                return;
            }

            SyncQueueResult queued = GateStore.QueueLatest(students.Rows);
            BindDevices();
            if (!queued.Succeeded)
            {
                ShowMessage(queued.ErrorMessage, true);
                return;
            }

            string text = queued.StudentCount.ToString(CultureInfo.InvariantCulture)
                + " new students queued for "
                + queued.DeviceCount.ToString(CultureInfo.InvariantCulture)
                + (queued.DeviceCount == 1 ? " device." : " devices.");
            if (queued.DeviceCount == 0)
            {
                text += " They will be sent as soon as a terminal calls this server.";
            }

            if (queued.SkippedWithoutName > 0)
            {
                text += " " + queued.SkippedWithoutName.ToString(CultureInfo.InvariantCulture) + " rows had no name and were skipped.";
            }

            if (queued.StudentCount == 0)
            {
                text = "No new students. Each student's latest row is already queued.";
            }

            ShowMessage(text, false);
        }

        private void BindDevices()
        {
            gvDevices.DataSource = GateStore.ListDevices();
            gvDevices.DataBind();
            int count = gvDevices.Rows.Count;
            lblSummary.Visible = true;
            lblSummary.Text = HttpUtility.HtmlEncode(
                count.ToString(CultureInfo.InvariantCulture)
                + (count == 1 ? " device has called this server." : " devices have called this server."));
        }

        private void ShowMessage(string text, bool isError)
        {
            lblMessage.Visible = true;
            lblMessage.CssClass = isError ? "message message-error" : "message message-ok";
            lblMessage.Text = HttpUtility.HtmlEncode(text ?? string.Empty);
            lblMessage.Attributes["role"] = isError ? "alert" : "status";
        }
    }
}
