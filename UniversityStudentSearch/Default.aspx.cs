using System;
using System.Globalization;
using System.Web;
using System.Web.UI;
using System.Web.UI.WebControls;
using UniversityStudentSearch.Data;

namespace UniversityStudentSearch
{
    public partial class _Default : Page
    {
        private const string ActiveKey = "active";
        private const string FieldKey = "field";
        private const string TermKey = "term";
        private const string PageKey = "page";

        private bool HasSearch
        {
            get { return ViewState[ActiveKey] is bool && (bool)ViewState[ActiveKey]; }
            set { ViewState[ActiveKey] = value; }
        }

        private string CurrentField
        {
            get { return ViewState[FieldKey] as string ?? "id"; }
            set { ViewState[FieldKey] = value; }
        }

        private string CurrentTerm
        {
            get { return ViewState[TermKey] as string ?? string.Empty; }
            set { ViewState[TermKey] = value; }
        }

        private int PageIndex
        {
            get { return ViewState[PageKey] is int ? (int)ViewState[PageKey] : 0; }
            set { ViewState[PageKey] = value; }
        }

        protected void Page_Load(object sender, EventArgs e)
        {
            ApplyFieldHints();
        }

        protected void btnSearch_Click(object sender, EventArgs e)
        {
            SearchField field;
            if (!StudentRepository.TryParseField(ddlField.SelectedValue, out field))
            {
                ShowMessage("Choose student ID, name, stage, or department.", true);
                pnlResults.Visible = false;
                return;
            }

            string term = (txtTerm.Text ?? string.Empty).Trim();
            if (term.Length == 0)
            {
                ShowMessage("Enter a student ID, name, stage, or department.", true);
                pnlResults.Visible = false;
                return;
            }

            if (term.Length > 200)
            {
                ShowMessage("Use 200 characters or fewer.", true);
                pnlResults.Visible = false;
                return;
            }

            if (field == SearchField.StudentId && !IsStudentId(term))
            {
                ShowMessage("Enter the student ID as a whole number.", true);
                pnlResults.Visible = false;
                return;
            }

            CurrentField = ddlField.SelectedValue;
            CurrentTerm = term;
            PageIndex = 0;
            HasSearch = true;
            BindGrid();
        }

        protected void btnClear_Click(object sender, EventArgs e)
        {
            ddlField.SelectedValue = "id";
            txtTerm.Text = string.Empty;
            HasSearch = false;
            CurrentTerm = string.Empty;
            PageIndex = 0;
            lblMessage.Visible = false;
            pnlResults.Visible = false;
            ApplyFieldHints();
        }

        protected void btnPrev_Click(object sender, EventArgs e)
        {
            if (!HasSearch || PageIndex <= 0)
            {
                return;
            }

            PageIndex -= 1;
            BindGrid();
        }

        protected void btnNext_Click(object sender, EventArgs e)
        {
            if (!HasSearch)
            {
                return;
            }

            PageIndex += 1;
            BindGrid();
        }

        protected void gvStudents_PreRender(object sender, EventArgs e)
        {
            if (gvStudents.Rows.Count > 0)
            {
                gvStudents.HeaderRow.TableSection = TableRowSection.TableHeader;
            }
        }

        protected string NameText(object value)
        {
            return TextOrDash(value);
        }

        protected string TextOrDash(object value)
        {
            string text = Convert.ToString(value, CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(text))
            {
                return "—";
            }

            return HttpUtility.HtmlEncode(text);
        }

        protected string IdText(object value)
        {
            if (value == null || value == DBNull.Value)
            {
                return "—";
            }

            return HttpUtility.HtmlEncode(
                Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture));
        }

        protected string MoneyText(object value)
        {
            if (value == null || value == DBNull.Value)
            {
                return "—";
            }

            return HttpUtility.HtmlEncode(
                Convert.ToDecimal(value, CultureInfo.InvariantCulture).ToString("N2", CultureInfo.InvariantCulture));
        }

