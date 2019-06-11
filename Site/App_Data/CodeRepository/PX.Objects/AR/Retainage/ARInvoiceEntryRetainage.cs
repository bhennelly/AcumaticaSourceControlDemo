﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

using PX.Common;
using PX.Data;
using PX.Objects.CM;
using PX.Objects.Common;
using PX.Objects.CR;
using PX.Objects.CS;
using PX.Objects.GL;
using PX.Objects.PM;
using PX.Objects.TX;
using static PX.Objects.AR.ARInvoiceEntry;

namespace PX.Objects.AR
{
	[Serializable]
	public class ARInvoiceEntryRetainage : PXGraphExtension<ARInvoiceEntry>
	{
		public static bool IsActive()
		{
			return PXAccess.FeatureInstalled<FeaturesSet.retainage>();
		}

		public override void Initialize()
		{
			base.Initialize();

			RetainageOptions releaseRetainageOptions = ReleaseRetainageOptions.Current;

			PXAction action = Base.Actions["action"];
			if (action != null)
			{
				action.AddMenuAction(releaseRetainage);
			}
		}

		#region Cache Attached Events

		[PXMergeAttributes(Method = MergeMethod.Append)]
		[ARRetainedTax(typeof(ARInvoice), typeof(ARTax), typeof(ARTaxTran))]
		protected virtual void ARTran_TaxCategoryID_CacheAttached(PXCache sender) { }
		
		[DBRetainagePercent(
			typeof(ARInvoice.retainageApply),
			typeof(ARInvoice.defRetainagePct),
			typeof(Sub<Current<ARTran.curyExtPrice>, Current<ARTran.curyDiscAmt>>),
			typeof(ARTran.curyRetainageAmt),
			typeof(ARTran.retainagePct))]
		protected virtual void ARTran_RetainagePct_CacheAttached(PXCache sender) { }

		[DBRetainageAmount(
			typeof(ARTran.curyInfoID),
			typeof(Sub<ARTran.curyExtPrice, ARTran.curyDiscAmt>),
			typeof(ARTran.curyRetainageAmt),
			typeof(ARTran.retainageAmt),
			typeof(ARTran.retainagePct))]
		protected virtual void ARTran_CuryRetainageAmt_CacheAttached(PXCache sender) { }

		[PXMergeAttributes(Method = MergeMethod.Append)]
		[PXFormula(typeof(
			IIf<Where<ARInvoice.projectID, NotEqual<NonProject>,
					And<Selector<ARInvoice.projectID, PMProject.baseType>, Equal<PMProject.ProjectBaseType>>>,
				Selector<ARInvoice.projectID, PMProject.retainagePct>,
				Selector<ARRegister.customerID, Customer.retainagePct>>
			))]
		protected virtual void ARInvoice_DefRetainagePct_CacheAttached(PXCache sender) { }

		[PXMergeAttributes(Method = MergeMethod.Append)]
		[PXFormula(typeof(ARRegister.curyRetainageTotal))]
		protected virtual void ARInvoice_CuryRetainageUnreleasedAmt_CacheAttached(PXCache sender) { }

		[PXMergeAttributes(Method = MergeMethod.Append)]
		[PXFormula(typeof(Switch<Case<Where2<
			FeatureInstalled<FeaturesSet.retainage>,
				And<ARRegister.retainageApply, Equal<True>,
				And<ARRegister.released, NotEqual<True>>>>,
			ARRegister.curyRetainageTotal>,
			ARRegister.curyRetainageUnpaidTotal>))]
		protected virtual void ARInvoice_CuryRetainageUnpaidTotal_CacheAttached(PXCache sender) { }

