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
        private const string SnapshotKey = "aix1-snapshot";

        protected void Page_Load(object sender, EventArgs e)
        {
            Server.ScriptTimeout = 180;
            if (!IsPostBack)
            {
                txtPort.Text = "8090";
                BindDevices();
            }
        }

        protected void btnAddDevice_Click(object sender, EventArgs e)
        {
            int port;
            if (!int.TryParse((txtPort.Text ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out port))
            {
                ShowMessage("Enter the port as a whole number. AYTE AI-X1 uses 8090.", true);
                return;
            }

            string error = DeviceCatalog.Add(txtDeviceName.Text, txtHost.Text, port, txtPassword.Text);
            if (error != null)
            {
                ShowMessage(error, true);
                BindDevices();
                return;
            }

            txtDeviceName.Text = string.Empty;
            txtHost.Text = string.Empty;
            txtPort.Text = "8090";
            txtPassword.Text = string.Empty;
            ShowMessage("Device saved.", false);
            BindDevices();
        }

        protected void gvDevices_RowCommand(object sender, GridViewCommandEventArgs e)
        {
            string id = Convert.ToString(e.CommandArgument, CultureInfo.InvariantCulture);
            if (e.CommandName == "Remove")
            {
                DeviceCatalog.Remove(id);
                SyncSnapshot snapshot = CurrentSnapshot();
                if (snapshot != null && snapshot.DeviceId == id)
                {
                    Session.Remove(SnapshotKey);
                    pnlDiff.Visible = false;
                    lblSummary.Visible = false;
                }

                ShowMessage("Device removed.", false);
                BindDevices();
                return;
            }

            if (e.CommandName == "Test")
            {
                DeviceRecord device = DeviceCatalog.Find(id);
                if (device == null)
                {
                    ShowMessage("That device is no longer in the list.", true);
                    BindDevices();
                    return;
                }

                DeviceReadResult result = AiX1Client.ListPeople(device);
                if (!result.Succeeded)
                {
                    ShowMessage(result.ErrorMessage, true);
                    return;
                }

                ShowMessage(device.Name + " answered. " + result.ReportedTotal.ToString(CultureInfo.InvariantCulture) + " people are enrolled.", false);
            }
        }

        protected void btnCompare_Click(object sender, EventArgs e)
        {
            DeviceRecord device = SelectedDevice();
            if (device == null)
            {
                ShowMessage("Add an AI-X1 device first.", true);
                return;
            }

            IdentityListResult database = StudentRepository.ListIdentities();
            if (!database.Succeeded)
            {
                ShowMessage(database.ErrorMessage, true);
                return;
            }

            DeviceReadResult people = AiX1Client.ListPeople(device);
            if (!people.Succeeded)
            {
                ShowMessage(people.ErrorMessage, true);
                return;
            }

            SyncSnapshot snapshot = StudentSync.Compare(database.Rows, people.People);
            snapshot.DeviceId = device.Id;
            Session[SnapshotKey] = snapshot;
            gvDiff.PageIndex = 0;
            BindDiff();
            lblMessage.Visible = false;
        }

        protected void ddlFilter_SelectedIndexChanged(object sender, EventArgs e)
        {
            gvDiff.PageIndex = 0;
            BindDiff();
        }

        protected void gvDiff_PageIndexChanging(object sender, GridViewPageEventArgs e)
        {
            gvDiff.PageIndex = e.NewPageIndex;
            BindDiff();
        }

        protected void gvDiff_RowDataBound(object sender, GridViewRowEventArgs e)
        {
            if (e.Row.RowType != DataControlRowType.DataRow)
            {
                return;
            }

            var pick = e.Row.FindControl("chkPick") as CheckBox;
            if (pick != null)
            {
                pick.InputAttributes["class"] = "pick";
                pick.InputAttributes["aria-label"] = "Select student";
            }
        }

        protected void btnToDevice_Click(object sender, EventArgs e)
        {
            Apply(true);
        }

        protected void btnToDatabase_Click(object sender, EventArgs e)
        {
            Apply(false);
        }

        protected string Encode(object value)
        {
            string text = Convert.ToString(value, CultureInfo.InvariantCulture);
            return HttpUtility.HtmlEncode(string.IsNullOrWhiteSpace(text) ? "—" : text);
        }

        private void Apply(bool toDevice)
        {
            SyncSnapshot snapshot = CurrentSnapshot();
            DeviceRecord device = snapshot == null ? null : DeviceCatalog.Find(snapshot.DeviceId);
            if (snapshot == null || device == null)
            {
                ShowMessage("Compare the device again before writing changes.", true);
                pnlDiff.Visible = false;
                return;
            }

            var selected = new List<string>();
            foreach (GridViewRow row in gvDiff.Rows)
            {
                var pick = row.FindControl("chkPick") as CheckBox;
                if (pick == null || !pick.Checked || row.RowIndex < 0)
                {
                    continue;
                }

                int index = (gvDiff.PageIndex * gvDiff.PageSize) + row.RowIndex;
                List<SyncDifference> visible = Filter(snapshot);
                if (index >= 0 && index < visible.Count)
                {
                    selected.Add(visible[index].StudentId);
                }
            }

            if (selected.Count == 0)
            {
                ShowMessage("Select at least one student.", true);
                BindDiff();
                return;
            }

            int saved = 0;
            var failures = new List<string>();
            foreach (string studentId in selected)
            {
                SyncDifference difference = FindDifference(snapshot, studentId);
                if (difference == null)
                {
                    continue;
                }

                string error = toDevice ? WriteDevice(device, difference) : WriteDatabase(difference);
                if (error == null)
                {
                    saved++;
                    MarkSynced(snapshot, difference, toDevice);
                }
                else if (failures.Count < 3)
                {
                    failures.Add(studentId + ": " + error);
                }
            }

            Session[SnapshotKey] = snapshot;
            BindDiff();
            string summary = saved.ToString(CultureInfo.InvariantCulture) + (toDevice ? " written to the device." : " saved in the database.");
            if (failures.Count > 0)
            {
                summary += " " + string.Join(" ", failures.ToArray());
            }

            ShowMessage(summary, failures.Count > 0 && saved == 0);
        }

        private static string WriteDevice(DeviceRecord device, SyncDifference difference)
        {
            if (difference.Kind == SyncKind.OnlyOnDevice)
            {
                return "This student is already only on the device. Save them into the database instead.";
            }

            long id;
            if (!long.TryParse(difference.StudentId, NumberStyles.Integer, CultureInfo.InvariantCulture, out id))
            {
                return "The student ID is not a whole number.";
            }

            string name = string.IsNullOrWhiteSpace(difference.DatabaseName) ? difference.DeviceName : difference.DatabaseName;
            return AiX1Client.SavePerson(device, difference.StudentId, name, difference.DatabaseDepartment, difference.Kind != SyncKind.OnlyInDatabase);
        }

        private static string WriteDatabase(SyncDifference difference)
        {
            if (difference.Kind == SyncKind.OnlyInDatabase)
            {
                return "This student is already only in the database. Write them to the device instead.";
            }

            long id;
            if (!long.TryParse(difference.StudentId, NumberStyles.Integer, CultureInfo.InvariantCulture, out id))
            {
                return "This device ID is not a whole number, so it cannot be stored in StudentID.";
            }

            string name = string.IsNullOrWhiteSpace(difference.DeviceName) ? difference.DatabaseName : difference.DeviceName;
            string department = difference.DeviceDepartment;
            return StudentRepository.SaveIdentity(difference.RowID, id, name, department);
        }

        private static void MarkSynced(SyncSnapshot snapshot, SyncDifference difference, bool toDevice)
        {
            if (toDevice)
            {
                difference.DeviceName = string.IsNullOrWhiteSpace(difference.DatabaseName) ? difference.DeviceName : difference.DatabaseName;
                difference.DeviceDepartment = difference.DatabaseDepartment;
            }
            else
            {
                difference.DatabaseName = string.IsNullOrWhiteSpace(difference.DeviceName) ? difference.DatabaseName : difference.DeviceName;
                difference.DatabaseDepartment = difference.DeviceDepartment;
            }

            snapshot.Differences.Remove(difference);
            snapshot.MatchCount++;
        }

        private void BindDevices()
        {
            List<DeviceRecord> devices = DeviceCatalog.Load();
            gvDevices.DataSource = devices;
            gvDevices.DataBind();

            string selected = ddlDevice.SelectedValue;
            ddlDevice.Items.Clear();
            foreach (DeviceRecord device in devices)
            {
                ddlDevice.Items.Add(new ListItem(device.Name + " (" + device.Host + ")", device.Id));
            }

            if (ddlDevice.Items.FindByValue(selected) != null)
            {
                ddlDevice.SelectedValue = selected;
            }

            BindDiff();
        }

        private void BindDiff()
        {
            SyncSnapshot snapshot = CurrentSnapshot();
            if (snapshot == null)
            {
                pnlDiff.Visible = false;
                lblSummary.Visible = false;
                return;
            }

            List<SyncDifference> visible = Filter(snapshot);
            lblSummary.Visible = true;
            lblSummary.Text = HttpUtility.HtmlEncode(Summary(snapshot, visible.Count));
            pnlDiff.Visible = true;
            gvDiff.DataSource = visible;
            gvDiff.DataBind();
        }

        private List<SyncDifference> Filter(SyncSnapshot snapshot)
        {
            var visible = new List<SyncDifference>();
            foreach (SyncDifference difference in snapshot.Differences)
            {
                if (MatchesFilter(difference.Kind, ddlFilter.SelectedValue))
                {
                    visible.Add(difference);
                }
            }

            return visible;
        }

        private static bool MatchesFilter(SyncKind kind, string filter)
        {
            switch (filter)
            {
                case "device":
                    return kind == SyncKind.OnlyOnDevice;
                case "database":
                    return kind == SyncKind.OnlyInDatabase;
                case "changed":
                    return kind == SyncKind.NameDiffers || kind == SyncKind.DepartmentDiffers || kind == SyncKind.BothDiffer;
                default:
                    return true;
            }
        }

        private static string Summary(SyncSnapshot snapshot, int shown)
        {
            int onDevice = 0;
            int inDatabase = 0;
            int changed = 0;
            foreach (SyncDifference difference in snapshot.Differences)
            {
                if (difference.Kind == SyncKind.OnlyOnDevice)
                {
                    onDevice++;
                }
                else if (difference.Kind == SyncKind.OnlyInDatabase)
                {
                    inDatabase++;
                }
                else
                {
                    changed++;
                }
            }

            return onDevice.ToString(CultureInfo.InvariantCulture) + " only on the device, "
                + inDatabase.ToString(CultureInfo.InvariantCulture) + " only in the database, "
                + changed.ToString(CultureInfo.InvariantCulture) + " with a different name or department, "
                + snapshot.MatchCount.ToString(CultureInfo.InvariantCulture) + " already match. Showing "
                + shown.ToString(CultureInfo.InvariantCulture) + ".";
        }

        private DeviceRecord SelectedDevice()
        {
            return DeviceCatalog.Find(ddlDevice.SelectedValue);
        }

        private SyncSnapshot CurrentSnapshot()
        {
            return Session[SnapshotKey] as SyncSnapshot;
        }

        private static SyncDifference FindDifference(SyncSnapshot snapshot, string studentId)
        {
            foreach (SyncDifference difference in snapshot.Differences)
            {
                if (difference.StudentId == studentId)
                {
                    return difference;
                }
            }

            return null;
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
