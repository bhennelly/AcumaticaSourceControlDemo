﻿using System.Collections;
using System.Collections.Generic;
using System;
using System.Linq;
using PX.Common;
using PX.Data;
using PX.Data.BQL;
using PX.Data.BQL.Fluent;
using PX.Objects.Common;
using PX.Objects.AP;
using PX.Objects.CS;
using PX.Objects.IN;
using PX.Objects.Extensions;
using WMSBase = PX.Objects.IN.WarehouseManagementSystemGraph<PX.Objects.PO.ReceivePutAway, PX.Objects.PO.ReceivePutAwayHost, PX.Objects.PO.POReceipt, PX.Objects.PO.ReceivePutAway.Header>;
using SiteStatus = PX.Objects.IN.Overrides.INDocumentRelease.SiteStatus;
using LocationStatus = PX.Objects.IN.Overrides.INDocumentRelease.LocationStatus;
using LotSerialStatus = PX.Objects.IN.Overrides.INDocumentRelease.LotSerialStatus;
using ItemLotSerial = PX.Objects.IN.Overrides.INDocumentRelease.ItemLotSerial;
using SiteLotSerial = PX.Objects.IN.Overrides.INDocumentRelease.SiteLotSerial;

namespace PX.Objects.PO
{
	public class ReceivePutAwayHost : POReceiptEntry
	{
		public override Type PrimaryItemType => typeof(ReceivePutAway.Header);
		public PXFilter<ReceivePutAway.Header> HeaderView;
		public ReceivePutAway WMS => FindImplementation<ReceivePutAway>();
	}
	public class ReceivePutAway : WMSBase
	{
		public class UserSetup : PXUserSetup<UserSetup, ReceivePutAwayHost, Header, POReceivePutAwayUserSetup, POReceivePutAwayUserSetup.userID> { }

		#region Attached Fields
		public class ToLocationID : PXFieldAttachedTo<POReceiptLineSplit>.By<ReceivePutAwayHost>.AsInteger
		{
			public override int? GetValue(POReceiptLineSplit Row) => null;
			protected override bool SuppressValueSetting => true;

			protected override PXFieldState AdjustStateByRow(PXFieldState state, POReceiptLineSplit row)
			{
				PXCache tranCache = Base.WMS.transferEntry.transactions.Cache;
				if (row == null)
				{
					state = (PXFieldState)tranCache.GetStateExt<INTran.toLocationID>(null);
				}
				else
				{
					var links =
						SelectFrom<POReceiptSplitToTransferSplitLink>
						.InnerJoin<INTran>.On<POReceiptSplitToTransferSplitLink.FK.INTran>
						.Where<POReceiptSplitToTransferSplitLink.FK.POReceiptLineSplit.SameAsCurrent>
						.View.SelectMultiBound(Base, new[] { row })
						.AsEnumerable()
						.Cast<PXResult<POReceiptSplitToTransferSplitLink, INTran>>()
						.ToArray();

					if (links.Length == 0)
					{
						state = (PXFieldState)tranCache.GetStateExt<INTran.toLocationID>(null);
						state.Value = "";
					}
					else if (links.Length == 1 || links.GroupBy(l => l.GetItem<INTran>().ToLocationID).Count() == 1)
					{
						state = (PXFieldState)tranCache.GetStateExt<INTran.toLocationID>(links[0].GetItem<INTran>());
						state.Value = (string)state.Value;
					}
					else
					{
						state = (PXFieldState)tranCache.GetStateExt<INTran.toLocationID>(null);
						state.Value = "<SPLIT>";
					}
				}
				state.Visible = Base.WMS.HeaderView.Current.Mode == Modes.PutAway;
				return state;
			}
		}

		[PXUIField(DisplayName = Msg.CartQty)]
		public class CartQty : PXFieldAttachedTo<POReceiptLineSplit>.By<ReceivePutAwayHost>.AsDecimal.Named<CartQty>
		{
			public override decimal? GetValue(POReceiptLineSplit row) => Base.WMS.GetCartQty(row);
			protected override bool? Visible => Base.WMS.With(wms => wms.IsCartRequired(wms.HeaderView.Current));
		}

		[PXUIField(DisplayName = Msg.CartOverallQty)]
		public class OverallCartQty : PXFieldAttachedTo<POReceiptLineSplit>.By<ReceivePutAwayHost>.AsDecimal.Named<OverallCartQty>
		{
			public override decimal? GetValue(POReceiptLineSplit row) => Base.WMS.GetOverallCartQty(row);
			protected override bool? Visible => Base.WMS.With(wms => wms.IsCartRequired(wms.HeaderView.Current));
		}

		[PXUIField(DisplayName = Msg.RestQty)]
		public class RestQty : PXFieldAttachedTo<POReceiptLineSplit>.By<ReceivePutAwayHost>.AsDecimal.Named<RestQty>
		{
			public override decimal? GetValue(POReceiptLineSplit row) => Base.WMS.HeaderView.Current?.Mode == Modes.Receive ? Base.WMS.GetNormalRestQty(row) : 0;
			protected override bool? Visible => Base.WMS.With(wms => wms.HeaderView.Current?.Mode == Modes.Receive);
		}

		[PXUIField(DisplayName = Msg.Fits)]
		public class Fits : PXFieldAttachedTo<POReceiptLineSplit>.By<ReceivePutAwayHost>.AsBool.Named<Fits>
		{
			public override bool? GetValue(POReceiptLineSplit row)
			{
				var header = Base.WMS.HeaderView.Current;
				bool fits = true;
				if (header.LocationID != null)
					fits &= header.LocationID == row.LocationID;
				if (header.InventoryID != null)
					fits &= header.InventoryID == row.InventoryID && header.SubItemID == row.SubItemID;
				if (header.LotSerialNbr != null)
					fits &= header.LotSerialNbr == row.LotSerialNbr;
				return fits;
			}
		}

		[PXUIField(Visible = false)]
		public class ShowReceive : PXFieldAttachedTo<Header>.By<ReceivePutAwayHost>.AsBool.Named<ShowReceive>
		{
			public override bool? GetValue(Header row) => Base.WMS.Setup.Current.ShowReceivingTab == true && row.Mode.IsIn(Modes.Free, Modes.Receive);
		}

		[PXUIField(Visible = false)]
		public class ShowPutAway : PXFieldAttachedTo<Header>.By<ReceivePutAwayHost>.AsBool.Named<ShowPutAway>
		{
			public override bool? GetValue(Header row) => Base.WMS.Setup.Current.ShowPutAwayTab == true && row.Mode.IsIn(Modes.Free, Modes.PutAway);
		}

		[PXUIField(Visible = false)]
		public class ShowLog : PXFieldAttachedTo<Header>.By<ReceivePutAwayHost>.AsBool.Named<ShowLog>
		{
			public override bool? GetValue(Header row) => Base.WMS.Setup.Current.ShowScanLogTab == true;
		}
		#endregion

		#region DACs
		public class Header : WMSHeader, ILSMaster
		{
			#region ReceiptNbr
			[PXUnboundDefault]
			[PXString(15, IsUnicode = true, InputMask = ">CCCCCCCCCCCCCCC")]
			[PXUIField(DisplayName = "Receipt Nbr.", Enabled = false)]
			[PXSelector(typeof(
				Search2<POReceipt.receiptNbr,
				LeftJoinSingleTable<Vendor, On<POReceipt.vendorID, Equal<Vendor.bAccountID>>>,
				Where<Vendor.bAccountID, IsNull, Or<Match<Vendor, Current<AccessInfo.userName>>>>>))]
			[PXRestrictor(typeof(
				Where<Current<mode>, Equal<Modes.receive>, And2<Where<POReceipt.status, Equal<POReceiptStatus.balanced>, Or<POReceipt.wMSSingleOrder, Equal<True>>>,
				Or<Current<mode>, Equal<Modes.putAway>, And<POReceipt.status, Equal<POReceiptStatus.released>, And<POReceipt.receiptType, NotEqual<POReceiptType.poreturn>, And<POReceipt.pOType, NotEqual<POOrderType.dropShip>>>>>>>),
				Msg.ReceiptInvalid, typeof(POReceipt.receiptNbr))]
			public override string RefNbr { get; set; }
			public new abstract class refNbr : PX.Data.BQL.BqlString.Field<refNbr> { }
			#endregion
			#region TranDate
			[PXDate]
			public virtual DateTime? TranDate { get; set; }
			public abstract class tranDate : PX.Data.BQL.BqlDateTime.Field<tranDate> { } 
			#endregion

			#region SiteID
			[Site]
			public virtual int? SiteID { get; set; }
			public abstract class siteID : PX.Data.BQL.BqlInt.Field<siteID> { }
			#endregion
			#region CartID
			[PXInt]
			[PXUIField(DisplayName = "Cart ID", Enabled = false)]
			[PXSelector(typeof(Search2<INCart.cartID, InnerJoin<INSite, On<INCart.FK.Site>>, Where<INCart.active, Equal<True>, And<Match<INSite, Current<AccessInfo.userName>>>>>), SubstituteKey = typeof(INCart.cartCD), DescriptionField = typeof(INCart.descr))]
			[PXUIVisible(typeof(Where<mode, Equal<Modes.putAway>, And2<FeatureInstalled<FeaturesSet.wMSCartTracking>, And<Current<POReceivePutAwaySetup.useCartsForPutAway>, Equal<True>>>>))]
			public virtual int? CartID { get; set; }
			public abstract class cartID : PX.Data.BQL.BqlInt.Field<cartID> { }
			#endregion
			#region DefaultLocationID
			[Location]
			public virtual int? DefaultLocationID { get; set; }
			public abstract class defaultLocationID : PX.Data.BQL.BqlInt.Field<defaultLocationID> { }
			#endregion
			#region LocationID
			[Location]
			public virtual int? LocationID { get; set; }
			public abstract class locationID : PX.Data.BQL.BqlInt.Field<locationID> { }
			#endregion
			#region ToLocationID
			[Location]
			public virtual int? ToLocationID { get; set; }
			public abstract class toLocationID : PX.Data.BQL.BqlInt.Field<toLocationID> { }
			#endregion

			#region InventoryID
			public new abstract class inventoryID : PX.Data.BQL.BqlInt.Field<inventoryID> { }
			#endregion
			#region SubItemID
			public new abstract class subItemID : PX.Data.BQL.BqlInt.Field<subItemID> { }
			#endregion
			#region LotSerialNbr
			[POLotSerialNbr(typeof(inventoryID), typeof(subItemID), typeof(locationID))]
			public virtual string LotSerialNbr { get; set; }
			public abstract class lotSerialNbr : PX.Data.BQL.BqlString.Field<lotSerialNbr> { }
			#endregion
			#region ExpirationDate
			[INExpireDate(typeof(inventoryID), PersistingCheck = PXPersistingCheck.Nothing)]
			public virtual DateTime? ExpireDate { get; set; }
			public abstract class expireDate : PX.Data.BQL.BqlDateTime.Field<expireDate> { }
			#endregion

			#region LotSerTrack
			[PXString(1, IsFixed = true)]
			public virtual String LotSerTrack { get; set; }
			public abstract class lotSerTrack : PX.Data.BQL.BqlString.Field<lotSerTrack> { }
			#endregion
			#region LotSerAssign
			[PXString(1, IsFixed = true)]
			public virtual String LotSerAssign { get; set; }
			public abstract class lotSerAssign : PX.Data.BQL.BqlString.Field<lotSerAssign> { }
			#endregion
			#region LotSerTrackExpiration
			[PXBool]
			public virtual Boolean? LotSerTrackExpiration { get; set; }
			public abstract class lotSerTrackExpiration : PX.Data.BQL.BqlBool.Field<lotSerTrackExpiration> { }
			#endregion
			#region AutoNextNbr
			[PXBool]
			public virtual Boolean? AutoNextNbr { get; set; }
			public abstract class autoNextNbr : PX.Data.BQL.BqlBool.Field<autoNextNbr> { }
			#endregion

			#region CartDone
			[PXBool, PXUnboundDefault(false)]
			[PXUIField(DisplayName = "Cart Unloading", Enabled = false)]
			[PXUIVisible(typeof(cartLoaded))]
			public virtual bool? CartLoaded { get; set; }
			public abstract class cartLoaded : PX.Data.BQL.BqlBool.Field<cartLoaded> { }
			#endregion
			#region Released
			[PXBool]
			[PXUnboundDefault(false, typeof(POReceipt.released))]
			[PXFormula(typeof(Default<Header.refNbr>))]
			public virtual bool? Released { get; set; }
			public abstract class released : PX.Data.BQL.BqlBool.Field<released> { }
			#endregion
			#region ForceInsertLine
			[PXBool, PXUnboundDefault(false)]
			public virtual bool? ForceInsertLine { get; set; }
			public abstract class forceInsertLine : PX.Data.BQL.BqlBool.Field<forceInsertLine> { }
			#endregion

			#region ReceiptType
			public abstract class receiptType : PX.Data.BQL.BqlString.Field<receiptType> { }
			[PXString(2, IsFixed = true)]
			public virtual String ReceiptType { get; set; }
			#endregion

			#region TransferRefNbr
			[PXDBString(15, IsUnicode = true, InputMask = ">CCCCCCCCCCCCCCC")]
			[PXUIField(DisplayName = "Transfer Ref Nbr.", Enabled = false)]
			[PXSelector(typeof(Search<INRegister.refNbr, Where<INRegister.docType, Equal<INDocType.transfer>>, OrderBy<Desc<INRegister.refNbr>>>), Filterable = true)]
			[PXUIVisible(typeof(Where<mode, Equal<Modes.putAway>>))]
			public virtual String TransferRefNbr { get; set; }
			public abstract class transferRefNbr : PX.Data.BQL.BqlString.Field<transferRefNbr> { }
			#endregion
			#region PONbr
			[PXDBString(15, IsUnicode = true, InputMask = ">CCCCCCCCCCCCCCC")]
			[PXUIField(DisplayName = "PO Ref. Nbr.", Enabled = false)]
			[PXSelector(typeof(Search<POOrder.orderNbr, Where<POOrder.orderType, Equal<POOrderType.regularOrder>>, OrderBy<Desc<POOrder.orderNbr>>>), Filterable = true)]
			[PXUIVisible(typeof(Where<mode, Equal<Modes.receive>>))]
			public virtual String PONbr { get; set; }
			public abstract class pONbr : PX.Data.BQL.BqlString.Field<transferRefNbr> { }
			#endregion

