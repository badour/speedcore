using System;
using System.Collections.Generic;
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

        protected void btnFind_Click(object sender, EventArgs e)
        {
            IdentityListResult students = StudentRepository.ListForSync(txtYear.Text, txtDepartment.Text);
            if (!students.Succeeded)
            {
                gvStudents.DataSource = null;
                gvStudents.DataBind();
                pnlStudents.Visible = false;
                ShowMessage(students.ErrorMessage, true);
                BindDevices();
                return;
            }

            gvStudents.DataSource = students.Rows;
            gvStudents.DataBind();
            pnlStudents.Visible = true;
            int count = students.Rows == null ? 0 : students.Rows.Count;
            string summary = count.ToString(CultureInfo.InvariantCulture)
                + (count == 1 ? " student has unset access." : " students have unset access.");
            if (students.Truncated)
            {
                summary += " Showing the first 1000.";
            }

            lblStudentSummary.Text = HttpUtility.HtmlEncode(summary);
            lblMessage.Visible = false;
            BindDevices();
        }

        protected void gvStudents_RowDataBound(object sender, GridViewRowEventArgs e)
        {
            if (e.Row.RowType != DataControlRowType.DataRow)
            {
                return;
            }

            var pick = e.Row.FindControl("chkPick") as CheckBox;
            var student = e.Row.DataItem as SyncStudent;
            if (pick == null || student == null)
            {
                return;
            }

            string name = string.IsNullOrWhiteSpace(student.StudentName) ? "student" : student.StudentName.Trim();
            pick.InputAttributes["aria-label"] = "Select " + name;
        }

        protected void btnSync_Click(object sender, EventArgs e)
        {
            var selected = new List<SyncStudent>();
            if (gvStudents.DataKeys != null)
            {
                foreach (GridViewRow row in gvStudents.Rows)
                {
                    var pick = row.FindControl("chkPick") as CheckBox;
                    if (pick == null || !pick.Checked || row.RowIndex < 0 || row.RowIndex >= gvStudents.DataKeys.Count)
                    {
                        continue;
                    }

                    DataKey key = gvStudents.DataKeys[row.RowIndex];
                    long rowId;
                    long studentId;
                    if (!long.TryParse(Convert.ToString(key.Values["RowID"], CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out rowId)
                        || !long.TryParse(Convert.ToString(key.Values["StudentID"], CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out studentId))
                    {
                        continue;
                    }

                    selected.Add(new SyncStudent
                    {
                        RowID = rowId,
                        StudentID = studentId,
                        StudentName = Convert.ToString(key.Values["StudentName"], CultureInfo.InvariantCulture),
                        Department = Convert.ToString(key.Values["Department"], CultureInfo.InvariantCulture)
                    });
                }
            }

            if (selected.Count == 0)
            {
                ShowMessage(pnlStudents.Visible
                    ? "Select at least one student."
                    : "Find students first, then select the names to send.", true);
                BindDevices();
                return;
            }

            SyncQueueResult queued = GateStore.QueueLatest(selected);
            BindDevices();
            if (!queued.Succeeded)
            {
                ShowMessage(queued.ErrorMessage, true);
                return;
            }

            string text = queued.StudentCount.ToString(CultureInfo.InvariantCulture)
                + " selected "
                + (queued.StudentCount == 1 ? "student" : "students")
                + " queued for "
                + queued.DeviceCount.ToString(CultureInfo.InvariantCulture)
                + (queued.DeviceCount == 1 ? " device." : " devices.");
            if (queued.DeviceCount == 0)
            {
                text += " They will be sent as soon as a terminal calls the bridge.";
            }

            if (queued.SkippedWithoutName > 0)
            {
                text += " " + queued.SkippedWithoutName.ToString(CultureInfo.InvariantCulture) + " selected rows had no name and were skipped.";
            }

            if (queued.StudentCount == 0)
            {
                text = "No new students. Each selected student's latest row is already queued.";
            }

            ShowMessage(text, false);
        }

        private void BindDevices()
        {
            gvDevices.DataSource = GateStore.ListDevices();
            gvDevices.DataBind();
            int count = gvDevices.Rows.Count;
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
