<%@ Page Title="Device sync" Language="C#" MasterPageFile="~/Site.Master" AutoEventWireup="true" CodeBehind="Sync.aspx.cs" Inherits="UniversityStudentSearch.SyncPage" ValidateRequest="false" %>

<asp:Content ID="Main" ContentPlaceHolderID="MainContent" runat="server">
    <asp:Label ID="lblMessage" runat="server" CssClass="message" Visible="false" />

    <asp:Panel ID="syncPanel" runat="server" CssClass="search-panel" DefaultButton="btnSync">
        <div class="search-copy">
            <h2>Sync students to the gates</h2>
            <p class="lede">Sync sends each new student's latest row to every AYTE AI-X1. The row is the one with the greatest RowID, and the fields are student name, student ID, and department. The terminals do not connect to this website, and their IP addresses are not required.</p>
        </div>
        <p class="hint">On each terminal, set the Yunatt server port to <strong>7792</strong>. GateDeviceBridge accepts that WebSocket and calls this site at <strong><%= Server.HtmlEncode(MiddlewareAddress) %></strong>. A face already stored on the terminal is not replaced. When a face is scanned, the bridge asks this site whether payment allows the door to open.</p>
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