			#region ILSMaster
			string ILSMaster.TranType => "";

			short? ILSMaster.InvtMult
			{
				get => (short)(ReceiptType != POReceiptType.POReturn ? 1 /* receipt */ : -1 /* issue */);
				set { }
			}

			int? ILSMaster.ProjectID
			{
				get { return null; }
				set { }
			}

			int? ILSMaster.TaskID
			{
				get { return null; }
				set { }
			}
			#endregion

			public decimal GetBaseQty(ReceivePutAwayHost graph) => INUnitAttribute.ConvertToBase(graph.transactions.Cache, InventoryID, UOM, Qty ?? 0, INPrecision.NOROUND);
		}
		#endregion

		public ReceivePutAway() => _transferEntry = Lazy.By(CreateTransferEntry);

		#region Views
		public override PXFilter<Header> HeaderView => Base.HeaderView;

		public PXSetupOptional<POReceivePutAwaySetup, Where<POReceivePutAwaySetup.branchID, Equal<Current<AccessInfo.branchID>>>> Setup;

		#region Receive
		public
			SelectFrom<POReceiptLineSplit>
			.Where<POReceiptLineSplit.receiptNbr.IsEqual<Header.refNbr.FromCurrent>>
			.OrderBy<POReceiptLineSplit.receiptNbr.Asc, POReceiptLineSplit.lineNbr.Asc>
			.View ReceiveSplits;

		public SelectFrom<POReceiptLineSplit>
			.InnerJoin<POReceiptLine>.On<POReceiptLineSplit.FK.ReceiptLine>
			.View Received;
		public virtual IEnumerable received()
		{
			var res = new PXDelegateResult();
			res.AddRange(
				from s in ReceiveSplits.SelectMain()
				join l in Base.transactions.SelectMain() on s.LineNbr equals l.LineNbr
				orderby l.PONbr == null
				select new PXResult<POReceiptLineSplit, POReceiptLine>(s, l));
			res.IsResultSorted = true;
			return res;
		} 
		#endregion

		#region PutAway
		public SelectFrom<POReceiptLineSplit>.View PutAway;
		public virtual IEnumerable putAway() => received();

		public
			SelectFrom<POReceiptSplitToTransferSplitLink>
			.InnerJoin<INTranSplit>.On<POReceiptSplitToTransferSplitLink.FK.INTranSplit>
			.InnerJoin<INTran>.On<POReceiptSplitToTransferSplitLink.FK.INTran>
			.Where<POReceiptSplitToTransferSplitLink.FK.POReceiptLineSplit.SameAsCurrent>
			.View TransferSplitLinks;

		#region Cart
		public
			SelectFrom<INCart>
			.Where<INCart.siteID.IsEqual<Header.siteID.FromCurrent>
				.And<INCart.cartID.IsEqual<Header.cartID.FromCurrent>>>
			.View Cart;

		public SelectFrom<INCartSplit>.Where<INCartSplit.FK.Cart.SameAsCurrent>.View CartSplits;

		public SelectFrom<POCartReceipt>.View CartsLinks;

		public
			SelectFrom<INCartSplit>
			.InnerJoin<INCart>.On<INCartSplit.FK.Cart>
			.InnerJoin<POCartReceipt>.On<POCartReceipt.FK.Cart>
			.Where<POCartReceipt.FK.Receipt.SameAsCurrent>
			.View AllCartSplits;

		public
			SelectFrom<POReceiptSplitToCartSplitLink>
			.InnerJoin<INCartSplit>.On<POReceiptSplitToCartSplitLink.FK.CartSplit>
			.Where<POReceiptSplitToCartSplitLink.FK.Cart.SameAsCurrent>
			.View CartSplitLinks;

		public
			SelectFrom<POReceiptSplitToCartSplitLink>
			.InnerJoin<INCartSplit>.On<POReceiptSplitToCartSplitLink.FK.CartSplit>
			.InnerJoin<INCart>.On<INCartSplit.FK.Cart>
			.InnerJoin<POCartReceipt>.On<POCartReceipt.FK.Cart>
			.Where<POCartReceipt.FK.Receipt.SameAsCurrent>
			.View AllCartSplitLinks;
		#endregion
		#endregion
		#endregion

		#region Buttons
		public PXAction<Header> ScanReleaseReceipt;
		[PXButton, PXUIField(DisplayName = "Release Receipt")]
		protected virtual IEnumerable scanReleaseReceipt(PXAdapter adapter) => scanBarcode(adapter, ScanCommands.ReleaseReceipt);

		public PXAction<Header> ScanReleaseTransfer;
		[PXButton, PXUIField(DisplayName = "Release Transfer")]
		protected virtual IEnumerable scanReleaseTransfer(PXAdapter adapter) => scanBarcode(adapter, ScanCommands.ReleaseTransfer);

		public PXAction<Header> ScanCompletePOLines;
		[PXButton, PXUIField(DisplayName = "Complete PO Lines")]
		protected virtual IEnumerable scanCompletePOLines(PXAdapter adapter) => scanBarcode(adapter, ScanCommands.CompletePOLines);

		public PXAction<Header> ReviewReceive;
		[PXButton, PXUIField(DisplayName = "Review")]
		protected virtual IEnumerable reviewReceive(PXAdapter adapter) => adapter.Get();

		public PXAction<Header> ReviewPutAway;
		[PXButton, PXUIField(DisplayName = "Review")]
		protected virtual IEnumerable reviewPutAway(PXAdapter adapter) => adapter.Get();

		public PXAction<Header> ViewOrder;
		[PXButton, PXUIField(DisplayName = "View Order")]
		protected virtual IEnumerable viewOrder(PXAdapter adapter)
		{
			POReceiptLineSplit currentSplit = Received.Current;
			if (currentSplit == null)
				return adapter.Get();

			POReceiptLine currentLine = Received.Search<POReceiptLineSplit.splitLineNbr>(currentSplit.SplitLineNbr).FirstOrDefault()?.GetItem<POReceiptLine>();
			if (currentLine == null)
				return adapter.Get();

			var orderEntry = PXGraph.CreateInstance<POOrderEntry>();
			orderEntry.Document.Current = orderEntry.Document.Search<POOrder.orderType, POOrder.orderNbr>(currentLine.POType, currentLine.PONbr);
			throw new PXRedirectRequiredException(orderEntry, true, nameof(ViewOrder)) { Mode = PXBaseRedirectException.WindowMode.NewWindow };
		}

		public PXAction<Header> ViewTransferInfo;
		[PXButton, PXUIField(DisplayName = "Transfer Allocations")]
		protected virtual void viewTransferInfo() => TransferSplitLinks.AskExt();
		#endregion

		#region Event Handlers
		protected virtual void _(Events.FieldUpdated<Header, WMSHeader.qty> e)
		{
			if (HeaderView.Current.ScanState == ScanStates.ReceiptConfirmation)
				SetScanState(ScanStates.ReceiptConfirmation);
			else if (HeaderView.Current.ScanState == ScanStates.PutAwayConfirmation)
				SetScanState(ScanStates.PutAwayConfirmation);
			HeaderView.Current.ForceInsertLine = false;
		}

		protected override void _(Events.RowSelected<Header> e)
		{
			base._(e);

			if (e.Row == null)
				return;

			if (IsCartRequired(e.Row))
				Cart.Current = Cart.Select();

			ScanModePutAway.SetEnabled(e.Row.Mode != Modes.PutAway);
			ScanModeReceive.SetEnabled(e.Row.Mode != Modes.Receive);

			bool isNotReleased = e.Row.RefNbr.With(nbr => POReceipt.PK.Find(Base, nbr)?.Released) == false;
			if (isNotReleased == false)
				Received.Cache.Adjust<PXUIFieldAttribute>().ForAllFields(a => a.Enabled = false);
			Received.Cache.Adjust<PXUIFieldAttribute>().For<POReceiptLineSplit.qty>(a => a.Visible = Receipt?.WMSSingleOrder != true);

			Received.Cache.AllowInsert = isNotReleased;
			Received.Cache.AllowUpdate = isNotReleased;
			Received.Cache.AllowDelete = isNotReleased;

			ReviewReceive.SetVisible(Base.IsMobile && e.Row.Mode == Modes.Receive);
			ReviewPutAway.SetVisible(Base.IsMobile && e.Row.Mode == Modes.PutAway);
			ScanReleaseTransfer.SetVisible(e.Row.Mode == Modes.PutAway);

			new[] {
				ScanReleaseReceipt,
				ScanCompletePOLines
			}
			.Modify(b => b.SetVisible(e.Row.Mode == Modes.Receive))
			.Modify(b => b.SetEnabled(isNotReleased))
			.Consume();

			bool hasSomethingToConfirm = e.Row.Remove == true
				|| e.Row.Mode == Modes.Receive && isNotReleased && Received.SelectMain().Any(s => s.ReceivedQty != s.Qty)
				|| e.Row.Mode == Modes.PutAway && (IsCartRequired(e.Row) == false || e.Row.CartLoaded == false) && !isNotReleased && Received.SelectMain().Any(s => s.PutAwayQty != s.ReceivedQty)
				|| e.Row.Mode == Modes.PutAway && IsCartRequired(e.Row) == true && e.Row.CartLoaded == true && !isNotReleased && Received.SelectMain().Any(s => GetCartQty(s) > 0);
			ScanConfirm.SetEnabled(hasSomethingToConfirm);
			ScanRemove.SetEnabled(e.Row.Remove != true && e.Row.RefNbr != null && (isNotReleased ? e.Row.Mode == Modes.Receive : (e.Row.Mode == Modes.PutAway && (IsCartRequired(e.Row) == false || e.Row.CartID != null))));

			INItemPlanIDAttribute.SetReleaseMode<POReceiptLineSplit.planID>(Base.splits.Cache, e.Row.Mode == Modes.PutAway);

			if (String.IsNullOrEmpty(e.Row.RefNbr))
			{
				if (String.IsNullOrEmpty(e.Row.PONbr))
					Base.Document.Current = null;
				ScanReleaseTransfer.SetEnabled(false);
			}
			else
			{
				Base.Document.Current = e.Row.Released == true ? POReceipt.PK.Find(Base, e.Row.RefNbr) : Base.Document.Search<POReceipt.receiptNbr>(e.Row.RefNbr);
				INRegister transfer = e.Row.Mode == Modes.PutAway ? Transfer : null;
				HeaderView.Current.TransferRefNbr = transfer?.RefNbr;
				ScanReleaseTransfer.SetEnabled(transfer?.Released == false);
			}
		}

		protected virtual void _(Events.FieldDefaulting<INRegister.docType> e) => e.NewValue = INDocType.Transfer;

		protected virtual void _(Events.ExceptionHandling<POReceiptLine, POReceiptLine.receiptQty> e)
		{
			ReceiveSplits
				.SelectMain()
				.Where(r => r.LineNbr == e.Row.LineNbr)
				.Select(r => redirectQtyErrorToReceivedQty
					? Received.Cache.RaiseExceptionHandling<POReceiptLineSplit.receivedQty>(r, r.ReceivedQty, e.Exception)
					: Received.Cache.RaiseExceptionHandling<POReceiptLineSplit.qty>(r, e.NewValue, e.Exception))
				.Consume();
		}
		private bool redirectQtyErrorToReceivedQty = false;

		protected virtual void _(Events.RowUpdated<POReceivePutAwayUserSetup> e) => e.Row.IsOverridden = !e.Row.SameAs(Setup.Current);
		protected virtual void _(Events.RowInserted<POReceivePutAwayUserSetup> e) => e.Row.IsOverridden = !e.Row.SameAs(Setup.Current);

		protected virtual void _(Events.RowPersisted<Header> e) => e.Row.RefNbr = Receipt?.ReceiptNbr;

		protected virtual void _(Events.RowSelected<POReceipt> e)
		{
			if (HeaderView.Current.ScanState == ScanStates.Wait)
			{
				if (HeaderView.Current.Mode == Modes.Receive && e.Row?.Released == true)
				{
					SetScanState(GetDefaultState(), Msg.ReceiptIsReleased);
				}
				else if (HeaderView.Current.Mode == Modes.PutAway && e.Row != null && Transfer == null)
				{
					if (CanPutAway)
						SetScanState(IsSingleReceivingLocation ? ScanStates.Item : ScanStates.Location, Msg.TransferIsReleased);
					else if (Base.IsMobile)
						Clear(Msg.TransferIsReleased);
					else
						SetScanState(GetDefaultState(), Msg.TransferIsReleased);
				}
			}
		}
		#endregion

		#region DAC overrides
		[PXCustomizeBaseAttribute(typeof(StockItemAttribute), nameof(StockItemAttribute.Visible), true)]
		[PXCustomizeBaseAttribute(typeof(StockItemAttribute), nameof(StockItemAttribute.Enabled), false)]
		protected virtual void _(Events.CacheAttached<POReceiptLineSplit.inventoryID> e) { }

		[PXCustomizeBaseAttribute(typeof(SiteAttribute), nameof(SiteAttribute.Enabled), false)]
		protected virtual void _(Events.CacheAttached<POReceiptLineSplit.siteID> e) { }

