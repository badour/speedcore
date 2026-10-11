<%@ Page Title="Student search" Language="C#" MasterPageFile="~/Site.Master" AutoEventWireup="true" CodeBehind="Default.aspx.cs" Inherits="UniversityStudentSearch._Default" ClientIDMode="Static" ValidateRequest="false" %>

<asp:Content ID="Main" ContentPlaceHolderID="MainContent" runat="server">
    <asp:Panel ID="searchPanel" runat="server" CssClass="search-panel" DefaultButton="btnSearch">
        <div class="search-copy">
            <h2>Find a student</h2>
            <p class="lede">Search by student ID, name, stage, or department.</p>
        </div>
        <div class="search-controls">
            <div class="field">
                <asp:Label ID="lblField" runat="server" AssociatedControlID="ddlField" Text="Search by" />
                <asp:DropDownList ID="ddlField" runat="server">
                    <asp:ListItem Value="id" Text="Student ID" Selected="True" />
                    <asp:ListItem Value="name" Text="Student name" />
                    <asp:ListItem Value="stage" Text="Stage" />
                    <asp:ListItem Value="department" Text="Department" />
                </asp:DropDownList>
            </div>
            <div class="field">
                <asp:Label ID="lblTerm" runat="server" AssociatedControlID="txtTerm" Text="Search text" />
                <asp:TextBox ID="txtTerm" runat="server" MaxLength="200" />
            </div>
            <div class="actions">
                <asp:Button ID="btnSearch" runat="server" Text="Search" CssClass="btn btn-search" OnClick="btnSearch_Click" />
                <asp:Button ID="btnClear" runat="server" Text="Clear" CssClass="btn btn-ghost" CausesValidation="false" OnClick="btnClear_Click" />
            </div>
        </div>
        <p id="searchHint" class="hint"><asp:Literal ID="litHint" runat="server" Text="Matches the student ID exactly." /></p>
    </asp:Panel>

    <asp:Label ID="lblMessage" runat="server" CssClass="message" Visible="false" />

    <asp:Panel ID="pnlResults" runat="server" Visible="false" CssClass="results-wrap">
        <div class="results-meta">
            <h2>Results</h2>
            <asp:Label ID="lblSummary" runat="server" CssClass="summary" />
        </div>
        <div class="table-scroll">
            <asp:GridView ID="gvStudents" runat="server"
                AutoGenerateColumns="False"
                Caption="Matching students"
                CssClass="results"
                GridLines="None"
                CellPadding="0"
                CellSpacing="0"
                DataKeyNames="RowID"
                UseAccessibleHeader="True"
                OnRowCommand="gvStudents_RowCommand"
                OnRowDataBound="gvStudents_RowDataBound"
                OnPreRender="gvStudents_PreRender">
                <Columns>
                    <asp:TemplateField HeaderText="Student name" ItemStyle-CssClass="name" HeaderStyle-CssClass="name">
                        <ItemTemplate>
                            <span class="student-name" dir="auto"><%# NameText(Eval("StudentName")) %></span>
                        </ItemTemplate>
                    </asp:TemplateField>
                    <asp:TemplateField HeaderText="Student ID" ItemStyle-CssClass="id" HeaderStyle-CssClass="id">
                        <ItemTemplate><%# IdText(Eval("StudentID")) %></ItemTemplate>
                    </asp:TemplateField>
                    <asp:TemplateField HeaderText="Year name" Visible="False">
                        <ItemTemplate>
                            <span dir="auto"><%# TextOrDash(Eval("YearName")) %></span>
                        </ItemTemplate>
                    </asp:TemplateField>
                    <asp:TemplateField HeaderText="Stage" Visible="False">
                        <ItemTemplate>
                            <span dir="auto"><%# TextOrDash(Eval("Stage")) %></span>
                        </ItemTemplate>
                    </asp:TemplateField>
                    <asp:TemplateField HeaderText="Department" Visible="False">
                        <ItemTemplate>
                            <span dir="auto"><%# TextOrDash(Eval("Department")) %></span>
                        </ItemTemplate>
                    </asp:TemplateField>
                    <asp:TemplateField HeaderText="Total" ItemStyle-CssClass="num" HeaderStyle-CssClass="num">
                        <ItemTemplate><%# MoneyText(Eval("TotalValue")) %></ItemTemplate>
                    </asp:TemplateField>
                    <asp:TemplateField HeaderText="Paid" ItemStyle-CssClass="num" HeaderStyle-CssClass="num">
                        <ItemTemplate><%# MoneyText(Eval("PaidValue")) %></ItemTemplate>
                    </asp:TemplateField>
                    <asp:TemplateField HeaderText="Remaining" ItemStyle-CssClass="num" HeaderStyle-CssClass="num">
                        <ItemTemplate><%# MoneyText(Eval("RemainingValue")) %></ItemTemplate>
                    </asp:TemplateField>
                    <asp:TemplateField HeaderText="Access">
                        <ItemTemplate>
                            <div class="access-edit">
                                <asp:DropDownList ID="ddlAccess" runat="server" CssClass="access-choice">
                                    <asp:ListItem Value="" Text="Choose" />
                                    <asp:ListItem Value="true" Text="Allowed" />
                                    <asp:ListItem Value="false" Text="Not set" />
                                </asp:DropDownList>
                                <asp:Button ID="btnSaveAccess" runat="server" Text="Save" CssClass="btn btn-ghost btn-small" CausesValidation="false" UseSubmitBehavior="false" CommandName="SaveAccess" CommandArgument='<%# Container.DataItemIndex %>' />
                            </div>
                        </ItemTemplate>
                    </asp:TemplateField>
                </Columns>
            </asp:GridView>
        </div>
        <asp:Panel ID="pnlPager" runat="server" CssClass="pager">
            <asp:Button ID="btnPrev" runat="server" Text="Previous" CssClass="btn btn-ghost" CausesValidation="false" OnClick="btnPrev_Click" />
            <asp:Label ID="lblPage" runat="server" CssClass="page-label" />
            <asp:Button ID="btnNext" runat="server" Text="Next" CssClass="btn btn-ghost" CausesValidation="false" OnClick="btnNext_Click" />
        </asp:Panel>
    </asp:Panel>

    <script src="<%= ResolveUrl("~/Scripts/search.js") %>"></script>
</asp:Content>
