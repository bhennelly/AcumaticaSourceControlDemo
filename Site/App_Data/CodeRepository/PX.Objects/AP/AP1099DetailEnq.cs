using System;
using System.Collections;
using System.Collections.Generic;
using PX.Data;
using PX.Objects.Common.Extensions;
using PX.Objects.CS;
using PX.Objects.GL;
using PX.Objects.GL.Attributes;
using System.Linq;
using PX.Objects.GL.DAC;

namespace PX.Objects.AP
{
    [System.SerializableAttribute]
	public partial class AP1099YearMaster : IBqlTable
	{
		#region FinYear
		public abstract class finYear : PX.Data.BQL.BqlString.Field<finYear> { }

		[PXDBString(4, IsFixed = true)]
		[PXDefault]
		[PXUIField(DisplayName = "1099 Year", Visibility = PXUIVisibility.SelectorVisible)]
		[PXSelector(typeof(Search<AP1099Year.finYear,
			Where<AP1099Year.organizationID, Equal<Current<AP1099YearMaster.organizationID>>>>))]
		public virtual String FinYear { get; set; }
		#endregion
		#region VendorID
		public abstract class vendorID : PX.Data.BQL.BqlInt.Field<vendorID> { }
		protected Int32? _VendorID;
		[Vendor(typeof(Search<Vendor.bAccountID, Where<Vendor.vendor1099, Equal<True>>>), DisplayName = "Vendor", DescriptionField = typeof(Vendor.acctName))]
		public virtual Int32? VendorID
		{
			get
			{
				return this._VendorID;
			}
			set
			{
				this._VendorID = value;
			}
		}
		#endregion
		#region OrganizationID
		public abstract class organizationID : PX.Data.BQL.BqlInt.Field<organizationID> { }

		[Organization(false)]
		public virtual Int32? OrganizationID { get; set; }
		#endregion
	}

	public class AP1099DetailEnq : PXGraph<AP1099DetailEnq>
	{
		#region Views/Selects
		
		public PXFilter<AP1099YearMaster> YearVendorHeader;
		[PXFilterable]
		public PXSelectJoinGroupBy<AP1099Box,
			LeftJoin<AP1099History, On<AP1099History.vendorID, Equal<Current<AP1099YearMaster.vendorID>>,
			And<AP1099History.boxNbr, Equal<AP1099Box.boxNbr>,
			And<AP1099History.finYear, Equal<Current<AP1099YearMaster.finYear>>>>>>,
			Where<boolTrue, Equal<boolTrue>>,
			Aggregate<GroupBy<AP1099Box.boxNbr, Sum<AP1099History.histAmt>>>> YearVendorSummary;

		#endregion

		public PXCancel<AP1099YearMaster> Cancel;
		public PXAction<AP1099YearMaster> firstVendor;
		public PXAction<AP1099YearMaster> previousVendor;
		public PXAction<AP1099YearMaster> nextVendor;
		public PXAction<AP1099YearMaster> lastVendor;
		public PXAction<AP1099YearMaster> reportsFolder;
		public PXAction<AP1099YearMaster> year1099SummaryReport;
		public PXAction<AP1099YearMaster> year1099DetailReport;

		public IEnumerable yearVendorSummary()
		{
			PXResultset<AP1099Box> list;
			using (new PXReadBranchRestrictedScope(YearVendorHeader.Current.OrganizationID.SingleToArray(), null))
			{
			   list = PXSelectJoinGroupBy<AP1099Box,
										LeftJoin<AP1099History, 
											On<AP1099History.vendorID, Equal<Current<AP1099YearMaster.vendorID>>,
											And<AP1099History.boxNbr, Equal<AP1099Box.boxNbr>,
											And<AP1099History.finYear, Equal<Current<AP1099YearMaster.finYear>>>>>>,
										Where<Current<AP1099YearMaster.organizationID>, IsNotNull>,
										Aggregate<GroupBy<AP1099Box.boxNbr, Sum<AP1099History.histAmt>>>>
										.Select(this);
				//There is "OrganizationID is not null" because select parameters must be changed after changing masterBranch. 
				//If select parametrs is unchanged since last use this select will not be executed and return previous result.
			}
			return list;
		}
		
		
		[PXUIField(DisplayName = "", MapEnableRights = PXCacheRights.Select, MapViewRights = PXCacheRights.Select)]
		[PXFirstButton]
		public virtual IEnumerable FirstVendor(PXAdapter adapter)
		{
			AP1099YearMaster filter = (AP1099YearMaster)YearVendorHeader.Current;
			Vendor next_vendor = (Vendor)PXSelect<Vendor, Where<Vendor.vendor1099, Equal<True>>, OrderBy<Asc<Vendor.acctCD>>>.Select(this);
			if (next_vendor != null)
			{
				filter.VendorID = next_vendor.BAccountID;
			}
			return adapter.Get();
		}
		