		[PXCustomizeBaseAttribute(typeof(PXUIFieldAttribute), nameof(PXUIFieldAttribute.Enabled), false)]
		protected virtual void _(Events.CacheAttached<POReceiptLineSplit.qty> e) { }
		#endregion

		private POReceipt Receipt => Base.CurrentDocument.Current;
		private INRegister Transfer =>
			IsCartRequired(HeaderView.Current)
			? (INRegister)
				SelectFrom<INRegister>
				.InnerJoin<POCartReceipt>.On<
					INRegister.docType.IsEqual<INDocType.transfer>
					.And<POCartReceipt.transferNbr.IsEqual<INRegister.refNbr>>>
				.Where<INRegister.transferType.IsEqual<INTransferType.oneStep>
					.And<INRegister.released.IsEqual<False>>
					.And<POCartReceipt.receiptNbr.IsEqual<Header.refNbr.FromCurrent>>
					.And<POCartReceipt.siteID.IsEqual<Header.siteID.FromCurrent>>
					.And<POCartReceipt.cartID.IsEqual<Header.cartID.FromCurrent>>>
				.View.ReadOnly.SelectSingleBound(Base, new[] { HeaderView.Current })
			: (INRegister)
				SelectFrom<INRegister>
				.Where<INRegister.docType.IsEqual<INDocType.transfer>
					.And<INRegister.transferType.IsEqual<INTransferType.oneStep>>
					.And<INRegister.released.IsEqual<False>>
					.And<INRegister.pOReceiptType.IsEqual<POReceipt.receiptType.FromCurrent>>
					.And<INRegister.pOReceiptNbr.IsEqual<POReceipt.receiptNbr.FromCurrent>>>
				.View.ReadOnly.SelectSingleBound(Base, new[] { Receipt });

		protected override WMSModeOf<ReceivePutAway, ReceivePutAwayHost> DefaultMode =>
			Setup.Current.ShowReceivingTab == true ? Modes.Receive :
			Setup.Current.ShowPutAwayTab == true ? Modes.PutAway :
			Modes.Free;
		public override string CurrentModeName =>
			HeaderView.Current.Mode == Modes.Receive ? Msg.ReceiveMode :
			HeaderView.Current.Mode == Modes.PutAway ? Msg.PutAwayMode :
			Msg.FreeMode;
		protected override string GetModePrompt()
		{
			if (HeaderView.Current.Mode == Modes.Receive)
			{
				if (HeaderView.Current.RefNbr == null)
					return Localize(Msg.ReceiptPrompt);

				if (IsLocationRequired(HeaderView.Current) && (IsSingleReceivingLocation || HeaderView.Current.ReceiptType == POReceiptType.POReturn))
					return Localize(Msg.LocationPrompt);
				else
					return Localize(Msg.InventoryPrompt);
			}
			if (HeaderView.Current.Mode == Modes.PutAway)
			{
				if (IsCartRequired(HeaderView.Current) && HeaderView.Current.CartID == null)
					return Localize(Msg.CartPrompt);
				if (HeaderView.Current.RefNbr == null)
					return Localize(Msg.ReceiptPrompt);
				return Localize(Msg.InventoryPrompt);
			}
			return null;
		}

		#region Scan State logic
		protected override string GetDefaultState(Header header = null) => IsCartRequired(header ?? HeaderView.Current) ? ScanStates.Cart : ScanStates.RefNbr;

		protected override bool ProcessCommand(string barcode)
		{
			switch (barcode)
			{
				case ScanCommands.Confirm:
					if (HeaderView.Current.Mode == Modes.Receive)
					{
						if (Receipt.Released == true) return true;
						ConfirmReceive();
						return true;
					}
					else if (HeaderView.Current.Mode == Modes.PutAway)
					{
						if (Receipt.Released == false) return true;
						ConfirmPutAway();
						return true;
					}
					return false;

				case ScanCommands.Remove:
					if (HeaderView.Current.ScanState == ScanStates.Command || HeaderView.Current.ForceInsertLine == true)
					{
						if (HeaderView.Current.Mode == Modes.Receive)
						{
							ClearHeaderInfo(false);
							SetScanState(IsLocationRequired(HeaderView.Current) && !IsSingleReceivingLocation && ViseVersaFlow ? ScanStates.Location : ScanStates.Item, Msg.RemoveMode);
						}
						else if (HeaderView.Current.Mode == Modes.PutAway)
							SetScanState(IsSingleReceivingLocation ? ScanStates.Item : ScanStates.Location, Msg.RemoveMode);
					}
					else
					{
						Report(Msg.RemoveMode);
					}
					HeaderView.Current.Remove = true;
					HeaderView.Current.ForceInsertLine = false;
					return true;

				case ScanCommands.CartIn:
					if (IsCartRequired(HeaderView.Current) == false) return false;
					ClearHeaderInfo();
					HeaderView.Current.CartLoaded = false;
					if (CanPutAway)
						SetScanState(IsSingleReceivingLocation ? ScanStates.Item : ScanStates.Location, Msg.CartLoading);
					else
						SetScanState(ScanStates.Command, Msg.CartLoading);
					return true;

				case ScanCommands.CartOut:
					if (IsCartRequired(HeaderView.Current) == false) return false;
					ClearHeaderInfo();
					HeaderView.Current.CartLoaded = true;
					if (CanPutAway)
						SetScanState(IsSingleReceivingLocation ? ScanStates.Item : ScanStates.Location, Msg.CartUnloading);
					else
						SetScanState(ScanStates.Command, Msg.CartUnloading);
					return true;

				case ScanCommands.ReleaseReceipt:
					if (Receipt.Released == true) return true;
					ReleaseReceipt(false);
					return true;

				case ScanCommands.CompletePOLines:
					if (Receipt.Released == true) return true;
					ReleaseReceipt(true);
					return true;

				case ScanCommands.ReleaseTransfer:
					if (Receipt.Released != true) return true;
					ReleaseTransfer();
					return true;

				default:
					return false;
			}
		}

		protected override bool ProcessByState(Header doc)
		{
			switch (doc.ScanState)
			{
				case ScanStates.ToLocation:
					ProcessToLocationBarcode(doc.Barcode);
					return true;

				default:
					return base.ProcessByState(doc);
			}
		}

		protected override void ApplyState(string state)
		{
			switch (state)
			{
				case ScanStates.Command:
					Prompt(Msg.UseCommandToContinue);
					break;
				case ScanStates.RefNbr:
					Prompt(Msg.ReceiptPrompt);
					break;
				case ScanStates.Cart:
					Prompt(Msg.CartPrompt);
					break;
				case ScanStates.Item:
					Prompt(Msg.InventoryPrompt);
					break;
				case ScanStates.Location:
					if (HeaderView.Current.Mode == Modes.Receive)
					{
						if (IsLocationRequired(HeaderView.Current))
							Prompt(Msg.LocationPrompt);
						else if (ViseVersaFlow) // location -> item -> ...
							SetScanState(ScanStates.Item);
						else // item -> location -> confirm
							SetScanState(ScanStates.ReceiptConfirmation);
					}
					else if (HeaderView.Current.Mode == Modes.PutAway)
					{
						if (IsSingleReceivingLocation)
							SetScanState(ScanStates.Item);
						else
							Prompt(Msg.LocationPrompt);
					}
					break;
				case ScanStates.ToLocation:
					Prompt(Msg.ToLocationPrompt);
					break;
				case ScanStates.LotSerial:
					Prompt(Msg.LotSerialPrompt);
					break;
				case ScanStates.ExpireDate:
					Prompt(Msg.LotSerialExpireDatePrompt);
					break;
				case ScanStates.ReceiptConfirmation:
					if (ExplicitLineConfirmation)
						Prompt(Msg.ReceiptConfirmationPrompt, HeaderView.Cache.GetValueExt<Header.inventoryID>(HeaderView.Current), HeaderView.Current.Qty);
					else
						ConfirmReceive();
					break;
				case ScanStates.PutAwayConfirmation:
					if (ExplicitLineConfirmation)
						Prompt(Msg.PutAwayConfirmationPrompt, HeaderView.Cache.GetValueExt<Header.inventoryID>(HeaderView.Current), HeaderView.Current.Qty);
					else
						ConfirmPutAway();
					break;
			}
		}

		protected override bool PrepareRedirect(string command)
		{
			switch (command)
			{
				case ScanRedirects.ModeReceive:
					if (HeaderView.Current.RefNbr != null && Receipt.Released != false)
					{
						ReportError(Msg.CouldNotReceive);
						return false;
					}
					else return true;

				case ScanRedirects.ModePutAway:
					if (HeaderView.Current.RefNbr != null && Receipt.Released != true)
					{
						ReportError(Msg.CouldNotPutAway);
						return false;
					}
					else return true;
			}
			return true;
		}

		protected override void ProcessDocumentNumber(string barcode)
		{
			bool newReceipt = false;
			var receipt = (POReceipt)PXSelectorAttribute.Select<Header.refNbr>(HeaderView.Cache, HeaderView.Current, barcode);
			if (receipt == null && HeaderView.Current.Mode == Modes.Receive)
			{
				POOrder order = POOrder.PK.Find(Base, POOrderType.RegularOrder, barcode);
				if (order != null)
				{
					if (order.Status != POOrderStatus.Open)
					{
						ReportError(Msg.POOrderInvalid, order.OrderNbr);
						return;
					}

					var poSplitsGrouped =
						SelectFrom<POLine>
						.Where<POLine.FK.Order.SameAsCurrent.And<POLine.siteID.IsNotNull>>
						.AggregateTo<GroupBy<POLine.siteID>>
						.View.SelectMultiBound(Base, new[] { order });
					if (poSplitsGrouped.Count != 1)
					{
						ReportError(Msg.POOrderMultiSites, order.OrderNbr);
						return;
					}

					receipt =
						SelectFrom<POReceipt>
						.InnerJoin<POOrderReceipt>.On<POOrderReceipt.FK.Receipt>
						.LeftJoin<Vendor>.On<POReceipt.FK.Vendor>.SingleTableOnly
						.Where<
							POOrderReceipt.FK.Order.SameAsCurrent
							.And<POReceipt.released.IsEqual<False>>
							.And<Vendor.bAccountID.IsNull.Or<Match<Vendor, AccessInfo.userName.FromCurrent>>>
							.And<POReceipt.wMSSingleOrder.IsEqual<False>.Or<POReceipt.createdByID.IsEqual<AccessInfo.userID.FromCurrent>>>>
						.OrderBy<POReceipt.wMSSingleOrder.Desc>
						.View.SelectSingleBound(Base, new[] { order }, barcode);

					if (receipt == null)
					{
						try
						{
							int linesCount =
								SelectFrom<POLine>
								.Where<POLine.FK.Order.SameAsCurrent>
								.View.SelectMultiBound(Base, new[] { order }).Count();

							receipt = linesCount > LargeOrderLinesCount
								? CreateEmptyReceiptFrom(order)
								: Base.CreateReceiptFrom(order);

							receipt.WMSSingleOrder = true;
							receipt.SiteID = ((POLine)poSplitsGrouped).SiteID;
							receipt.AutoCreateInvoice = false;
							receipt.Hold = true;
							receipt = Base.Document.Current = Base.Document.Update(receipt);

							foreach(POReceiptLineSplit rLine in Base.splits.Cache.Inserted)
							{
								INLotSerClass lsClass = InventoryItem.PK.Find(Base, rLine.InventoryID).With(i => INLotSerClass.PK.Find(Base, i.LotSerClassID));
								if (lsClass != null && lsClass.LotSerTrack.IsIn(INLotSerTrack.LotNumbered, INLotSerTrack.SerialNumbered) && lsClass.LotSerTrackExpiration == true && lsClass.AutoNextNbr == true && rLine.ExpireDate == null)
								{
									DateTime? expireDate =
										SelectFrom<INItemLotSerial>
										.Where<INItemLotSerial.inventoryID.IsEqual<POReceiptLineSplit.inventoryID.FromCurrent>>
										.OrderBy<
											Desc<True.When<INItemLotSerial.lotSerialNbr.IsEqual<POReceiptLineSplit.lotSerialNbr.FromCurrent>>.Else<False>>,
											Desc<INItemLotSerial.expireDate>>
										.View.SelectSingleBound(Base, new[] { rLine })
										.RowCast<INItemLotSerial>()
										.FirstOrDefault()?
										.ExpireDate ?? Base.Accessinfo.BusinessDate;
									Base.splits.Cache.SetValueExt<POReceiptLineSplit.expireDate>(rLine, expireDate);
								}

								rLine.Qty = 0;
								Base.splits.Cache.Update(rLine);
							}

							HeaderView.Current.PONbr = order.OrderNbr;
							newReceipt = true;

							Save.Press();
						}
						catch
						{
							Base.Clear();
							ReportError(Msg.POOrderUnableToCreateReceipt, order.OrderNbr);
							return;
						}
					}
					else
					{
						HeaderView.Current.PONbr = order.OrderNbr;
					}
				}
			}

			if (receipt == null)
			{
				ReportError(Msg.ReceiptMissing, barcode);
				return;
			}

			int? siteID = receipt.SiteID;
			if (newReceipt == false)
			{
				if (IsValid<Header.refNbr>(receipt.ReceiptNbr, out string error) == false)
				{
					ReportError(error);
					return;
				}

				var splitsGrouped =
					SelectFrom<POReceiptLineSplit>
					.Where<POReceiptLineSplit.receiptNbr.IsEqual<@P.AsString>
						.And<POReceiptLineSplit.siteID.IsNotNull>>
					.AggregateTo<GroupBy<POReceiptLineSplit.siteID>>
					.View.Select(Base, receipt.ReceiptNbr);
				if (splitsGrouped.Count != 1)
				{
					ReportError(Msg.ReceiptMultiSites, receipt.ReceiptNbr);
					return;
				}
				else if (siteID == null)
					siteID = ((POReceiptLineSplit)splitsGrouped).SiteID;
			}

			if (HeaderView.Current.Mode == Modes.PutAway)
			{
				POReceiptLineSplit notPutSplit =
					SelectFrom<POReceiptLineSplit>
					.Where<POReceiptLineSplit.receiptNbr.IsEqual<@P.AsString>
						.And<POReceiptLineSplit.putAwayQty.IsLess<POReceiptLineSplit.qty>>>
					.View.Select(Base, receipt.ReceiptNbr);

				if (notPutSplit == null)
				{
					INRegister notReleasedTransfer =
						SelectFrom<INRegister>
						.Where<INRegister.docType.IsEqual<INDocType.transfer>
							.And<INRegister.transferType.IsEqual<INTransferType.oneStep>>
							.And<INRegister.released.IsEqual<False>>
							.And<INRegister.pOReceiptType.IsEqual<POReceipt.receiptType.FromCurrent>>
							.And<INRegister.pOReceiptNbr.IsEqual<POReceipt.receiptNbr.FromCurrent>>>
						.View.ReadOnly.SelectSingleBound(Base, new[] { receipt });

					Base.Document.Current = receipt;
					decimal cartQty = IsCartRequired(HeaderView.Current)
						? AllCartSplitLinks.SelectMain().Sum(_ => _.Qty ?? 0)
						: 0;
					Base.Document.Current = null;

					if (notReleasedTransfer == null && cartQty == 0)
					{
						ReportError(Msg.AlreadyPutAwayInFull, receipt.ReceiptNbr);
						return;
					}
				}
			}

			int? defaultLocationID = HeaderView.Current.Mode == Modes.Receive && Setup.Current.DefaultReceivingLocation == true
				? INSite.PK.Find(Base, siteID)?.ReceiptLocationID
				: null;

			HeaderView.Current.RefNbr = receipt.ReceiptNbr;
			HeaderView.Current.SiteID = siteID;
			HeaderView.Current.DefaultLocationID = defaultLocationID;
			HeaderView.Current.LocationID = defaultLocationID;
			HeaderView.Current.ReceiptType = receipt.ReceiptType;
			HeaderView.Current.TranDate = receipt.ReceiptDate;
			Base.Document.Current = receipt;

			Report(newReceipt ? Msg.ReceiptReadyNew : Msg.ReceiptReady, receipt.ReceiptNbr);

			if (HeaderView.Current.Mode == Modes.Receive)
			{
				if (CanReceive == false)
					SetScanState(ScanStates.Command, Msg.ReceiptReceived, receipt.ReceiptNbr);
				else if (IsLocationRequired(HeaderView.Current) && ViseVersaFlow && defaultLocationID == null)
					SetScanState(ScanStates.Location); // REFNBR -> location -> item -> ... -> confirm
				else
					SetScanState(ScanStates.Item); // REFNBR -> item -> ... -> location -> confirm
			}
			else if (HeaderView.Current.Mode == Modes.PutAway)
			{
				if (IsCartRequired(HeaderView.Current) && HeaderView.Current.CartID == null)
					SetScanState(ScanStates.Cart);
				else if (CanPutAway)
					SetScanState(IsSingleReceivingLocation ? ScanStates.Item : ScanStates.Location);
				else
					SetScanState(ScanStates.Command);
			}
		}

