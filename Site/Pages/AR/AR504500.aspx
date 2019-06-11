﻿<%@ Page Language="C#" MasterPageFile="~/MasterPages/FormDetail.master" AutoEventWireup="true" ValidateRequest="false" CodeFile="AR504500.aspx.cs" Inherits="Page_AR504500" Title="Untitled Page" %>
<%@ MasterType VirtualPath="~/MasterPages/FormDetail.master" %>

<asp:Content ID="cont1" ContentPlaceHolderID="phDS" runat="Server">
    <px:PXDataSource ID="ds" runat="server" Visible="True" Width="100%" PrimaryView="Filter" TypeName="PX.Objects.AR.ARPPDCreditMemoProcess">
    </px:PXDataSource>
</asp:Content>
<asp:Content ID="cont2" ContentPlaceHolderID="phF" runat="Server">
    <px:PXFormView ID="form" runat="server" DataSourceID="ds" Style="z-index: 100"
        Width="100%" DataMember="Filter" Caption="Selection" MarkRequired="Dynamic" >
        <Template>
            <px:PXLayoutRule runat="server" StartColumn="True" LabelsWidth="S" ControlSize="M" />
            <px:PXDateTimeEdit CommitChanges="True" ID="edApplicationDate" runat="server" DataField="ApplicationDate" />
            <px:PXSegmentMask CommitChanges="True" ID="edBranchID" runat="server" DataField="BranchID" />
            <px:PXSegmentMask CommitChanges="True" ID="edCustomerID" runat="server" DataField="CustomerID" />
            <px:PXLayoutRule runat="server" StartColumn="True" LabelsWidth="SM" ControlSize="M" />
            <px:PXCheckBox CommitChanges="True" ID="chkGenerateOnePerCustomer" runat="server" DataField="GenerateOnePerCustomer" AlignLeft="True" />
            <px:PXDateTimeEdit CommitChanges="True" ID="edCreditMemoDate" runat="server" DataField="CreditMemoDate" />
            <px:PXSelector CommitChanges="True" ID="edFinPeriodID" runat="server" DataField="FinPeriodID" />
        </Template>
    </px:PXFormView>
</asp:Content>
<asp:Content ID="cont3" ContentPlaceHolderID="phG" runat="Server">
    <px:PXGrid ID="grid" runat="server" DataSourceID="ds" Style="z-index: 100" AllowPaging="True" AllowSearch="true" 
        Width="100%" Height="150px" Caption="Applications" SkinID="PrimaryInquire" >
        <Levels>
            <px:PXGridLevel DataMember="Applications">
                <Columns>
                    <px:PXGridColumn AllowNull="False" DataField="Selected" TextAlign="Center" Type="CheckBox" Width="30px" AllowCheckAll="True" AutoCallBack="True" />
                    <px:PXGridColumn DataField="AdjdBranchID" Width="100px" />
                    <px:PXGridColumn DataField="AdjdCustomerID" Width="100px" />
                    <px:PXGridColumn DataField="AdjdDocType" Width="100px" />
                    <px:PXGridColumn DataField="AdjdRefNbr" Width="100px" LinkCommand="ViewDocument" />
                    <px:PXGridColumn DataField="AdjdDocDate" Width="100px" />
                    <px:PXGridColumn DataField="InvCuryID" Width="100px" />
                    <px:PXGridColumn DataField="InvCuryOrigDocAmt" Width="100px" TextAlign="Right" />
                    <px:PXGridColumn DataField="CuryAdjdPPDAmt" Width="100px" TextAlign="Right" />
                    <px:PXGridColumn DataField="InvTermsID" Width="100px" />
                    <px:PXGridColumn DataField="AdjgRefNbr" Width="100px" LinkCommand="ViewDocument" />
                </Columns>
            </px:PXGridLevel>
        </Levels>
        <AutoSize Container="Window" Enabled="True" MinHeight="400" />
    </px:PXGrid>
</asp:Content>