		[PXUIField(DisplayName = "", MapEnableRights = PXCacheRights.Select, MapViewRights = PXCacheRights.Select)]
		[PXPreviousButton]
		public virtual IEnumerable PreviousVendor(PXAdapter adapter)
		{
			AP1099YearMaster filter = (AP1099YearMaster) YearVendorHeader.Current;
			Vendor vendor = (Vendor) PXSelect<Vendor, Where<Vendor.bAccountID, Equal<Current<AP1099YearMaster.vendorID>>>>.Select(this);
			if (vendor == null)
			{
				vendor = new Vendor();
				vendor.AcctCD = "";
			}
			Vendor next_vendor = (Vendor)PXSelect<Vendor, Where<Vendor.vendor1099, Equal<True>, And<Vendor.acctCD, Less<Required<Vendor.acctCD>>>>>.Select(this, vendor.AcctCD);
			if (next_vendor != null)
			{
				filter.VendorID = next_vendor.BAccountID;
			}
			return adapter.Get();
		}

		[PXUIField(DisplayName = "", MapEnableRights = PXCacheRights.Select, MapViewRights = PXCacheRights.Select)]
		[PXNextButton]
		public virtual IEnumerable NextVendor(PXAdapter adapter)
		{
			AP1099YearMaster filter = (AP1099YearMaster)YearVendorHeader.Current;

			Vendor vendor = (Vendor) PXSelect<Vendor, Where<Vendor.bAccountID, Equal<Current<AP1099YearMaster.vendorID>>>>.Select(this);
			if (vendor == null)
			{
				vendor = new Vendor();
				vendor.AcctCD = "";
			}
			Vendor next_vendor = (Vendor) PXSelect<Vendor, Where<Vendor.vendor1099, Equal<True>, And<Vendor.acctCD, Greater<Required<Vendor.acctCD>>>>>.Select(this, vendor.AcctCD);
			if (next_vendor != null)
			{
				filter.VendorID = next_vendor.BAccountID;
			}
			return adapter.Get();
		}

		[PXUIField(DisplayName = "", MapEnableRights = PXCacheRights.Select, MapViewRights = PXCacheRights.Select)]
		[PXLastButton]
		public virtual IEnumerable LastVendor(PXAdapter adapter)
		{
			AP1099YearMaster filter = (AP1099YearMaster)YearVendorHeader.Current;
			Vendor next_vendor = (Vendor)PXSelect<Vendor, Where<Vendor.vendor1099, Equal<True>>, OrderBy<Desc<Vendor.acctCD>>>.Select(this);
			if (next_vendor != null)
			{
				filter.VendorID = next_vendor.BAccountID;
			}
			return adapter.Get();
		}

		[PXUIField(DisplayName = "Reports", MapEnableRights = PXCacheRights.Select)]
		[PXButton(SpecialType = PXSpecialButtonType.ReportsFolder)]
		protected virtual IEnumerable Reportsfolder(PXAdapter adapter)
		{
			return adapter.Get();
		}

		[PXUIField(DisplayName = Messages.Year1099SummaryReport, MapEnableRights = PXCacheRights.Select, MapViewRights = PXCacheRights.Select)]
		public virtual IEnumerable Year1099SummaryReport(PXAdapter adapter)
		{
			AP1099YearMaster filter = YearVendorHeader.Current;
			if (filter != null)
			{
				Dictionary<string, string> parameters = new Dictionary<string, string>();
				Organization org = OrganizationMaint.FindOrganizationByID(this, filter.OrganizationID);
				parameters["OrganizationID"] = org?.OrganizationCD;
				parameters["FinYear"] = filter.FinYear;
				throw new PXReportRequiredException(parameters, "AP654000", Messages.Year1099SummaryReport); 
			}
			return adapter.Get();
		}

		[PXUIField(DisplayName = Messages.Year1099DetailReport, MapEnableRights = PXCacheRights.Select, MapViewRights = PXCacheRights.Select)]
		public virtual IEnumerable Year1099DetailReport(PXAdapter adapter)
		{
			AP1099YearMaster filter = YearVendorHeader.Current;
			if (filter != null)
			{
				Dictionary<string, string> parameters = new Dictionary<string, string>();
				Organization org = OrganizationMaint.FindOrganizationByID(this, filter.OrganizationID);
				parameters["OrganizationID"] = org?.OrganizationCD;
				parameters["FinYear"] = filter.FinYear;
				throw new PXReportRequiredException(parameters, "AP654500", Messages.Year1099DetailReport);
			}
			return adapter.Get();
		}
		
		public AP1099DetailEnq()
		{
			APSetup setup = APSetup.Current;
			PXUIFieldAttribute.SetEnabled<AP1099Box.boxNbr>(YearVendorSummary.Cache, null, false);
			PXUIFieldAttribute.SetEnabled<AP1099Box.descr>(YearVendorSummary.Cache, null, false);
			PXUIFieldAttribute.SetRequired<AP1099YearMaster.vendorID>(YearVendorHeader.Cache, true);
			reportsFolder.MenuAutoOpen = true;
			reportsFolder.AddMenuAction(year1099SummaryReport);
			reportsFolder.AddMenuAction(year1099DetailReport);
		}
		public PXSetup<APSetup> APSetup;
	}
}