		protected override void ProcessCartBarcode(string barcode)
		{
			// for put away mode only
			if (HeaderView.Current.Mode != Modes.PutAway)
				throw new NotSupportedException();

			INCart cart = ReadCartByBarcode(barcode);
			if (cart == null)
			{
				ReportError(Msg.CartMissing, barcode);
			}
			else if (HeaderView.Current.RefNbr != null && cart.SiteID != HeaderView.Current.SiteID)
			{
				ReportError(Msg.CartInvalidSite, barcode);
			}
			else
			{
				HeaderView.Current.CartID = cart.CartID;
				HeaderView.Current.SiteID = cart.SiteID;
				Cart.Current = Cart.Select();

				Report(Msg.CartReady, cart.CartCD);

				if (HeaderView.Current.RefNbr == null)
					SetScanState(ScanStates.RefNbr);
				else if (CanPutAway)
					SetScanState(IsSingleReceivingLocation ? ScanStates.Item : ScanStates.Location);
				else
					SetScanState(ScanStates.Command);
			}
		}

		protected override void ProcessItemBarcode(string barcode)
		{
			var item = ReadItemByBarcode(barcode, INPrimaryAlternateType.VPN);
			if (item == null)
			{
				ReportError(Msg.InventoryMissing, barcode);
				return;
			}

			INItemXRef xref = item;
			InventoryItem inventoryItem = item;
			INLotSerClass lsclass = item;

			HeaderView.Current.InventoryID = xref.InventoryID;
			HeaderView.Current.SubItemID = xref.SubItemID;
			HeaderView.Current.UOM = xref.UOM ?? inventoryItem.PurchaseUnit;
			HeaderView.Current.LotSerTrack = lsclass.LotSerTrack;
			HeaderView.Current.LotSerAssign = lsclass.LotSerAssign;
			HeaderView.Current.LotSerTrackExpiration = lsclass.LotSerTrackExpiration;
			HeaderView.Current.AutoNextNbr = lsclass.AutoNextNbr;

			Report(Msg.InventoryReady, inventoryItem.InventoryCD);

			if (lsclass.LotSerTrack.IsIn(INLotSerTrack.LotNumbered, INLotSerTrack.SerialNumbered) && lsclass.LotSerAssign == INLotSerAssign.WhenReceived &&
				(HeaderView.Current.Mode == Modes.PutAway || DefaultLotSerial == false || lsclass.AutoNextNbr == false))
			{
				SetScanState(ScanStates.LotSerial);
			}
			else if (HeaderView.Current.Mode == Modes.Receive)
			{
				if (IsLocationRequired(HeaderView.Current) && !ViseVersaFlow)
					SetScanState(ScanStates.Location); // refNbr -> ITEM -> location -> confirm
				else
					SetScanState(ScanStates.ReceiptConfirmation); // refNbr -> location -> ITEM -> confirm
			}
			else if (HeaderView.Current.Mode == Modes.PutAway)
			{
				if (IsCartRequired(HeaderView.Current) == false || HeaderView.Current.CartLoaded == true)
					SetScanState(ScanStates.ToLocation);
				else if (HeaderView.Current.CartLoaded == false)
					SetScanState(ScanStates.PutAwayConfirmation);
			}
		}

		protected override void ProcessLotSerialBarcode(string barcode)
		{
			if (IsValid<Header.lotSerialNbr>(barcode, out string error) == false)
			{
				ReportError(error);
				return;
			}

			if (HeaderView.Current.Mode == Modes.PutAway && PutAway.SelectMain().All(t => t.LotSerialNbr != barcode))
			{
				ReportError(Msg.LotSerialMissingInReceipt, barcode);
				return;
			}

			HeaderView.Current.LotSerialNbr = barcode;
			Report(Msg.LotSerialReady, barcode);

			if (HeaderView.Current.Mode == Modes.Receive)
			{
				if (HeaderView.Current.Remove == false && HeaderView.Current.ReceiptType != POReceiptType.POReturn && HeaderView.Current.LotSerTrackExpiration == true && HeaderView.Current.LotSerAssign == INLotSerAssign.WhenReceived && (DefaultExpireDate == false || EnsureExpireDateDefault() == null))
					SetScanState(ScanStates.ExpireDate);
				else if (IsLocationRequired(HeaderView.Current) && !ViseVersaFlow)
					SetScanState(ScanStates.Location); // refNbr -> item -> LOTSERIAL -> location -> confirm
				else
					SetScanState(ScanStates.ReceiptConfirmation); // refNbr -> location -> item -> LOTSERIAL -> confirm
			}
			else if (HeaderView.Current.Mode == Modes.PutAway)
			{
				if (IsCartRequired(HeaderView.Current) == false || HeaderView.Current.CartLoaded == true)
					SetScanState(ScanStates.ToLocation);
				else if (HeaderView.Current.CartLoaded == false)
					SetScanState(ScanStates.PutAwayConfirmation);
			}
		}

		protected override void ProcessExpireDate(string barcode)
		{
			// for receive mode only
			if (HeaderView.Current.Mode != Modes.Receive)
				throw new NotSupportedException();

			if (DateTime.TryParse(barcode.Trim(), out DateTime value) == false)
			{
				ReportError(Msg.LotSerialExpireDateBadFormat);
				return;
			}

			if (IsValid<Header.expireDate>(value, out string error) == false)
			{
				ReportError(error);
				return;
			}

			HeaderView.Current.ExpireDate = value;
			Report(Msg.LotSerialExpireDateReady, barcode);

			if (IsLocationRequired(HeaderView.Current) && !ViseVersaFlow)
				SetScanState(ScanStates.Location); // refNbr -> item -> lotserial -> EXPIREDATE -> location -> confirm
			else
				SetScanState(ScanStates.ReceiptConfirmation); // refNbr -> location -> item -> lotserial -> EXPIREDATE -> confirm
		}

		protected override void ProcessLocationBarcode(string barcode)
		{
			INLocation location = ReadLocationByBarcode(HeaderView.Current.SiteID, barcode);
			if (location == null)
				return;

			HeaderView.Current.LocationID = location.LocationID;
			Report(Msg.LocationReady, location.LocationCD);

			if (HeaderView.Current.Mode == Modes.Receive)
			{
				if (ViseVersaFlow)
					SetScanState(ScanStates.Item); // refNbr -> LOCATION -> item -> ... -> confirm
				else
					SetScanState(ScanStates.ReceiptConfirmation); // refNbr -> item -> ... -> LOCATION -> confirm
			}
			else if (HeaderView.Current.Mode == Modes.PutAway)
			{
				SetScanState(ScanStates.Item);
			}
		}

		protected virtual void ProcessToLocationBarcode(string barcode)
		{
			INLocation location = ReadLocationByBarcode(HeaderView.Current.SiteID, barcode);
			if (location == null)
				return;

			HeaderView.Current.ToLocationID = location.LocationID;
			SetScanState(ScanStates.PutAwayConfirmation, Msg.ToLocationReady, location.LocationCD);
		}
		#endregion

		#region Receive line confirmation
		private void ConfirmReceive()
		{
			bool isSerialItem = HeaderView.Current.LotSerTrack == INLotSerTrack.SerialNumbered;

			if (HeaderView.Current.Remove == false)
				ProcessReceived();
			else
				ProcessReceiveRemoved();

			if (!isSerialItem && Info.Current.MessageType == WMSMessageTypes.Information)
				HeaderView.Current.IsQtyOverridable = true;
		}

		private WMSFlowStatus ProcessReceiveRemoved()
		{
			WMSFlowStatus Implementation()
			{
				Header header = HeaderView.Current;

				if (header.InventoryID == null)
					return WMSFlowStatus.Fail(Msg.InventoryNotSet).ClearIsNeeded;

				bool isLocationRequired = IsLocationRequired(header);
				if (isLocationRequired && header.LocationID == null)
					return WMSFlowStatus.Fail(Msg.LocationNotSet).ClearIsNeeded;

				if (header.LotSerTrack.IsIn(INLotSerTrack.LotNumbered, INLotSerTrack.SerialNumbered) && header.LotSerAssign == INLotSerAssign.WhenReceived && DefaultLotSerial == false && header.LotSerialNbr == null)
					return WMSFlowStatus.Fail(Msg.LotSerialNotSet).ClearIsNeeded;

				if (header.LocationID == null && header.DefaultLocationID != null)
					header.LocationID = header.DefaultLocationID;

				var allSplits = Received.SelectMain().Where(
					r => r.InventoryID == header.InventoryID
						 && r.SubItemID == header.SubItemID).ToArray();

				bool IsDeductable(POReceiptLineSplit r) => r.LotSerialNbr == (header.LotSerialNbr ?? r.LotSerialNbr) && r.LocationID == (header.LocationID ?? r.LocationID);

				var deductableSplits = allSplits.Where(IsDeductable).ToArray();
				if (deductableSplits.Any() == false)
					return WMSFlowStatus.Fail(Msg.NothingToRemove).ClearIsNeeded;

				decimal qty = header.GetBaseQty(Base);

				if (deductableSplits.Sum(s => s.ReceivedQty.Value) - qty < 0)
					return WMSFlowStatus.Fail(Msg.Underpicking);

				if (header.LotSerTrack == INLotSerTrack.SerialNumbered)
				{
					var serialSplit = deductableSplits.Reverse().First(s => s.ReceivedQty != 0);

					if (serialSplit.PONbr == null)
						Received.Delete(serialSplit);
					else
					{
						serialSplit.ReceivedQty = 0;
						Received.Update(serialSplit);
					}
				}
				else
				{
					decimal undeductedQty = qty;

					var splitGroups = allSplits.Reverse().GroupBy(s => s.LineNbr).ToDictionary(g => g.Key, g => g.OrderByDescending(IsDeductable).ToArray());
					foreach (var group in splitGroups.OrderByDescending(kvp => kvp.Key))
					{
						decimal groupUndeductedQty = Math.Min(group.Value.Where(IsDeductable).Sum(s => s.ReceivedQty.Value), undeductedQty);
						if (groupUndeductedQty == 0)
							continue;

						var removedSplits = new HashSet<int>();

						bool isQtyRearranged = group.Value.Any(s => s.PONbr == null);

						decimal groupCurrentQty = groupUndeductedQty;
						decimal rearrangeQty = 0;
						for (int i = 0; i < group.Value.Length; i++)
						{
							var split = group.Value[i];
							if (IsDeductable(split) == false)
								break;

							decimal currentQty = Math.Min(split.ReceivedQty.Value, groupCurrentQty);
							if (i == group.Value.LastIndex() && split.PONbr != null)
							{
								split.Qty += rearrangeQty;
								split.ReceivedQty -= currentQty;
								Received.Update(split);
								isQtyRearranged = true;
								break;
							}
							else if (currentQty < split.ReceivedQty)
							{
								split.ReceivedQty -= currentQty;
								Received.Update(split);
							}
							else if (split.PONbr == null)
							{
								Base.transactions.Delete(Base.transactions.Search<POReceiptLine.lineNbr>(split.LineNbr));
							}
							else
							{
								removedSplits.Add(split.SplitLineNbr.Value);
								rearrangeQty += split.Qty.Value;
								Received.Delete(split);
							}

							groupCurrentQty -= currentQty;
							if (groupCurrentQty == 0)
								break;
						}

						if (isQtyRearranged == false)
						{
							var donorSplit = group.Value.FirstOrDefault(s => IsDeductable(s) == false) ?? group.Value.First(s => s.SplitLineNbr.Value.IsNotIn(removedSplits));
							donorSplit.Qty += rearrangeQty;
							Received.Update(donorSplit);
						}

						undeductedQty -= groupUndeductedQty;
						if (undeductedQty == 0)
							break;
					}
				}

				var newScanState = HeaderView.Current.ReceiptType == POReceiptType.POReturn && IsLocationRequired(HeaderView.Current) && !IsSingleReceivingLocation
					? ScanStates.Location
					: ScanStates.Item;

				SetScanState(newScanState, Msg.InventoryRemoved, InventoryItem.PK.Find(Base, header.InventoryID.Value).InventoryCD, header.Qty, header.UOM);
				return WMSFlowStatus.Ok;
			}

			return ExecuteAndCompleteFlow(Implementation);
		}