		[PXMergeAttributes(Method = MergeMethod.Append)]
		[PXFormula(typeof(
			IIf<Where2<FeatureInstalled<FeaturesSet.retainage>,
					And<ARRegister.docType, Equal<ARInvoiceType.invoice>,
					And<ARRegister.origModule, Equal<BatchModule.moduleAR>,
					And<Current<ARSetup.migrationMode>, Equal<False>>>>>,
				IsNull<Selector<ARRegister.customerID, Customer.retainageApply>, False>,
				False>))]
		[PXUIVerify(
			typeof(Where<
			ARRegister.retainageApply, NotEqual<True>,
			And<ARRegister.isRetainageDocument, NotEqual<True>,
				Or<Selector<ARInvoice.termsID, Terms.installmentType>, NotEqual<TermsInstallmentType.multiple>>>>),
			PXErrorLevel.Error,
			AP.Messages.RetainageWithMultipleCreditTerms)]
		[PXUIVerify(
			typeof(Where<ARRegister.retainageApply, NotEqual<True>,
				Or<ARRegister.curyID, Equal<GetSetupValue<Company.baseCuryID>>>>),
			PXErrorLevel.Error,
			AP.Messages.RetainageDocumentNotInBaseCurrency)]
		protected virtual void ARInvoice_RetainageApply_CacheAttached(PXCache sender) { }

		[PXMergeAttributes(Method = MergeMethod.Append)]
		[PXUIVerify(
			typeof(Where<ARRegister.curyRetainageTotal, GreaterEqual<decimal0>, And<ARRegister.hold, NotEqual<True>,
				Or<ARRegister.hold, Equal<True>>>>),
			PXErrorLevel.Error,
			AP.Messages.IncorrectRetainageTotalAmount)]
		protected virtual void ARInvoice_CuryRetainageTotal_CacheAttached(PXCache sender) { }

		#endregion

		#region APInvoice Events

		protected virtual void ARInvoice_RowSelected(PXCache cache, PXRowSelectedEventArgs e)
		{
			ARInvoice doc = e.Row as ARInvoice;
			if (doc == null) return;

			bool isDocumentReleased = doc.Released == true;
			bool isDocumentInvoice = doc.DocType == ARDocType.Invoice;
			bool retainageApply = doc.RetainageApply == true;

			releaseRetainage.SetEnabled(
				isDocumentInvoice &&
				isDocumentReleased &&
				retainageApply &&
				doc.CuryRetainageUnreleasedAmt > 0m);
		}

		protected virtual void ARInvoice_RowSelecting(PXCache sender, PXRowSelectingEventArgs e)
		{
			ARInvoice document = e.Row as ARInvoice;
			if (document == null) return;

			if (document.RetainageApply == true &&
				document.Released == true)
			{
				using (new PXConnectionScope())
				{
					ARRetainageInvoice dummyInvoice = new ARRetainageInvoice();
					dummyInvoice.CuryRetainageUnpaidTotal = 0m;
					dummyInvoice.CuryRetainagePaidTotal = 0m;

					foreach (ARRetainageInvoice childRetainageBill in RetainageDocuments
						.Select(document.DocType, document.RefNbr)
						.RowCast<ARRetainageInvoice>()
						.Where(res => res.Released == true))
					{
						dummyInvoice.DocType = childRetainageBill.DocType;
						dummyInvoice.RefNbr = childRetainageBill.RefNbr;
						dummyInvoice.CuryOrigDocAmt = childRetainageBill.CuryOrigDocAmt;
						dummyInvoice.CuryDocBal = childRetainageBill.CuryOrigDocAmt;

						foreach (ARAdjust application in PXSelect<ARAdjust,
							Where<ARAdjust.adjdDocType, Equal<Required<ARAdjust.adjdDocType>>,
								And<ARAdjust.adjdRefNbr, Equal<Required<ARAdjust.adjdRefNbr>>,
								And<ARAdjust.released, Equal<True>,
								And<ARAdjust.voided, NotEqual<True>,
								And<ARAdjust.adjgDocType, NotEqual<ARDocType.creditMemo>>>>>>>
							.Select(Base, childRetainageBill.DocType, childRetainageBill.RefNbr))
						{
							dummyInvoice.AdjustBalance(application);
						}

						dummyInvoice.CuryRetainageUnpaidTotal += childRetainageBill.DocBal * childRetainageBill.SignAmount;
						dummyInvoice.CuryRetainagePaidTotal += (dummyInvoice.CuryOrigDocAmt - dummyInvoice.CuryDocBal) * dummyInvoice.SignAmount;
					}

					document.CuryRetainageUnpaidTotal = document.CuryRetainageUnreleasedAmt + dummyInvoice.CuryRetainageUnpaidTotal;
					document.CuryRetainagePaidTotal = dummyInvoice.CuryRetainagePaidTotal;
				}
			}
		}

