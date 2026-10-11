<%@ Page Title="Device sync" Language="C#" MasterPageFile="~/Site.Master" AutoEventWireup="true" CodeBehind="Sync.aspx.cs" Inherits="UniversityStudentSearch.SyncPage" ValidateRequest="false" %>

<asp:Content ID="Main" ContentPlaceHolderID="MainContent" runat="server">
    <asp:Label ID="lblMessage" runat="server" CssClass="message" Visible="false" />

    <asp:Panel ID="syncPanel" runat="server" CssClass="search-panel" DefaultButton="btnSync">
        <div class="search-copy">
            <h2>Sync students to the gates</h2>
            <p class="lede">Sync sends each new student's latest row to every AYTE AI-X1. The row is the one with the greatest RowID, and the fields are student name, student ID, and department. Device addresses are not required. Each terminal calls this server on its own.</p>
        </div>
        <p class="hint">On each terminal, set the server host to <strong><%= Server.HtmlEncode(ServerHost) %></strong> and the port to <strong><%= Server.HtmlEncode(ServerPort) %></strong>. Open this page with the address the gates can reach. A terminal then appears in the list below. Face photos and attendance logs are not changed.</p>
        <div class="actions sync-actions">
            <asp:Button ID="btnSync" runat="server" Text="Sync" CssClass="btn btn-search" OnClick="btnSync_Click" />
        </div>
        <asp:Label ID="lblSummary" runat="server" CssClass="summary" Visible="false" />
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
    </asp:Panel>
</asp:Content>