		private WMSFlowStatus ProcessReceived()
		{
			WMSFlowStatus Implementation()
			{
				Header header = HeaderView.Current;
				decimal qty = header.GetBaseQty(Base);

				if (header.InventoryID == null)
					return WMSFlowStatus.Fail(Msg.InventoryNotSet).ClearIsNeeded;

				bool isLocationRequired = IsLocationRequired(header);
				if (isLocationRequired && header.LocationID == null)
					return WMSFlowStatus.Fail(Msg.LocationNotSet).ClearIsNeeded;

				if (header.LocationID == null && header.DefaultLocationID != null)
					header.LocationID = header.DefaultLocationID;

				bool enterableLotSerial = header.LotSerTrack.IsIn(INLotSerTrack.LotNumbered, INLotSerTrack.SerialNumbered) && header.LotSerAssign == INLotSerAssign.WhenReceived;
				bool inputLotSerial = enterableLotSerial && DefaultLotSerial == false || header.AutoNextNbr == false;
				if (enterableLotSerial)
				{
					if (inputLotSerial && header.LotSerialNbr == null)
						return WMSFlowStatus.Fail(Msg.LotSerialNotSet).ClearIsNeeded;

					if (inputLotSerial && header.LotSerTrack == INLotSerTrack.SerialNumbered && qty != 1)
						return WMSFlowStatus.Fail(Msg.SerialItemNotComplexQty);

					if (header.ReceiptType != POReceiptType.POReturn && header.LotSerTrackExpiration == true && DefaultExpireDate == false && header.ExpireDate == null)
						return WMSFlowStatus.Fail(Msg.LotSerialExpireDateNotSet).ClearIsNeeded;
				}

				void SetLotAndLoc(ILSMaster s, bool newLine = false)
				{
					if (base.IsLocationRequired(header) && header.LocationID != null)
						s.LocationID = header.LocationID;
					if (inputLotSerial && header.LotSerialNbr != null)
						s.LotSerialNbr = header.LotSerialNbr;

					DateTime? expireDate = newLine ? header.ExpireDate : EnsureExpireDateDefault();
					if (header.LotSerTrackExpiration == true && (newLine || DefaultExpireDate == false) && expireDate != null)
						s.ExpireDate = expireDate;
				}

				var itemSplits = Received.SelectMain().Where(s => s.InventoryID == header.InventoryID && s.SubItemID == header.SubItemID);

				if (Receipt.WMSSingleOrder == true && Receipt.OrigPONbr != null && itemSplits.Any() == false)
				{
					Base.AddPurchaseOrder(POOrder.PK.Find(Base, POOrderType.RegularOrder, Receipt.OrigPONbr), header.InventoryID, header.SubItemID);
					itemSplits = Received.Cache.Inserted.Cast<POReceiptLineSplit>();
				}

				itemSplits = itemSplits
					.OrderByDescending(s => s.LotSerialNbr == (header.LotSerialNbr ?? s.LotSerialNbr))
					.ThenByDescending(s => s.LocationID == header.LocationID)
					.ToArray();

				if (inputLotSerial && header.LotSerTrack == INLotSerTrack.SerialNumbered && itemSplits.Any(s => s.LotSerialNbr == header.LotSerialNbr && s.ReceivedQty == 1))
					return WMSFlowStatus.Fail(Msg.SerialItemNotComplexQty).ClearIsNeeded;

				var donorSplits = itemSplits.Where(s => s.Qty - s.ReceivedQty > 0 || GetExtendedRestQty(s) > 0 || s.PONbr == null).ToArray();
				var acceptorSplits = new List<POReceiptLineSplit>();

				var exactSplits = donorSplits
					.Where(s =>
						isLocationRequired.Implies(s.LocationID == header.LocationID || s.ReceivedQty == 0) &&
						inputLotSerial.Implies(s.LotSerialNbr == header.LotSerialNbr || s.ReceivedQty == 0))
					.ToArray();

				bool newLineIsNeeded = qty > donorSplits.GroupBy(s => new { s.LineNbr, s.PONbr }).Sum(g => GetExtendedRestQty(g.First()));
				if (newLineIsNeeded && header.ForceInsertLine == false)
				{
					header.ForceInsertLine = true;
					Prompt(Msg.ConfirmQuestion);
					return WMSFlowStatus.Warning(Msg.ReceiveNewLineWarning, header.RefNbr, InventoryItem.PK.Find(Base, header.InventoryID.Value).InventoryCD, header.Qty, header.UOM);
				}

				decimal restQty = qty;
				if (restQty > 0)
				{
					foreach (var split in exactSplits)
					{
						if (restQty == 0) break;

						decimal currentQty = split.Qty.Value - split.ReceivedQty.Value;
						currentQty = Math.Min(currentQty, restQty);

						if (currentQty > 0)
						{
							Base.transactions.Current = Base.transactions.Search<POReceiptLine.lineNbr>(split.LineNbr);
							split.ReceivedQty += currentQty;
							SetLotAndLoc(split);
							Received.Update(split);

							restQty -= currentQty;
						}
					} 
				}

				if (restQty > 0)
				{
					foreach (var donorSplitGroup in donorSplits.Except(exactSplits).GroupBy(s => s.LineNbr))
					{
						decimal acceptorQty = 0;
						POReceiptLineSplit acceptorSplit = null;
						foreach (var donorSplit in donorSplitGroup)
						{
							if (restQty == 0) break;

							decimal currentQty = donorSplit.Qty.Value - donorSplit.ReceivedQty.Value;
							currentQty = Math.Min(currentQty, restQty);

							if (currentQty > 0)
							{
								Base.transactions.Current = Base.transactions.Search<POReceiptLine.lineNbr>(donorSplit.LineNbr);
								donorSplit.Qty -= currentQty;
								Received.Update(donorSplit);

								if (acceptorSplit == null)
									acceptorSplit = PXCache<POReceiptLineSplit>.CreateCopy(donorSplit);

								restQty -= currentQty;
								acceptorQty += currentQty;
							}
						}

						if (acceptorSplit != null)
						{
							acceptorSplit.SplitLineNbr = null;
							acceptorSplit.Qty = acceptorQty;
							acceptorSplit.ReceivedQty = acceptorQty;

							if (base.IsLocationRequired(header))
								acceptorSplit.LocationID = null;
							if (DefaultLotSerial == false)
								acceptorSplit.LotSerialNbr = null;
							if (DefaultExpireDate == false)
								acceptorSplit.ExpireDate = null;

							SetLotAndLoc(acceptorSplit, newLine: true);
							acceptorSplits.Add(Received.Insert(acceptorSplit));
						} 
					}
				}

				if (restQty > 0)
				{
					foreach (var exactSplitGroup in exactSplits.Concat(acceptorSplits).GroupBy(s => new { s.LineNbr, s.PONbr }).OrderBy(g => g.Key == null))
					{
						if (restQty == 0) break;

						decimal restGroupQty = exactSplitGroup.Key.PONbr == null ? restQty : Math.Min(restQty, GetExtendedRestQty(exactSplitGroup.First()));
						foreach (var split in exactSplitGroup.Where(s => (header.LotSerTrack == INLotSerTrack.SerialNumbered).Implies(s.ReceivedQty == 0)))
						{
							if (restGroupQty == 0) break;

							decimal currentQty = header.LotSerTrack == INLotSerTrack.SerialNumbered ? 1 : restGroupQty;

							if (currentQty > 0)
							{
								Base.transactions.Current = Base.transactions.Search<POReceiptLine.lineNbr>(split.LineNbr);
								split.ReceivedQty += currentQty;
								if (exactSplitGroup.Key == null)
									split.Qty += currentQty;
								SetLotAndLoc(split);
								Received.Update(split);

								restQty -= currentQty;
								restGroupQty -= currentQty;
							} 
						}
					}
				}

				if (restQty > 0)
				{
					foreach (var donorSplitGroup in
						donorSplits.Except(exactSplits.Concat(acceptorSplits))
						.Where(s => s.Qty.Value - s.ReceivedQty.Value > 0 || GetExtendedRestQty(s) > 0)
						.GroupBy(s => new { s.LineNbr, s.PONbr })
						.Where(g => g.Key.PONbr != null))
					{
						if (restQty == 0) break;

						decimal currentGroupQty = GetExtendedRestQty(donorSplitGroup.First());
						currentGroupQty = Math.Min(currentGroupQty, restQty);

						if (currentGroupQty > 0)
						{
							Base.transactions.Current = Base.transactions.Search<POReceiptLine.lineNbr>(donorSplitGroup.Key.LineNbr);

							decimal acceptorQty = 0;
							decimal restGroupQty = currentGroupQty;
							foreach (var donorSplit in donorSplitGroup)
							{
								if (restGroupQty == 0) break;

								decimal currentQty = donorSplit.Qty.Value - donorSplit.ReceivedQty.Value;
								currentQty = Math.Min(currentQty, restGroupQty);

								if (currentQty > 0)
								{
									donorSplit.Qty -= currentQty;
									Received.Update(donorSplit);

									restGroupQty -= currentQty;
									acceptorQty += currentQty;
								}

								if (restGroupQty > 0)
								{
									decimal extraQty = GetExtendedRestQty(donorSplit);
									extraQty = Math.Min(extraQty, restGroupQty);

									if (extraQty > 0)
									{
										restGroupQty -= extraQty;
										acceptorQty += extraQty;
									}
								}
							}

							if (acceptorQty > 0)
							{
								var acceptorSplit = PXCache<POReceiptLineSplit>.CreateCopy(donorSplitGroup.First());
								acceptorSplit.SplitLineNbr = null;
								acceptorSplit.Qty = acceptorQty;
								acceptorSplit.ReceivedQty = acceptorQty;

								if (base.IsLocationRequired(header))
									acceptorSplit.LocationID = null;
								if (DefaultLotSerial == false)
									acceptorSplit.LotSerialNbr = null;
								if (DefaultExpireDate == false)
									acceptorSplit.ExpireDate = null;

								SetLotAndLoc(acceptorSplit);
								Received.Insert(acceptorSplit);

								restQty -= acceptorQty;
							} 
						}
					}
				}

				if (restQty > 0 && newLineIsNeeded)
				{
					POReceiptLine newLine = Base.transactions.With(_ => _.Insert() ?? _.Insert());
					Base.transactions.SetValueExt<POReceiptLine.inventoryID>(newLine, header.InventoryID);
					Base.transactions.SetValueExt<POReceiptLine.subItemID>(newLine, header.SubItemID);
					Base.transactions.SetValueExt<POReceiptLine.siteID>(newLine, header.SiteID);
					Base.transactions.SetValueExt<POReceiptLine.uOM>(newLine, InventoryItem.PK.Find(Base, header.InventoryID.Value).BaseUnit);
					newLine.Qty = restQty;
					newLine = Base.transactions.Update(newLine);
					SetLotAndLoc(newLine, newLine: true);
					newLine = Base.transactions.Update(newLine);
					foreach (POReceiptLineSplit split in Base.splits.Select())
					{
						split.ReceivedQty = split.Qty;
						Received.Update(split);
					}
				}

				if (CanReceive)
				{
					Report(Msg.InventoryAdded, InventoryItem.PK.Find(Base, header.InventoryID.Value).InventoryCD, header.Qty, header.UOM);

					if (!IsSingleReceivingLocation && ViseVersaFlow && isLocationRequired)
						SetScanState(ScanStates.Location);
					else
						SetScanState(ScanStates.Item);
				}
				else
					SetScanState(ScanStates.Command, Msg.ReceiptReceived, header.RefNbr);

				return WMSFlowStatus.Ok;
			}

			return ExecuteAndCompleteFlow(Implementation);
		}
		#endregion

		#region PutAway line confirmation
		private void ConfirmPutAway()
		{
			bool isSerialItem = HeaderView.Current.LotSerTrack == INLotSerTrack.SerialNumbered;

			if (IsCartRequired(HeaderView.Current) == false)
				ProcessPutAwayNoCart();
			else if (HeaderView.Current.CartLoaded == false)
				ProcessPutAwayInCart();
			else
				ProcessPutAwayOutCart();

			if (!isSerialItem && Info.Current.MessageType == WMSMessageTypes.Information)
				HeaderView.Current.IsQtyOverridable = true;
		}