		protected virtual void ARInvoice_RowPersisting(PXCache sender, PXRowPersistingEventArgs e)
		{
			ARInvoice doc = (ARInvoice)e.Row;

			Terms terms = (Terms)PXSelectorAttribute.Select<ARInvoice.termsID>(Base.Document.Cache, doc);

			if (terms != null && doc.RetainageApply == true && terms.InstallmentType == CS.TermsInstallmentType.Multiple)
			{
				sender.RaiseExceptionHandling<ARInvoice.termsID>(doc, doc.TermsID, new PXSetPropertyException(AP.Messages.RetainageWithMultipleCreditTerms));
			}

			bool disablePersistingCheckForRetainageAccountAndSub = doc.RetainageApply != true;
			PXDefaultAttribute.SetPersistingCheck<ARRegister.retainageAcctID>(sender, doc, disablePersistingCheckForRetainageAccountAndSub
				? PXPersistingCheck.Nothing
				: PXPersistingCheck.NullOrBlank);
			PXDefaultAttribute.SetPersistingCheck<ARInvoice.retainageSubID>(sender, doc, disablePersistingCheckForRetainageAccountAndSub
				? PXPersistingCheck.Nothing
				: PXPersistingCheck.NullOrBlank);
		}

		protected virtual void ARInvoice_RetainageAcctID_FieldDefaulting(PXCache sender, PXFieldDefaultingEventArgs e)
		{
			if (Base.location.Current != null && e.Row != null)
			{
				e.NewValue = Base.GetAcctSub<CR.Location.aRRetainageAcctID>(Base.location.Cache, Base.location.Current);
			}
		}
		
		protected virtual void ARInvoice_RetainageSubID_FieldDefaulting(PXCache sender, PXFieldDefaultingEventArgs e)
		{
			if (Base.location.Current != null && e.Row != null)
			{
				e.NewValue = Base.GetAcctSub<CR.Location.aRRetainageSubID>(Base.location.Cache, Base.location.Current);
			}
		}

		protected virtual void ARInvoice_CustomerLocationID_FieldUpdated(PXCache sender, PXFieldUpdatedEventArgs e)
		{
			sender.SetDefaultExt<ARInvoice.retainageAcctID>(e.Row);
			sender.SetDefaultExt<ARInvoice.retainageSubID>(e.Row);
		}

		protected virtual void ARInvoice_RetainageApply_FieldVerifying(PXCache sender, PXFieldVerifyingEventArgs e)
		{
			ARInvoice document = (ARInvoice)e.Row;
			bool? newValue = (bool?)e.NewValue;

			if (document == null) return;

			if (Base.GetType() == typeof(PX.Objects.CT.CTBillEngine.ARContractInvoiceEntry))
			{
				e.NewValue = false;
				return;
			}

			if (document.RetainageApply == true && newValue == false)
			{
				IEnumerable<ARTran> trans = Base.Transactions.Select().AsEnumerable().Where(tran => ((ARTran)tran).CuryRetainageAmt != 0 || ((ARTran)tran).RetainagePct != 0).RowCast<ARTran>();

				if (!trans.Any()) return;

				WebDialogResult wdr =
					Base.Document.Ask(
						Messages.Warning,
						AP.Messages.UncheckApplyRetainage,
						MessageButtons.YesNo,
						MessageIcon.Warning);

				if (wdr == WebDialogResult.Yes)
				{
					foreach (ARTran tran in trans)
					{
						tran.CuryRetainageAmt = 0m;
						tran.RetainagePct = 0m;
						Base.Transactions.Update(tran);
					}
				}
				else
				{
					e.Cancel = true;
					e.NewValue = true;
				}
			}
			else if (document.RetainageApply != true && newValue == true)
			{
				ClearCurrentDocumentDiscountDetails();
			}
		}