        protected string AccessBadge(object value)
        {
            if (value == null || value == DBNull.Value)
            {
                return "<span class=\"badge badge-unknown\">Not set</span>";
            }

            bool allowed = Convert.ToBoolean(value, CultureInfo.InvariantCulture);
            if (allowed)
            {
                return "<span class=\"badge badge-allowed\">Allowed</span>";
            }

            return "<span class=\"badge badge-blocked\">Not allowed</span>";
        }

        private void BindGrid()
        {
            SearchField field;
            if (!StudentRepository.TryParseField(CurrentField, out field))
            {
                ShowMessage("Choose student ID, name, stage, or department.", true);
                pnlResults.Visible = false;
                return;
            }

            StudentSearchResult result = StudentRepository.Search(field, CurrentTerm, PageIndex);
            if (!result.Succeeded)
            {
                ShowMessage(result.ErrorMessage, true);
                pnlResults.Visible = false;
                return;
            }

            PageIndex = result.PageIndex;
            gvStudents.Columns[2].Visible = result.HasStage;
            gvStudents.Columns[3].Visible = result.HasDepartment;

            if (result.TotalCount == 0)
            {
                ShowMessage("No student matched " + MatchSentence() + ".", false);
                pnlResults.Visible = false;
                return;
            }

            lblMessage.Visible = false;
            pnlResults.Visible = true;
            gvStudents.DataSource = result.Rows;
            gvStudents.DataBind();

            int from = (result.PageIndex * StudentRepository.PageSize) + 1;
            int to = from + result.Rows.Count - 1;
            lblSummary.Text = HttpUtility.HtmlEncode(
                "Showing " + from.ToString(CultureInfo.InvariantCulture)
                + "–" + to.ToString(CultureInfo.InvariantCulture)
                + " of " + result.TotalCount.ToString(CultureInfo.InvariantCulture)
                + " for " + MatchSentence());

            int pages = (int)Math.Ceiling(result.TotalCount / (double)StudentRepository.PageSize);
            pnlPager.Visible = pages > 1;
            lblPage.Text = "Page " + (result.PageIndex + 1).ToString(CultureInfo.InvariantCulture)
                + " of " + pages.ToString(CultureInfo.InvariantCulture);
            btnPrev.Enabled = result.PageIndex > 0;
            btnNext.Enabled = result.PageIndex < pages - 1;
        }

        private void ShowMessage(string text, bool isError)
        {
            lblMessage.Visible = true;
            lblMessage.CssClass = isError ? "message message-error" : "message";
            lblMessage.Text = HttpUtility.HtmlEncode(text ?? string.Empty);
            lblMessage.Attributes["role"] = isError ? "alert" : "status";
        }

        private void ApplyFieldHints()
        {
            string field = ddlField.SelectedValue;
            litHint.Text = HintFor(field);
            txtTerm.Attributes["placeholder"] = PlaceholderFor(field);
            txtTerm.Attributes["inputmode"] = field == "id" ? "numeric" : "search";
            txtTerm.Attributes["autocomplete"] = "off";
            txtTerm.Attributes["spellcheck"] = field == "id" ? "false" : "true";
        }

        private static bool IsStudentId(string term)
        {
            long id;
            return long.TryParse(term, NumberStyles.Integer, CultureInfo.InvariantCulture, out id);
        }

        private string MatchSentence()
        {
            string term = CurrentTerm ?? string.Empty;
            switch (CurrentField)
            {
                case "id":
                    return "student ID " + term;
                case "name":
                    return "a name containing \"" + term + "\"";
                case "stage":
                    return "a stage containing \"" + term + "\"";
                default:
                    return "a department containing \"" + term + "\"";
            }
        }

        private static string HintFor(string field)
        {
            switch (field)
            {
                case "name":
                    return "Matches any part of the student name.";
                case "stage":
                    return "Matches any part of the stage.";
                case "department":
                    return "Matches any part of the department.";
                default:
                    return "Matches the student ID exactly.";
            }
        }

        private static string PlaceholderFor(string field)
        {
            switch (field)
            {
                case "name":
                    return "Full or partial name";
                case "stage":
                    return "Stage";
                case "department":
                    return "Department";
                default:
                    return "Student ID";
            }
        }
    }
}