		private WMSFlowStatus ProcessPutAwayNoCart()
		{
			WMSFlowStatus Implementation()
			{
				Header header = HeaderView.Current;
				bool remove = header.Remove == true;

				var receivedSplits =
					Received.SelectMain().Where(
						r => r.InventoryID == header.InventoryID
							&& r.SubItemID == header.SubItemID
							&& r.LotSerialNbr == (header.LotSerialNbr ?? r.LotSerialNbr)
							&& (remove ? r.PutAwayQty > 0 : r.PutAwayQty < r.Qty));
				if (receivedSplits.Any() == false)
					return WMSFlowStatus.Fail(Msg.NothingToPutAway).ClearIsNeeded;

				decimal qty = Sign.MinusIf(remove) * header.GetBaseQty(Base);

				if (!remove && receivedSplits.Sum(s => s.Qty - s.PutAwayQty) < qty)
					return WMSFlowStatus.Fail(Msg.Overputting);
				if (remove && receivedSplits.Sum(s => s.PutAwayQty) + qty < 0)
					return WMSFlowStatus.Fail(Msg.Underputting);

				decimal unassignedQty = qty;
				foreach (var receivedSplit in remove ? receivedSplits.Reverse() : receivedSplits)
				{
					decimal currentQty = remove
						? -Math.Min(receivedSplit.PutAwayQty.Value, -unassignedQty)
						: Math.Min(receivedSplit.Qty.Value - receivedSplit.PutAwayQty.Value, unassignedQty);

					WMSFlowStatus transferStatus = SyncWithTransfer(header, receivedSplit, currentQty);
					if (transferStatus.IsError != false)
						return transferStatus;

					receivedSplit.PutAwayQty += currentQty;
					PutAway.Update(receivedSplit);

					unassignedQty -= currentQty;
					if (unassignedQty == 0)
						break;
				}

				if (CanPutAway)
				{
					Report(remove ? Msg.InventoryRemoved : Msg.InventoryAdded, InventoryItem.PK.Find(Base, header.InventoryID.Value).InventoryCD, header.Qty, header.UOM);

					if (IsSingleReceivingLocation)
						SetScanState(ScanStates.Item);
					else
						SetScanState(ScanStates.Location);
				}
				else
					SetScanState(ScanStates.Command, Msg.ReceiptPutAway, header.RefNbr);

				return WMSFlowStatus.Ok;
			}

			using (var ts = new PXTransactionScope())
			{
				var res = ExecuteAndCompleteFlow(Implementation);
				if (res.IsError == false)
					ts.Complete();
				return res;
			}
		}

		private WMSFlowStatus ProcessPutAwayInCart()
		{
			WMSFlowStatus Implementation()
			{
				Header header = HeaderView.Current;
				bool remove = header.Remove == true;

				if (IsSingleReceivingLocation)
					header.LocationID = PutAway.SelectMain().FirstOrDefault()?.LocationID;

				var receivedSplits =
					PutAway.SelectMain().Where(
						r => r.LocationID == (header.LocationID ?? r.LocationID)
							&& r.InventoryID == header.InventoryID
							&& r.SubItemID == header.SubItemID
							&& r.LotSerialNbr == (header.LotSerialNbr ?? r.LotSerialNbr));
				if (receivedSplits.Any() == false)
					return WMSFlowStatus.Fail(Msg.NothingToPutAway).ClearIsNeeded;

				decimal qty = Sign.MinusIf(remove) * header.GetBaseQty(Base);

				if (qty != 0)
				{
					if (!remove && receivedSplits.Sum(s => s.Qty - s.PutAwayQty) < qty)
						return WMSFlowStatus.Fail(Msg.Overputting);
					if (remove && receivedSplits.Sum(s => GetCartQty(s)) + qty < 0)
						return WMSFlowStatus.Fail(Msg.CartUnderpicking);

					try
					{
						decimal unassignedQty = qty;
						foreach (var receivedSplit in remove ? receivedSplits.Reverse() : receivedSplits)
						{
							decimal currentQty = remove
								? -Math.Min(GetCartQty(receivedSplit), -unassignedQty)
								: Math.Min(receivedSplit.Qty.Value - receivedSplit.PutAwayQty.Value, unassignedQty);

							if (currentQty == 0)
								continue;

							WMSFlowStatus cartStatus = SyncWithCart(header, receivedSplit, currentQty);
							if (cartStatus.IsError != false)
								return cartStatus;

							receivedSplit.PutAwayQty += currentQty;
							PutAway.Update(receivedSplit);

							unassignedQty -= currentQty;
							if (unassignedQty == 0)
								break;
						}
					}
					finally
					{
						EnsureCartReceiptLink();
					}
				}

				if (CanPutAway)
					SetScanState(IsSingleReceivingLocation ? ScanStates.Item : ScanStates.Location, remove ? Msg.InventoryRemoved : Msg.InventoryAdded, InventoryItem.PK.Find(Base, header.InventoryID.Value).InventoryCD, header.Qty, header.UOM);
				else
					SetScanState(ScanStates.Command, Msg.ReceiptPutAway, header.RefNbr);

				return WMSFlowStatus.Ok;
			}

			return ExecuteAndCompleteFlow(Implementation);
		}

		private WMSFlowStatus ProcessPutAwayOutCart()
		{
			WMSFlowStatus Implementation()
			{
				Header header = HeaderView.Current;
				bool remove = header.Remove == true;

				if (header.ToLocationID == null)
					return WMSFlowStatus.Fail(Msg.ToLocationNotSet).ClearIsNeeded;

				var receivedSplits =
					PutAway.SelectMain().Where(
						r => r.InventoryID == header.InventoryID
							&& r.SubItemID == header.SubItemID
							&& r.LotSerialNbr == (header.LotSerialNbr ?? r.LotSerialNbr)
							&& (remove ? r.PutAwayQty > 0 : GetCartQty(r) > 0));
				if (receivedSplits.Any() == false)
					return WMSFlowStatus.Fail(Msg.NothingToPutAway).ClearIsNeeded;

				decimal qty = Sign.MinusIf(remove) * header.GetBaseQty(Base);

				if (qty != 0)
				{
					if (remove && receivedSplits.Sum(s => s.PutAwayQty) + qty < 0)
						return WMSFlowStatus.Fail(Msg.Underputting);
					if (!remove && receivedSplits.Sum(s => GetCartQty(s)) - qty < 0)
						return WMSFlowStatus.Fail(Msg.CartUnderpicking);

					try
					{
						decimal unassignedQty = qty;
						foreach (var receivedSplit in remove ? receivedSplits.Reverse() : receivedSplits)
						{
							decimal currentQty = remove
								? -Math.Min(receivedSplit.PutAwayQty.Value, -unassignedQty)
								: Math.Min(GetCartQty(receivedSplit), unassignedQty);

							if (currentQty == 0)
								continue;

							WMSFlowStatus cartStatus = SyncWithCart(header, receivedSplit, -currentQty);
							if (cartStatus.IsError != false)
								return cartStatus;

							WMSFlowStatus transferStatus = SyncWithTransfer(header, receivedSplit, currentQty);
							if (transferStatus.IsError != false)
								return transferStatus;

							unassignedQty -= currentQty;
							if (unassignedQty == 0)
								break;
						}
					}
					finally
					{
						EnsureCartReceiptLink();
					}
				}

				if (CanPutAway)
					SetScanState(IsSingleReceivingLocation ? ScanStates.Item : ScanStates.Location, remove ? Msg.InventoryRemoved : Msg.InventoryAdded, InventoryItem.PK.Find(Base, header.InventoryID.Value).InventoryCD, header.Qty, header.UOM);
				else
					SetScanState(ScanStates.Command, Msg.ReceiptPutAway, header.RefNbr);

				return WMSFlowStatus.Ok;
			}


			using (var ts = new PXTransactionScope())
			{
				var res = ExecuteAndCompleteFlow(Implementation);
				if (res.IsError == false)
					ts.Complete();
				return res;
			}
		}

		#region Transfer sync
		private Lazy<INTransferEntry> _transferEntry;
		private INTransferEntry transferEntry => _transferEntry.Value;
		private INTransferEntry CreateTransferEntry()
		{
			var ie = PXGraph.CreateInstance<INTransferEntry>();
			Base.Caches[typeof(SiteStatus)] = ie.Caches[typeof(SiteStatus)];
			Base.Caches[typeof(LocationStatus)] = ie.Caches[typeof(LocationStatus)];
			Base.Caches[typeof(LotSerialStatus)] = ie.Caches[typeof(LotSerialStatus)];
			Base.Caches[typeof(SiteLotSerial)] = ie.Caches[typeof(SiteLotSerial)];
			Base.Caches[typeof(ItemLotSerial)] = ie.Caches[typeof(ItemLotSerial)];

			Base.Views.Caches.Remove(typeof(SiteStatus));
			Base.Views.Caches.Remove(typeof(LocationStatus));
			Base.Views.Caches.Remove(typeof(LotSerialStatus));
			Base.Views.Caches.Remove(typeof(SiteLotSerial));
			Base.Views.Caches.Remove(typeof(ItemLotSerial));
			if (HeaderView.Current?.TransferRefNbr != null)
			{
				ie.transfer.Current = Transfer;
				EnsureCartReceiptLink();
			}

			return ie;
		}

		private WMSFlowStatus SyncWithTransfer(Header header, POReceiptLineSplit receivedSplit, decimal qty)
		{
			bool isNewDocument = false;
			if (transferEntry.transfer.Current == null)
			{
				var doc = (INRegister)transferEntry.transfer.Cache.CreateInstance();
				doc.SiteID = header.SiteID;
				doc.ToSiteID = header.SiteID;
				doc.POReceiptType = Receipt.ReceiptType;
				doc.POReceiptNbr = Receipt.ReceiptNbr;
				doc.OrigModule = GL.BatchModule.PO;
				doc = transferEntry.transfer.Insert(doc);
				isNewDocument = true;
			}

			INTranSplit[] linkedSplits = isNewDocument
				? Array.Empty<INTranSplit>()
				: SelectFrom<POReceiptSplitToTransferSplitLink>
				.InnerJoin<INTranSplit>.On<POReceiptSplitToTransferSplitLink.FK.INTranSplit>
				.Where<POReceiptSplitToTransferSplitLink.FK.POReceiptLineSplit.SameAsCurrent
					.And<POReceiptSplitToTransferSplitLink.transferRefNbr.IsEqual<@P.AsString>>
					.And<INTranSplit.toLocationID.IsEqual<@P.AsInt>>>
				.View.ReadOnly.SelectMultiBound(Base, new object[] { receivedSplit }, transferEntry.transfer.Current.RefNbr, header.ToLocationID)
				.RowCast<INTranSplit>()
				.ToArray();

			INTranSplit[] appropriateSplits = isNewDocument
				? Array.Empty<INTranSplit>()
				: SelectFrom<INTranSplit>
				.Where<INTranSplit.refNbr.IsEqual<@P.AsString>
					.And<INTranSplit.inventoryID.IsEqual<POReceiptLineSplit.inventoryID.FromCurrent>>
					.And<INTranSplit.subItemID.IsEqual<POReceiptLineSplit.subItemID.FromCurrent>>
					.And<INTranSplit.siteID.IsEqual<POReceiptLineSplit.siteID.FromCurrent>>
					.And<INTranSplit.locationID.IsEqual<POReceiptLineSplit.locationID.FromCurrent>>
					.And<INTranSplit.lotSerialNbr.IsEqual<POReceiptLineSplit.lotSerialNbr.FromCurrent>>
					.And<INTranSplit.toLocationID.IsEqual<@P.AsInt>>>
				.View.ReadOnly.SelectMultiBound(Base, new object[] { receivedSplit }, transferEntry.transfer.Current.RefNbr, header.ToLocationID)
				.RowCast<INTranSplit>()
				.ToArray();

			INTranSplit[] existingINSplits = isNewDocument
				? Array.Empty<INTranSplit>()
				: linkedSplits.Concat(appropriateSplits).ToArray();

			bool isNewSplit = false;
			INTran tran;
			INTranSplit tranSplit;
			if (existingINSplits.Length == 0)
			{
				tran = transferEntry.transactions.With(_ => _.Insert() ?? _.Insert());
				tran.InventoryID = receivedSplit.InventoryID;
				tran.SubItemID = receivedSplit.SubItemID;
				tran.LotSerialNbr = receivedSplit.LotSerialNbr;
				tran.ExpireDate = receivedSplit.ExpireDate;
				tran.UOM = receivedSplit.UOM;
				tran.SiteID = receivedSplit.SiteID;
				tran.LocationID = receivedSplit.LocationID;
				tran.ToSiteID = header.SiteID;
				tran.ToLocationID = header.ToLocationID;
				tran.POReceiptType = Receipt.ReceiptType;
				tran.POReceiptNbr = Receipt.ReceiptNbr;
				tran.POReceiptLineNbr = receivedSplit.LineNbr;
				tran = transferEntry.transactions.Update(tran);
				tranSplit = transferEntry.splits.Search<INTranSplit.lineNbr>(tran.LineNbr);
				if (tranSplit == null)
				{
					tranSplit = transferEntry.splits.With(_ => _.Insert() ?? _.Insert());
					tranSplit.LotSerialNbr = header.LotSerialNbr;
					tranSplit.ExpireDate = header.ExpireDate;
					tranSplit.ToSiteID = header.SiteID;
					tranSplit.ToLocationID = header.ToLocationID;
					tranSplit = transferEntry.splits.Update(tranSplit);
				}
				isNewSplit = true;
			}
			else
			{
				tranSplit = existingINSplits.FirstOrDefault(s => s.LotSerialNbr == (header.LotSerialNbr ?? s.LotSerialNbr));
				if (tranSplit != null)
				{
					tran = transferEntry.transactions.Current = transferEntry.transactions.Search<INTran.lineNbr>(tranSplit.LineNbr);
					tranSplit = transferEntry.splits.Search<INTranSplit.splitLineNbr>(tranSplit.SplitLineNbr);
				}
				else
				{
					tran = transferEntry.transactions.Current = transferEntry.transactions.Search<INTran.lineNbr>(existingINSplits.First().LineNbr);
					tranSplit = transferEntry.splits.With(_ => _.Insert() ?? _.Insert());
					tranSplit.LotSerialNbr = header.LotSerialNbr;
					tranSplit.ExpireDate = header.ExpireDate;
					tranSplit.ToSiteID = header.SiteID;
					tranSplit.ToLocationID = header.ToLocationID;
					tranSplit = transferEntry.splits.Update(tranSplit);
					isNewSplit = true;
				}
			}

			tranSplit.Qty += qty;
			tranSplit = transferEntry.splits.Update(tranSplit);
			if (tranSplit.Qty == 0)
				transferEntry.splits.Delete(tranSplit);

			tran = transferEntry.transactions.Search<INTran.lineNbr>(tran.LineNbr);

			if (qty < 0)
			{
				if (tran.Qty + qty == 0)
					transferEntry.transactions.Delete(tran);
				else
				{
					// qty deduction is not synchronized with splits - we don't want unassigned qty to show up
					tran.Qty += INUnitAttribute.ConvertFromBase(transferEntry.transactions.Cache, tran.InventoryID, tran.UOM, qty, INPrecision.NOROUND);
					tran = transferEntry.transactions.Update(tran);
				}
			}

			transferEntry.Save.Press();

			if (isNewDocument)
				HeaderView.Current.TransferRefNbr = transferEntry.transfer.Current.RefNbr;

			if (isNewSplit)
				tranSplit = transferEntry.splits.Search<INTranSplit.lineNbr, INTranSplit.splitLineNbr>(tranSplit.LineNbr, tranSplit.SplitLineNbr);

			return EnsureReceiptTransferSplitLink(receivedSplit, tranSplit, qty);
		}

