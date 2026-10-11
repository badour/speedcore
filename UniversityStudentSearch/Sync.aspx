<%@ Page Title="Device sync" Language="C#" MasterPageFile="~/Site.Master" AutoEventWireup="true" CodeBehind="Sync.aspx.cs" Inherits="UniversityStudentSearch.SyncPage" ValidateRequest="false" %>

<asp:Content ID="Main" ContentPlaceHolderID="MainContent" runat="server">
    <asp:Label ID="lblMessage" runat="server" CssClass="message" Visible="false" />

    <asp:Panel ID="devicePanel" runat="server" CssClass="search-panel" DefaultButton="btnAddDevice">
        <div class="search-copy">
            <h2>AYTE AI-X1 devices</h2>
            <p class="lede">Add each terminal that students were enrolled on directly. The sync compares student name, student ID, and department. Face photos and attendance logs stay on the device.</p>
        </div>
        <div class="device-form">
            <div class="field">
                <asp:Label ID="lblDeviceName" runat="server" AssociatedControlID="txtDeviceName" Text="Name" />
                <asp:TextBox ID="txtDeviceName" runat="server" MaxLength="80" />
            </div>
            <div class="field">
                <asp:Label ID="lblHost" runat="server" AssociatedControlID="txtHost" Text="Address" />
                <asp:TextBox ID="txtHost" runat="server" MaxLength="253" />
            </div>
            <div class="field">
                <asp:Label ID="lblPort" runat="server" AssociatedControlID="txtPort" Text="Port" />
                <asp:TextBox ID="txtPort" runat="server" MaxLength="5" Text="8090" />
            </div>
            <div class="field">
                <asp:Label ID="lblPassword" runat="server" AssociatedControlID="txtPassword" Text="Password" />
                <asp:TextBox ID="txtPassword" runat="server" MaxLength="64" TextMode="Password" />
            </div>
            <div class="actions">
                <asp:Button ID="btnAddDevice" runat="server" Text="Add device" CssClass="btn btn-search" OnClick="btnAddDevice_Click" />
            </div>
        </div>
        <p class="hint">Use the device IP and its password. AYTE AI-X1 terminals answer the person API on port 8090. Department is stored in the person remark.</p>
        <asp:GridView ID="gvDevices" runat="server"
            AutoGenerateColumns="False"
            CssClass="results devices"
            GridLines="None"
            ShowHeader="True"
            UseAccessibleHeader="True"
            EmptyDataText="No devices yet."
            OnRowCommand="gvDevices_RowCommand">
            <Columns>
                <asp:BoundField DataField="Name" HeaderText="Device" />
                <asp:BoundField DataField="Host" HeaderText="Address" />
                <asp:BoundField DataField="Port" HeaderText="Port" />
                <asp:TemplateField HeaderText="Actions">
                    <ItemTemplate>
                        <asp:Button runat="server" Text="Test" CssClass="btn btn-ghost btn-small" CommandName="Test" CommandArgument='<%# Eval("Id") %>' CausesValidation="false" />
                        <asp:Button runat="server" Text="Remove" CssClass="btn btn-ghost btn-small" CommandName="Remove" CommandArgument='<%# Eval("Id") %>' CausesValidation="false" />
                    </ItemTemplate>
                </asp:TemplateField>
            </Columns>
        </asp:GridView>
    </asp:Panel>

    <asp:Panel ID="comparePanel" runat="server" CssClass="search-panel sync-compare" DefaultButton="btnCompare">
        <div class="search-copy">
            <h2>Synchronize students</h2>
            <p class="lede">Students added on the terminal show as only on the device. Students in AllStudentValueTb who are missing from the terminal show as only in the database.</p>
        </div>
        <div class="search-controls sync-controls">
            <div class="field">
                <asp:Label ID="lblDevice" runat="server" AssociatedControlID="ddlDevice" Text="Device" />
                <asp:DropDownList ID="ddlDevice" runat="server" />
            </div>
            <div class="field">
                <asp:Label ID="lblFilter" runat="server" AssociatedControlID="ddlFilter" Text="Show" />
                <asp:DropDownList ID="ddlFilter" runat="server" AutoPostBack="true" OnSelectedIndexChanged="ddlFilter_SelectedIndexChanged">
                    <asp:ListItem Value="differences" Text="All differences" Selected="True" />
                    <asp:ListItem Value="device" Text="Only on the device" />
                    <asp:ListItem Value="database" Text="Only in the database" />
                    <asp:ListItem Value="changed" Text="Name or department differs" />
                </asp:DropDownList>
            </div>
            <div class="actions">
                <asp:Button ID="btnCompare" runat="server" Text="Compare" CssClass="btn btn-search" OnClick="btnCompare_Click" />
            </div>
        </div>
        <asp:Label ID="lblSummary" runat="server" CssClass="summary" Visible="false" />
        <asp:Panel ID="pnlDiff" runat="server" Visible="false" CssClass="results-wrap">
            <div class="table-scroll">
                <asp:GridView ID="gvDiff" runat="server"
                    AutoGenerateColumns="False"
                    AllowPaging="True"
                    PageSize="40"
                    CssClass="results"
                    GridLines="None"
                    UseAccessibleHeader="True"
                    Caption="Student differences"
                    OnPageIndexChanging="gvDiff_PageIndexChanging"
                    OnRowDataBound="gvDiff_RowDataBound">
                    <Columns>
                        <asp:TemplateField HeaderText="Select">
                            <HeaderTemplate>
                                <input type="checkbox" class="pick-all" aria-label="Select all on this page" />
                            </HeaderTemplate>
                            <ItemTemplate>
                                <asp:CheckBox ID="chkPick" runat="server" />
                            </ItemTemplate>
                        </asp:TemplateField>
                        <asp:BoundField DataField="StudentId" HeaderText="Student ID" />
                        <asp:TemplateField HeaderText="Database name">
                            <ItemTemplate><span dir="auto"><%# Encode(Eval("DatabaseName")) %></span></ItemTemplate>
                        </asp:TemplateField>
                        <asp:TemplateField HeaderText="Device name">
                            <ItemTemplate><span dir="auto"><%# Encode(Eval("DeviceName")) %></span></ItemTemplate>
                        </asp:TemplateField>
                        <asp:TemplateField HeaderText="Database department">
                            <ItemTemplate><span dir="auto"><%# Encode(Eval("DatabaseDepartment")) %></span></ItemTemplate>
                        </asp:TemplateField>
                        <asp:TemplateField HeaderText="Device department">
                            <ItemTemplate><span dir="auto"><%# Encode(Eval("DeviceDepartment")) %></span></ItemTemplate>
                        </asp:TemplateField>
                        <asp:BoundField DataField="StatusText" HeaderText="Status" />
                    </Columns>
                    <PagerSettings Mode="NumericFirstLast" Position="Bottom" />
                </asp:GridView>
            </div>
            <div class="actions sync-actions">
                <asp:Button ID="btnToDevice" runat="server" Text="Write selected to the device" CssClass="btn btn-search" OnClick="btnToDevice_Click" />
                <asp:Button ID="btnToDatabase" runat="server" Text="Save selected into the database" CssClass="btn btn-ghost" OnClick="btnToDatabase_Click" />
            </div>
            <p class="hint">Writing to the device adds a missing person or updates the name and department. It does not delete anyone and does not replace a face photo. Saving into the database inserts a new student or updates the latest row.</p>
        </asp:Panel>
    </asp:Panel>
    <script src="<%= ResolveUrl("~/Scripts/sync.js") %>"></script>
</asp:Content>