		protected virtual void ClearCurrentDocumentDiscountDetails()
		{
			Base.ARDiscountDetails
					.Select()
					.RowCast<ARInvoiceDiscountDetail>()
					.ForEach(discountDetail => Base.ARDiscountDetails.Cache.Delete(discountDetail));

			Base.Discount_Row
				.Select()
				.RowCast<ARTran>()
				.ForEach(tran => Base.Discount_Row.Cache.Delete(tran));
		}

		#endregion

		#region ARInvoiceDiscountDetail events

		protected virtual void ARInvoiceDiscountDetail_RowInserting(PXCache sender, PXRowInsertingEventArgs e)
		{
			ARInvoice invoice = Base.Document.Current;

			if (invoice?.RetainageApply == true ||
				invoice?.IsRetainageDocument == true)
			{
				e.Cancel = true;
			}
		}

		public delegate void AddDiscountDelegate(PXCache sender, ARInvoice row);

		[PXOverride]
		public void AddDiscount(
			PXCache sender,
			ARInvoice row,
			AddDiscountDelegate baseMethod)
		{
			bool isRetainage =
				row.RetainageApply == true ||
				row.IsRetainageDocument == true;

			if (!isRetainage)
			{
				baseMethod(sender, row);
			}
		}

		#endregion

		[PXReadOnlyView]
		[PXCopyPasteHiddenView]
		// ARRetainageInvoice class is a ARRegister class alias
		// because only ARRegister part is affecting by the release process
		// and only this way we can get a proper behavior for the QueryCache mechanism.
		//
		public PXSelectJoin<ARRetainageInvoice,
			InnerJoinSingleTable<ARInvoice, On<ARInvoice.docType, Equal<ARRetainageInvoice.docType>,
				And<ARInvoice.refNbr, Equal<ARRetainageInvoice.refNbr>>>>,
			Where<ARRetainageInvoice.isRetainageDocument, Equal<True>,
				And<ARRetainageInvoice.origDocType, Equal<Optional<ARInvoice.docType>>,
				And<ARRetainageInvoice.origRefNbr, Equal<Optional<ARInvoice.refNbr>>>>>> RetainageDocuments;

		[PXCopyPasteHiddenView]
		public PXFilter<RetainageOptions> ReleaseRetainageOptions;

		public PXAction<ARInvoice> releaseRetainage;

		[PXUIField(
			DisplayName = "Release Retainage",
			MapEnableRights = PXCacheRights.Update,
			MapViewRights = PXCacheRights.Update)]
		[PXProcessButton]
		[ARMigrationModeDependentActionRestriction(
			restrictInMigrationMode: true,
			restrictForRegularDocumentInMigrationMode: true,
			restrictForUnreleasedMigratedDocumentInNormalMode: true)]
		public virtual IEnumerable ReleaseRetainage(PXAdapter adapter)
		{
			ARInvoice doc = Base.Document.Current;

			if (doc != null &&
				doc.DocType == ARDocType.Invoice &&
				doc.RetainageApply == true &&
				doc.CuryRetainageUnreleasedAmt > 0m)
			{
				ARRetainageInvoice retainageDoc = RetainageDocuments
					.Select()
					.RowCast<ARRetainageInvoice>()
					.FirstOrDefault(row => row.Released != true);

				if (retainageDoc != null)
				{
					throw new PXException(
						AP.Messages.ReleaseRetainageNotReleasedDocument,
						PXMessages.LocalizeNoPrefix(ARDocTypeDict[retainageDoc.DocType]),
						retainageDoc.RefNbr,
						PXMessages.LocalizeNoPrefix(ARDocTypeDict[doc.DocType]));
				}

				ARRegister reversingDoc;
				if (Base.CheckReversingRetainageDocumentAlreadyExists(Base.Document.Current, out reversingDoc))
				{
					throw new PXException(
						AP.Messages.ReleaseRetainageReversingDocumentExists,
						PXMessages.LocalizeNoPrefix(ARDocTypeDict[doc.DocType]),
						PXMessages.LocalizeNoPrefix(ARDocTypeDict[reversingDoc.DocType]),
						reversingDoc.RefNbr);
				}

				Base.Save.Press();

				RetainageOptions retainageOpts = ReleaseRetainageOptions.Current;
				WebDialogResult wdr = ReleaseRetainageOptions.AskExt(
					(graph, view) =>
					{
						retainageOpts.CuryRetainageTotal = doc.CuryRetainageUnreleasedAmt;
						retainageOpts.RetainagePct = 100m;
						retainageOpts.CuryRetainageAmt = doc.CuryRetainageUnreleasedAmt;
						retainageOpts.CuryRetainageUnreleasedAmt = retainageOpts.CuryRetainageTotal - retainageOpts.CuryRetainageAmt;
					},
					true);

				if (wdr == WebDialogResult.OK)
				{
					ARInvoice invoice = PXCache<ARInvoice>.CreateCopy(doc);

					try
					{
						ReleaseRetainageProc(invoice, retainageOpts);
						return new List<ARInvoice> { Base.Document.Current };
					}
					catch (PXException)
					{
						Base.Clear(PXClearOption.PreserveTimeStamp);
						Base.Document.Current = doc;
						throw;
					}
				}
			}

			return adapter.Get();
		}