		private WMSFlowStatus EnsureReceiptTransferSplitLink(POReceiptLineSplit poSplit, INTranSplit inSplit, decimal deltaQty)
		{
			var allLinks =
				SelectFrom<POReceiptSplitToTransferSplitLink>
				.Where<POReceiptSplitToTransferSplitLink.FK.INTranSplit.SameAsCurrent
					.Or<POReceiptSplitToTransferSplitLink.FK.POReceiptLineSplit.SameAsCurrent>>
				.View.SelectMultiBound(Base, new object[] { inSplit, poSplit })
				.RowCast<POReceiptSplitToTransferSplitLink>()
				.ToArray();

			POReceiptSplitToTransferSplitLink currentLink = allLinks.FirstOrDefault(
				link => POReceiptSplitToTransferSplitLink.FK.INTranSplit.Match(Base, inSplit, link)
					&& POReceiptSplitToTransferSplitLink.FK.POReceiptLineSplit.Match(Base, poSplit, link));

			decimal transferQty = allLinks.Where(link => POReceiptSplitToTransferSplitLink.FK.INTranSplit.Match(Base, inSplit, link)).Sum(link => link.Qty ?? 0);
			decimal receiptQty = allLinks.Where(link => POReceiptSplitToTransferSplitLink.FK.POReceiptLineSplit.Match(Base, poSplit, link)).Sum(link => link.Qty ?? 0);

			if (transferQty + deltaQty > inSplit.Qty)
			{
				return WMSFlowStatus.Fail(Msg.LinkTransferOverpicking);
			}
			if (receiptQty + deltaQty > poSplit.Qty)
			{
				return WMSFlowStatus.Fail(Msg.LinkReceiptOverpicking);
			}
			if (currentLink == null ? deltaQty < 0 : currentLink.Qty + deltaQty < 0)
			{
				return WMSFlowStatus.Fail(Msg.LinkUnderpicking);
			}

			if (currentLink == null)
			{
				currentLink = TransferSplitLinks.Insert(new POReceiptSplitToTransferSplitLink
				{
					ReceiptNbr = poSplit.ReceiptNbr,
					ReceiptLineNbr = poSplit.LineNbr,
					ReceiptSplitLineNbr = poSplit.SplitLineNbr,
					TransferRefNbr = inSplit.RefNbr,
					TransferLineNbr = inSplit.LineNbr,
					TransferSplitLineNbr = inSplit.SplitLineNbr,
					Qty = deltaQty
				});
			}
			else
			{
				currentLink.Qty += deltaQty;
				currentLink = TransferSplitLinks.Update(currentLink);
			}

			if (currentLink.Qty == 0)
				TransferSplitLinks.Delete(currentLink);

			return WMSFlowStatus.Ok;
		}
		#endregion

		#region Cart sync
		private WMSFlowStatus SyncWithCart(Header header, POReceiptLineSplit receivedSplit, decimal qty)
		{
			INCartSplit[] linkedSplits =
				SelectFrom<POReceiptSplitToCartSplitLink>
				.InnerJoin<INCartSplit>.On<POReceiptSplitToCartSplitLink.FK.CartSplit>
				.Where<POReceiptSplitToCartSplitLink.FK.POReceiptLineSplit.SameAsCurrent
					.And<POReceiptSplitToCartSplitLink.siteID.IsEqual<@P.AsInt>>
					.And<POReceiptSplitToCartSplitLink.cartID.IsEqual<@P.AsInt>>>
				.View.SelectMultiBound(Base, new object[] { receivedSplit }, header.SiteID, header.CartID)
				.RowCast<INCartSplit>()
				.ToArray();

			INCartSplit[] appropriateSplits =
				SelectFrom<INCartSplit>
				.Where<INCartSplit.cartID.IsEqual<@P.AsInt>
					.And<INCartSplit.inventoryID.IsEqual<POReceiptLineSplit.inventoryID.FromCurrent>>
					.And<INCartSplit.subItemID.IsEqual<POReceiptLineSplit.subItemID.FromCurrent>>
					.And<INCartSplit.siteID.IsEqual<POReceiptLineSplit.siteID.FromCurrent>>
					.And<INCartSplit.fromLocationID.IsEqual<POReceiptLineSplit.locationID.FromCurrent>>
					.And<INCartSplit.lotSerialNbr.IsEqual<POReceiptLineSplit.lotSerialNbr.FromCurrent>>>
				.View.SelectMultiBound(Base, new object[] { receivedSplit }, header.CartID)
				.RowCast<INCartSplit>()
				.ToArray();

			INCartSplit[] existingINSplits = linkedSplits.Concat(appropriateSplits).ToArray();

			INCartSplit cartSplit = existingINSplits.FirstOrDefault(s => s.LotSerialNbr == (header.LotSerialNbr ?? s.LotSerialNbr));
			if (cartSplit == null)
			{
				cartSplit = CartSplits.Insert(new INCartSplit
				{
					CartID = header.CartID,
					InventoryID = receivedSplit.InventoryID,
					SubItemID = receivedSplit.SubItemID,
					LotSerialNbr = receivedSplit.LotSerialNbr,
					ExpireDate = receivedSplit.ExpireDate,
					UOM = receivedSplit.UOM,
					SiteID = receivedSplit.SiteID,
					FromLocationID = receivedSplit.LocationID,
					Qty = qty
				});
			}
			else
			{
				cartSplit.Qty += qty;
				cartSplit = CartSplits.Update(cartSplit);
			}

			if (cartSplit.Qty == 0)
			{
				CartSplits.Delete(cartSplit);
				return WMSFlowStatus.Ok;
			}
			else
				return EnsureReceiptCartSplitLink(receivedSplit, cartSplit, qty);
		}

		private WMSFlowStatus EnsureReceiptCartSplitLink(POReceiptLineSplit poSplit, INCartSplit cartSplit, decimal deltaQty)
		{
			var allLinks =
				SelectFrom<POReceiptSplitToCartSplitLink>
				.Where<POReceiptSplitToCartSplitLink.FK.CartSplit.SameAsCurrent
					.Or<POReceiptSplitToCartSplitLink.FK.POReceiptLineSplit.SameAsCurrent>>
				.View.SelectMultiBound(Base, new object[] { cartSplit, poSplit })
				.RowCast<POReceiptSplitToCartSplitLink>()
				.ToArray();

			POReceiptSplitToCartSplitLink currentLink = allLinks.FirstOrDefault(
				link => POReceiptSplitToCartSplitLink.FK.CartSplit.Match(Base, cartSplit, link)
					&& POReceiptSplitToCartSplitLink.FK.POReceiptLineSplit.Match(Base, poSplit, link));

			decimal cartQty = allLinks.Where(link => POReceiptSplitToCartSplitLink.FK.CartSplit.Match(Base, cartSplit, link)).Sum(link => link.Qty ?? 0);

			if (cartQty + deltaQty > cartSplit.Qty)
			{
				return WMSFlowStatus.Fail(Msg.LinkCartOverpicking);
			}
			if (currentLink == null ? deltaQty < 0 : currentLink.Qty + deltaQty < 0)
			{
				return WMSFlowStatus.Fail(Msg.LinkUnderpicking);
			}

			if (currentLink == null)
			{
				currentLink = CartSplitLinks.Insert(new POReceiptSplitToCartSplitLink
				{
					ReceiptNbr = poSplit.ReceiptNbr,
					ReceiptLineNbr = poSplit.LineNbr,
					ReceiptSplitLineNbr = poSplit.SplitLineNbr,
					SiteID = cartSplit.SiteID,
					CartID = cartSplit.CartID,
					CartSplitLineNbr = cartSplit.SplitLineNbr,
					Qty = deltaQty
				});
			}
			else
			{
				currentLink.Qty += deltaQty;
				currentLink = CartSplitLinks.Update(currentLink);
			}

			if (currentLink.Qty == 0)
				CartSplitLinks.Delete(currentLink);

			return WMSFlowStatus.Ok;
		}

		private void EnsureCartReceiptLink()
		{
			if (HeaderView.Current.CartID != null && HeaderView.Current.SiteID != null && HeaderView.Current.RefNbr != null)
			{
				var link = new POCartReceipt
				{
					SiteID = HeaderView.Current.SiteID,
					CartID = HeaderView.Current.CartID,
					ReceiptNbr = HeaderView.Current.RefNbr,
					TransferNbr = HeaderView.Current.TransferRefNbr,
				};

				if (CartSplits.SelectMain().Any() || Transfer?.Released != true)
					CartsLinks.Update(link); // also insert
				else
					CartsLinks.Delete(link);
			}
		}
		#endregion
		#endregion

		#region Clearing logic
		protected override void ClearMode()
		{
			ClearHeaderInfo(true);
			Report(Msg.ScreenCleared);

			if (HeaderView.Current.Mode == Modes.Receive)
			{
				if (HeaderView.Current.RefNbr == null)
					SetScanState(ScanStates.RefNbr);
				else if (CanReceive)
					SetScanState(IsLocationRequired(HeaderView.Current) && ViseVersaFlow ? ScanStates.Location : ScanStates.Item);
				else
					SetScanState(ScanStates.Command);
			}
			else if (HeaderView.Current.Mode == Modes.PutAway)
			{
				if (IsCartRequired(HeaderView.Current) && HeaderView.Current.CartID == null)
					SetScanState(ScanStates.Cart);
				else if (HeaderView.Current.RefNbr == null)
					SetScanState(ScanStates.RefNbr);
				else if (CanPutAway)
					SetScanState(IsSingleReceivingLocation ? ScanStates.Item : ScanStates.Location);
				else
					SetScanState(ScanStates.Command);
			}
		}

		protected override void ClearHeaderInfo(bool redirect = false)
		{
			base.ClearHeaderInfo(redirect);

			if (redirect || IsSingleReceivingLocation == false)
				HeaderView.Current.LocationID = null;
			HeaderView.Current.ToLocationID = null;
			HeaderView.Current.LotSerialNbr = null;
			HeaderView.Current.ExpireDate = null;
			HeaderView.Current.LotSerTrack = null;
			HeaderView.Current.LotSerAssign = null;
			HeaderView.Current.LotSerTrackExpiration = null;
			HeaderView.Current.AutoNextNbr = false;
			HeaderView.Current.ForceInsertLine = false;
			HeaderView.Current.TransferRefNbr = null;
			if (redirect)
			{
				HeaderView.Current.CartLoaded = false;
				HeaderView.Current.PONbr = null;
			}
		}
		#endregion

