<%@ Page Title="Device sync" Language="C#" MasterPageFile="~/Site.Master" AutoEventWireup="true" CodeBehind="Sync.aspx.cs" Inherits="UniversityStudentSearch.SyncPage" ValidateRequest="false" %>

<asp:Content ID="Main" ContentPlaceHolderID="MainContent" runat="server">
    <asp:Label ID="lblMessage" runat="server" CssClass="message" Visible="false" />

    <asp:Panel ID="filterPanel" runat="server" CssClass="search-panel" DefaultButton="btnFind">
        <div class="search-copy">
            <h2>Sync students to the gates</h2>
            <p class="lede">Find students by year name and department, then choose one name or all of them. Sync sends the latest row of each selected student: student name, student ID, and department. Only a student whose latest access value is still null is listed.</p>
        </div>
        <div class="sync-controls">
            <div class="field">
                <asp:Label ID="lblYear" runat="server" AssociatedControlID="txtYear" Text="Year name" />
                <asp:TextBox ID="txtYear" runat="server" MaxLength="200" placeholder="Year name" />
            </div>
            <div class="field">
                <asp:Label ID="lblDepartment" runat="server" AssociatedControlID="txtDepartment" Text="Department" />
                <asp:TextBox ID="txtDepartment" runat="server" MaxLength="200" placeholder="Department name" />
            </div>
            <div class="actions">
                <asp:Button ID="btnFind" runat="server" Text="Find students" CssClass="btn btn-search" OnClick="btnFind_Click" />
            </div>
        </div>
        <p class="hint">A year matches any row for that student, including 2026/2027 and 2026-2027. A department matches any row, including DepartmentName, DeptName, and a department lookup. When both boxes are filled, the student must match both. Access on the latest row must be null. On each terminal, set the Yunatt server port to <strong>7792</strong>. GateDeviceBridge calls this site at <strong><%= Server.HtmlEncode(MiddlewareAddress) %></strong>.</p>
    </asp:Panel>

    <asp:Panel ID="pnlStudents" runat="server" Visible="false" CssClass="results-wrap">
        <div class="results-meta">
            <h2>Students to sync</h2>
            <asp:Label ID="lblStudentSummary" runat="server" CssClass="summary" />
        </div>
        <div class="table-scroll">
            <asp:GridView ID="gvStudents" runat="server"
                AutoGenerateColumns="False"
                CssClass="results students"
                GridLines="None"
                UseAccessibleHeader="True"
                DataKeyNames="RowID,StudentID,StudentName,Department"
                OnRowDataBound="gvStudents_RowDataBound"
                EmptyDataText="No students with null access match that year and department.">
                <Columns>
                    <asp:TemplateField HeaderText="Send">
                        <HeaderTemplate>
                            <input type="checkbox" class="pick-all" aria-label="Select all students" />
                        </HeaderTemplate>
                        <ItemTemplate>
                            <asp:CheckBox ID="chkPick" runat="server" CssClass="pick" />
                        </ItemTemplate>
                    </asp:TemplateField>
                    <asp:BoundField DataField="StudentName" HeaderText="Student name" />
                    <asp:BoundField DataField="StudentID" HeaderText="Student ID" />
                    <asp:BoundField DataField="YearName" HeaderText="Year name" />
                    <asp:BoundField DataField="Department" HeaderText="Department" />
                </Columns>
            </asp:GridView>
        </div>
        <div class="actions sync-actions">
            <asp:Button ID="btnSync" runat="server" Text="Sync selected" CssClass="btn btn-search" OnClick="btnSync_Click" />
        </div>
    </asp:Panel>

    <asp:Panel ID="devicePanel" runat="server" CssClass="results-wrap">
        <div class="results-meta">
            <h2>Gates</h2>
            <asp:Label ID="lblSummary" runat="server" CssClass="summary" />
        </div>
        <div class="table-scroll">
            <asp:GridView ID="gvDevices" runat="server"
                AutoGenerateColumns="False"
                CssClass="results devices"
                GridLines="None"
                UseAccessibleHeader="True"
                EmptyDataText="No device has called this server yet.">
                <Columns>
                    <asp:BoundField DataField="Serial" HeaderText="Device" />
                    <asp:BoundField DataField="LastSeen" HeaderText="Last seen (UTC)" />
                    <asp:BoundField DataField="Waiting" HeaderText="Waiting" />
                </Columns>
            </asp:GridView>
        </div>
    </asp:Panel>
    <script src="<%= ResolveUrl("~/Scripts/sync.js") %>?v=2"></script>
</asp:Content>