		public virtual ARInvoice ReleaseRetainageProc(ARInvoice doc, RetainageOptions retainageOpts, bool isAutoRelease = false)
		{
			Base.Clear(PXClearOption.PreserveTimeStamp);

			if (retainageOpts.CuryRetainageAmt <= 0 || retainageOpts.CuryRetainageAmt > doc.CuryRetainageUnreleasedAmt)
			{
				throw new PXException(AP.Messages.IncorrectRetainageAmount);
			}

			// Magic. We need to prevent rewriting of CurrencyInfo.IsReadOnly 
			// by true in CurrencyInfoView
			// 
			Base.CurrentDocument.Cache.AllowUpdate = true;

			PXResult<ARInvoice, CurrencyInfo, Terms, Customer> resultDoc =
				ARInvoice_CurrencyInfo_Terms_Customer
					.SelectSingleBound(Base, null, doc.DocType, doc.RefNbr, doc.CustomerID).AsEnumerable()
					.Cast<PXResult<ARInvoice, CurrencyInfo, Terms, Customer>>()
					.First();

			CurrencyInfo info = resultDoc;
			ARInvoice origInvoice = resultDoc;
			Customer customer = resultDoc;

			CurrencyInfo new_info = PXCache<CurrencyInfo>.CreateCopy(info);
			new_info.CuryInfoID = null;
			new_info.IsReadOnly = false;
			new_info = PXCache<CurrencyInfo>.CreateCopy(Base.currencyinfo.Insert(new_info));

			ARInvoice invoice = PXCache<ARInvoice>.CreateCopy(origInvoice);
			invoice.CuryInfoID = new_info.CuryInfoID;
			invoice.DocType = ARDocType.Invoice;
			invoice.RefNbr = null;
			invoice.LineCntr = null;
			invoice.InvoiceNbr = origInvoice.InvoiceNbr;

			// Must be set for _RowSelected event handler
			// 
			invoice.OpenDoc = true;
			invoice.Released = false;

			Base.Document.Cache.SetDefaultExt<ARInvoice.isMigratedRecord>(invoice);
			Base.Document.Cache.SetDefaultExt<ARInvoice.hold>(invoice);
			invoice.BatchNbr = null;
			invoice.ScheduleID = null;
			invoice.Scheduled = false;
			invoice.NoteID = null;

			invoice.DueDate = null;
			invoice.DiscDate = null;
			invoice.CuryOrigDiscAmt = 0m;
			invoice.OrigDocType = origInvoice.DocType;
			invoice.OrigRefNbr = origInvoice.RefNbr;
			invoice.OrigDocDate = origInvoice.DocDate;

			invoice.CuryLineTotal = 0m;
			invoice.IsTaxPosted = false;
			invoice.IsTaxValid = false;
			invoice.CuryVatTaxableTotal = 0m;
			invoice.CuryVatExemptTotal = 0m;

			invoice.CuryDocBal = 0m;
			invoice.CuryOrigDocAmt = retainageOpts.CuryRetainageAmt;
			invoice.Hold = !isAutoRelease && Base.ARSetup.Current.HoldEntry == true;

			invoice.DocDate = retainageOpts.DocDate;
			FinPeriodIDAttribute.SetPeriodsByMaster<ARInvoice.finPeriodID>(Base.Document.Cache, invoice, retainageOpts.MasterFinPeriodID);

			Base.ClearRetainageSummary(invoice);
			invoice.RetainageApply = false;
			invoice.IsRetainageDocument = true;

			invoice = Base.Document.Insert(invoice);

			if (new_info != null)
			{
				CurrencyInfo b_info = (CurrencyInfo)PXSelect<CurrencyInfo,
					Where<CurrencyInfo.curyInfoID, Equal<Current<ARInvoice.curyInfoID>>>>.Select(Base);

				b_info.CuryID = new_info.CuryID;
				b_info.CuryEffDate = new_info.CuryEffDate;
				b_info.CuryRateTypeID = new_info.CuryRateTypeID;
				b_info.CuryRate = new_info.CuryRate;
				b_info.RecipRate = new_info.RecipRate;
				b_info.CuryMultDiv = new_info.CuryMultDiv;
				Base.currencyinfo.Update(b_info);
			}

			decimal retainagePercent = (decimal)(retainageOpts.CuryRetainageAmt / doc.CuryRetainageTotal);

			PXResultset<ARTran> details = PXSelectGroupBy<ARTran,
				Where<ARTran.tranType, Equal<Required<ARTran.tranType>>,
					And<ARTran.refNbr, Equal<Required<ARTran.refNbr>>,
					And<ARTran.curyRetainageAmt, NotEqual<decimal0>>>>,
				Aggregate<
					GroupBy<ARTran.taxCategoryID,
					Sum<ARTran.curyRetainageAmt>>>>
					.Select(Base, doc.DocType, doc.RefNbr);

			ARTran tranNew = null;
			decimal prevCuryTotal = 0m;

			TaxCalc oldTaxCalc = TaxBaseAttribute.GetTaxCalc<ARTran.taxCategoryID>(Base.Transactions.Cache, null);
			TaxBaseAttribute.SetTaxCalc<ARTran.taxCategoryID>(Base.Transactions.Cache, null, TaxCalc.ManualCalc);

			foreach (ARTran detail in details)
			{
				// Create ARTran record for chosen retainage amount, 
				// clear all required fields to prevent tax calculation,
				// discount calculation and retainage calculation.
				// CuryUnitPrice = 0m and CuryExtPrice = 0m here to prevent their 
				// FieldDefaulting events, because in our case default value 
				// should be equal to zero.
				//
				tranNew = Base.Transactions.Insert(new ARTran
				{
					CuryUnitPrice = 0m,
					CuryExtPrice = 0m
				});

				tranNew.BranchID = origInvoice.BranchID;
				tranNew.TaxCategoryID = detail.TaxCategoryID;
				tranNew.AccountID = origInvoice.RetainageAcctID;
				tranNew.SubID = origInvoice.RetainageSubID;
				tranNew.ProjectID = ProjectDefaultAttribute.NonProject();

				tranNew.Qty = 0m;
				tranNew.ManualDisc = true;
				tranNew.DiscPct = 0m;
				tranNew.CuryDiscAmt = 0m;
				tranNew.RetainagePct = 0m;
				tranNew.CuryRetainageAmt = 0m;
				tranNew.CuryTaxableAmt = 0m;
				tranNew.CuryTaxAmt = 0;
				tranNew.GroupDiscountRate = 1m;
				tranNew.DocumentDiscountRate = 1m;

				using (new PXLocaleScope(customer.LocaleName))
				{
					tranNew.TranDesc = PXMessages.LocalizeFormatNoPrefix(
						AP.Messages.RetainageForTransactionDescription,
						ARDocTypeDict[origInvoice.DocType],
						origInvoice.RefNbr);
				}

				prevCuryTotal = (retainageOpts.CuryRetainageAmt ?? 0m) - (invoice.CuryDocBal ?? 0m);
				tranNew.CuryExtPrice = PXCurrencyAttribute.RoundCury(Base.Transactions.Cache, tranNew, (detail.CuryRetainageAmt ?? 0m) * retainagePercent);
				tranNew = Base.Transactions.Update(tranNew);
			}

			ClearCurrentDocumentDiscountDetails();

			// We should copy all taxes from the original document
			// because it is possible to add or delete them.
			// 
			foreach (ARTaxTran artaxtran in PXSelect<ARTaxTran,
				Where<ARTaxTran.module, Equal<BatchModule.moduleAR>,
					And<ARTaxTran.tranType, Equal<Required<ARTaxTran.tranType>>,
					And<ARTaxTran.refNbr, Equal<Required<ARTaxTran.refNbr>>>>>>
				.Select(Base, origInvoice.DocType, origInvoice.RefNbr)
				.RowCast<ARTaxTran>()
				.Where(row => row.CuryRetainedTaxAmt != 0m))
			{
				ARTaxTran new_artaxtran = Base.Taxes.Insert(new ARTaxTran
				{
					TaxID = artaxtran.TaxID
				});

				if (new_artaxtran != null)
				{
					new_artaxtran = PXCache<ARTaxTran>.CreateCopy(new_artaxtran);
					new_artaxtran.TaxRate = artaxtran.TaxRate;
					new_artaxtran = Base.Taxes.Update(new_artaxtran);
				}
			}

			TaxBaseAttribute.SetTaxCalc<ARTran.taxCategoryID>(Base.Transactions.Cache, null, oldTaxCalc);
			decimal diff = (retainageOpts.CuryRetainageAmt ?? 0m) - (invoice.CuryDocBal ?? 0m);

			if (tranNew != null && diff != 0m)
			{
				HashSet<string> taxList = PXSelectJoin<ARTax,
					InnerJoin<Tax, On<Tax.taxID, Equal<ARTax.taxID>>>,
					Where<ARTax.tranType, Equal<Required<ARTax.tranType>>,
						And<ARTax.refNbr, Equal<Required<ARTax.refNbr>>,
						And<ARTax.lineNbr, Equal<Required<ARTax.lineNbr>>,
						And<Tax.taxType, NotEqual<CSTaxType.use>>>>>>
					.Select(Base, tranNew.TranType, tranNew.RefNbr, tranNew.LineNbr)
					.RowCast<ARTax>()
					.Select(row => row.TaxID)
					.ToHashSet();

				// To guarantee correct document total amount 
				// we should calculate last line total, 
				// including its taxes.
				//
				TaxAttribute.CalcTaxable calcClass = new TaxAttribute.CalcTaxable(false, TaxAttribute.TaxCalcLevelEnforcing.None);
				decimal curyExtPrice = calcClass.CalcTaxableFromTotalAmount(
					Base.Transactions.Cache,
					tranNew,
					taxList,
					invoice.DocDate.Value,
					prevCuryTotal);

				tranNew.CuryExtPrice = curyExtPrice;
				tranNew = Base.Transactions.Update(tranNew);
			}

			return invoice;
		}

		public PXAction<ARInvoice> ViewRetainageDocument;

		[PXUIField(Visible = false, MapEnableRights = PXCacheRights.Select, MapViewRights = PXCacheRights.Select)]
		[PXLookupButton]
		protected virtual IEnumerable viewRetainageDocument(PXAdapter adapter)
		{
			RedirectionToOrigDoc.TryRedirect(RetainageDocuments.Current.DocType, RetainageDocuments.Current.RefNbr, RetainageDocuments.Current.OrigModule);
			return adapter.Get();
		}
	}
}