		private void ReleaseReceipt(bool completePOLines)
		{
			PXSelectBase<POReceiptLine> lines = Base.transactions;
			PXSelectBase<POReceiptLineSplit> splits = Base.splits;

			bool anyChanges = false;

			foreach (POReceiptLine line in lines.Select())
			{
				lines.Current = line;

				decimal lineQty = 0;
				foreach (POReceiptLineSplit split in splits.Select())
				{
					splits.Current = split;

					anyChanges |= splits.Current.Qty != splits.Current.ReceivedQty;

					splits.Current.Qty = splits.Current.ReceivedQty;
					splits.UpdateCurrent();

					if (splits.Current.ReceivedQty == 0)
					{
						splits.DeleteCurrent();
						anyChanges = true;
					}
					else
						lineQty += splits.Current.ReceivedQty ?? 0;
				}

				lines.Current.Qty = INUnitAttribute.ConvertFromBase(lines.Cache, lines.Current.InventoryID, lines.Current.UOM, lineQty, INPrecision.NOROUND);
				lines.UpdateCurrent();
				if (completePOLines && lines.Current.Qty > 0)
					lines.Cache.SetValueExt<POReceiptLine.allowComplete>(line, true);

				if (lines.Current.Qty == 0)
				{
					lines.DeleteCurrent();
					anyChanges = true;
				}
			}

			if (Base.Document.Current.Hold == true)
			{
				Base.Document.Current.Hold = false;
				Base.Document.UpdateCurrent();
			}

			ClearHeaderInfo();
			Save.Press();

			string printLabelsReportID = UserSetup.For(Base).InventoryLabelsReportID;
			bool printLabels = UserSetup.For(Base).PrintInventoryLabelsAutomatically == true;
			bool printReceipt = UserSetup.For(Base).PrintPurchaseReceiptAutomatically == true;
			POReceipt receipt = Receipt;

			WaitFor(delegate
			{
				POReleaseReceipt.ReleaseDoc(new List<POReceipt>() { receipt }, false);

				if (PXAccess.FeatureInstalled<FeaturesSet.deviceHub>())
				{
					INReceiptEntry inGraph = null;

					if (printLabels)
					{
						inGraph = PXGraph.CreateInstance<INReceiptEntry>();

						string refNbr = POReceipt.PK.Find(inGraph, receipt.ReceiptNbr)?.InventoryRefNbr;
						if (refNbr != null && !string.IsNullOrEmpty(printLabelsReportID))
						{
							var reportParameters = new Dictionary<string, string>()
							{
								[nameof(INRegister.RefNbr)] = refNbr
							};

							PrintReportViaDeviceHub<CR.BAccount>(inGraph, printLabelsReportID, reportParameters, INNotificationSource.None, null);
						}
					}

					if (printReceipt && anyChanges)
					{
						inGraph = inGraph ?? PXGraph.CreateInstance<INReceiptEntry>();

						var reportParameters = new Dictionary<string, string>()
						{
							[nameof(POReceipt.ReceiptType)] = receipt.ReceiptType,
							[nameof(POReceipt.ReceiptNbr)] = receipt.ReceiptNbr
						};

						PrintReportViaDeviceHub(inGraph, "PO646000", reportParameters, PONotificationSource.Vendor, Vendor.PK.Find(inGraph, receipt.VendorID));
					}
				}
			}, Msg.ReceiptReleasing, HeaderView.Current.RefNbr);
		}

		private void ReleaseTransfer()
		{
			if (transferEntry.transfer.Current?.Released == false)
			{
				Tuple<string, string> transferNbr = Tuple.Create(transferEntry.transfer.Current.DocType, transferEntry.transfer.Current.RefNbr);
				WaitFor(delegate
				{
					INTransferEntry te = PXGraph.CreateInstance<INTransferEntry>();
					te.transfer.Current =
						SelectFrom<INRegister>
						.Where<INRegister.docType.IsEqual<@P.AsString>
							.And<INRegister.refNbr.IsEqual<@P.AsString>>>
						.View.Select(te, transferNbr.Item1, transferNbr.Item2);
					te.transfer.Cache.SetValueExt<INRegister.hold>(te.transfer.Current, false);
					te.transfer.UpdateCurrent();
					te.release.Press();
				}, Msg.TransferReleasing, HeaderView.Current.TransferRefNbr);
			}
			else
			{
				ReportError(Msg.TransferNothingToRelease);
			}
		}

		private POReceipt CreateEmptyReceiptFrom(POOrder order)
		{
			POReceipt receipt = new POReceipt
			{
				ReceiptType = POReceiptType.POReceipt,
				BranchID = order.BranchID,
				VendorID = order.VendorID,
				VendorLocationID = order.VendorLocationID,
				ProjectID = order.ProjectID,
				OrigPONbr = order.OrderNbr
		};
			receipt = Base.Document.Insert(receipt);
			return receipt;
		}

		protected virtual bool IsSingleReceivingLocation => HeaderView.Current.Mode == Modes.Receive
			? UserSetup.For(Base).SingleLocation == true
			: PutAway.SelectMain().GroupBy(s => s.LocationID).Count() < 2;

		protected override bool IsLocationRequired(Header header) => base.IsLocationRequired(header) && (header.Mode == Modes.Receive).Implies(header.DefaultLocationID == null);
		protected override bool IsCartRequired(Header header) => base.IsCartRequired(header) && Setup.Current.UseCartsForPutAway == true && header.Mode == Modes.PutAway;
		private decimal GetCartQty(POReceiptLineSplit posplit) => CartSplitLinks.SelectMain().Where(link => POReceiptSplitToCartSplitLink.FK.POReceiptLineSplit.Match(Base, posplit, link)).Sum(_ => _.Qty ?? 0);
		private decimal GetOverallCartQty(POReceiptLineSplit posplit) => AllCartSplitLinks.SelectMain().Where(link => POReceiptSplitToCartSplitLink.FK.POReceiptLineSplit.Match(Base, posplit, link)).Sum(_ => _.Qty ?? 0);

		private decimal GetOverallReceivedQty(POReceiptLineSplit split) => GetSplitQuantities(split).overallReceivedQty;
		private decimal GetNormalReceivedQty(POReceiptLineSplit split) => GetSplitQuantities(split).normalReceiptQty;
		private decimal GetNormalRestQty(POReceiptLineSplit split) => GetSplitQuantities(split).normalRestQty;
		private decimal GetExtendedReceivedQty(POReceiptLineSplit split) => GetSplitQuantities(split).extendedReceiptQty;
		private decimal GetExtendedRestQty(POReceiptLineSplit split) => GetSplitQuantities(split).extendedRestQty;

		private (decimal overallReceivedQty, decimal normalReceiptQty, decimal normalRestQty, decimal extendedReceiptQty, decimal extendedRestQty) GetSplitQuantities(POReceiptLineSplit split)
		{
			var row = (PXResult<POReceiptLine, POLine, InventoryItem, INLotSerClass>)
				SelectFrom<POReceiptLine>
				.LeftJoin<POLine>.On<POReceiptLine.FK.OrderLine>
				.LeftJoin<InventoryItem>.On<POReceiptLine.FK.InventoryItem>
				.LeftJoin<INLotSerClass>.On<InventoryItem.FK.LotSerClass>
				.Where<POReceiptLine.receiptType.IsEqual<@P.AsString>
					.And<POReceiptLine.receiptNbr.IsEqual<@P.AsString>>
					.And<POReceiptLine.lineNbr.IsEqual<@P.AsInt>>>
				.View.Select(Base, split.ReceiptType, split.ReceiptNbr, split.LineNbr);
			POReceiptLine line = row;
			POLine poLine = row;
			INLotSerClass lotSerClass = row;
			bool isDirectReceiptLine = poLine == null || poLine.OrderNbr == null;

			decimal overallReceivedQty = isDirectReceiptLine
				? SelectFrom<POReceiptLineSplit>
					.Where<POReceiptLineSplit.FK.ReceiptLine.SameAsCurrent>
					.View.SelectMultiBound(Base, new[] { line })
					.RowCast<POReceiptLineSplit>()
					.AsEnumerable()
					.Sum(s => s.BaseReceivedQty.Value)
				: SelectFrom<POReceiptLineSplit>
					.InnerJoin<POReceiptLine>.On<POReceiptLineSplit.FK.ReceiptLine>
					.Where<POReceiptLine.FK.OrderLine.SameAsCurrent>
					.View.SelectMultiBound(Base, new[] { poLine })
					.RowCast<POReceiptLineSplit>()
					.AsEnumerable()
					.Sum(s => s.BaseReceivedQty.Value);

			if (lotSerClass != null && lotSerClass.LotSerTrack == INLotSerTrack.SerialNumbered)
				return (overallReceivedQty, 1, 1 - split.ReceivedQty.Value, 1, 1 - split.ReceivedQty.Value);

			decimal normalReceiptQty = isDirectReceiptLine
				? line.BaseQty.Value
				: poLine.BaseOrderQty.Value;
			decimal restQty = PXDBQuantityAttribute.Round(Math.Max(0, normalReceiptQty - overallReceivedQty));

			decimal extendedReceiptQty = isDirectReceiptLine
				? 0
				: poLine.BaseOrderQty.Value * (poLine.RcptQtyMax.Value - 100) / 100m;
			decimal extendedRestQty = PXDBQuantityAttribute.Round(extendedReceiptQty + (normalReceiptQty - overallReceivedQty));

			return (overallReceivedQty, normalReceiptQty, restQty, extendedReceiptQty, extendedRestQty);
		}

		private DateTime? EnsureExpireDateDefault() => LSSelect.ExpireDateByLot(Base, HeaderView.Current, null);

		private bool DefaultExpireDate => UserSetup.For(Base).DefaultExpireDate == true;
		private bool DefaultLotSerial => UserSetup.For(Base).DefaultLotSerialNumber == true;

		private bool CanReceive => Received.SelectMain().Any(s => s.ReceivedQty < s.Qty || GetExtendedRestQty(s) > 0);
		private bool CanPutAway => IsCartRequired(HeaderView.Current) == false || HeaderView.Current.CartLoaded == false
			? PutAway.SelectMain().Any(s => s.PutAwayQty != s.Qty)
			: PutAway.SelectMain().Any(s => GetCartQty(s) > 0);

		private bool ViseVersaFlow => HeaderView.Current.Mode == Modes.Receive && (IsSingleReceivingLocation || HeaderView.Current.ReceiptType == POReceiptType.POReturn);

		protected override bool UseQtyCorrectection => Setup.Current.UseDefaultQty != true;
		protected override bool ExplicitLineConfirmation => Setup.Current.ExplicitLineConfirmation == true;
		protected virtual int LargeOrderLinesCount => 200;

		#region Constants & Messages
		public new abstract class Modes : WMSBase.Modes
		{
			public static WMSModeOf<ReceivePutAway, ReceivePutAwayHost> Receive { get; } = WMSMode("RCPT");
			public static WMSModeOf<ReceivePutAway, ReceivePutAwayHost> PutAway { get; } = WMSMode("PTAW");
			public class receive : BqlString.Constant<receive> { public receive() : base(Receive) { } }
			public class putAway : BqlString.Constant<putAway> { public putAway() : base(PutAway) { } }
		}

		public new abstract class ScanStates : WMSBase.ScanStates
		{
			public const string ReceiptConfirmation = "RCPT";
			public const string PutAwayConfirmation = "PTAW";
			public const string ToLocation = "TLOC";
		}

		public new abstract class ScanCommands : WMSBase.ScanCommands
		{
			public const string CartIn = Marker + "CART*IN";
			public const string CartOut = Marker + "CART*OUT";

			public const string ReleaseReceipt = Marker + "RELEASE*RECEIPT";
			public const string ReleaseTransfer = Marker + "RELEASE*TRANSFER";
			public const string CompletePOLines = Marker + "COMPLETE*POLINES";
		}

		[PXLocalizable]
		public new abstract class Msg : WMSBase.Msg
		{
			public const string ReceiveMode = "RECEIVE";
			public const string PutAwayMode = "PUT AWAY";

			public const string ReceiptPrompt = "Scan the receipt number.";
			public const string ReceiptReady = "Receipt {0} is loaded and ready to be processed.";
			public const string ReceiptReadyNew = "New receipt has been created and ready for processing.";
			public const string ReceiptMissing = "Receipt {0} is not found.";
			public const string ReceiptInvalid = "Receipt {0} has status invalid for processing.";
			public const string ReceiptMultiSites = "Receipt {0} should have a single warehouse to be processed.";
			public const string ReceiptReceived = "Receipt {0} is received.";
			public const string ReceiptPutAway = "Receipt {0} is put away.";
			public const string ReceiptReleasing = "The {0} receipt is being released.";
			public const string ReceiptIsReleased = "The receipt is successfully released.";

			public const string POOrderInvalid = "Status of the {0} order is not valid for processing.";
			public const string POOrderMultiSites = "All items in the {0} order must refer to the same warehouse.";
			public const string POOrderUnableToCreateReceipt = "Cannot create a purchase receipt for the purchase order {0}. Create a purchase receipt manually.";

			public const string TransferNothingToRelease = "There are no transfers to release.";
			public const string TransferReleasing = "The {0} transfer is being released.";
			public const string TransferIsReleased = "The transfer is successfully released.";

			public const string ReceiptConfirmationPrompt = "Confirm receiving {1} {0}.";
			public const string PutAwayConfirmationPrompt = "Confirm putting away {1} {0}.";

			public const string InventoryMissingInReceipt = "The inventory item {0} does not exist in the receipt, or already has been received in full. Confirm adding a new line to receipt, or clear the flow.";
			public const string LotSerialMissingInReceipt = "The lot/serial number {0} does not exist in the receipt.";
			public const string LocationMissingInReceipt = "The location {0} does not exist in the receipt.";

			public const string ToLocationPrompt = "Scan the barcode of the destination location.";
			public const string ToLocationReady = "The {0} location is selected as the destination location.";
			public const string ToLocationNotSet = "Destination Location is not selected";

			public const string ReceiveNewLineWarning = "Scanned quantity exceeds the quantity in the {0} PO receipt for the {1} item. Would you like to add a new line in the receipt for the {2} {3} quantity?";

			public const string Overpicking = "The received quantity cannot be greater than the expected quantity.";
			public const string Underpicking = "The received quantity cannot become negative.";

			public const string Overputting = "The put away quantity cannot be greater than the received quantity.";
			public const string Underputting = "The put away quantity cannot become negative.";

			public const string NothingToPutAway = "No items to put away.";

			public const string CouldNotReceive = "Only items included in not released receipts can be received.";
			public const string CouldNotPutAway = "Only items included in released receipts can be put away.";
			public const string AlreadyPutAwayInFull = "The {0} receipt has already been put away in full.";

			public const string LinkTransferOverpicking = "Link quantity cannot be greater than the quantity of a transfer line split.";
			public const string LinkReceiptOverpicking = "Link quantity cannot be greater than the quantity of a receipt line split.";
			public const string LinkCartOverpicking = "Link quantity cannot be greater than the quantity of a cart line split.";
			public const string LinkUnderpicking = "Link quantity cannot be negative.";

			public const string RestQty = "Remaining Qty.";
		} 
		#endregion
	}
}