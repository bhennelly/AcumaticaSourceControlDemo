﻿using PX.Data;
using PX.Objects.CM;
using PX.Objects.CS;
using PX.Objects.GL;
using PX.Objects.GL.FinPeriods;
using PX.Objects.GL.FinPeriods.TableDefinition;
using PX.Objects.IN;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;

namespace PX.Objects.PM
{
	public class ForecastMaint : PXGraph<ForecastMaint, PMForecast>, PXImportAttribute.IPXPrepareItems
	{
		public PXSelect<PMForecast> Revisions;
		public PXFilter<PMForecastFilter> Filter;

		[PXImport(typeof(PMForecast))]
		[PXVirtualDAC]
		public PXSelect<PMForecastRecord> Items;

		public PXFilter<PMForecastAddPeriodDialogInfo> AddPeriodDialog;
		public PXFilter<PMForecastCopyDialogInfo> CopyDialog;
		public PXFilter<PMForecastDistributeDialogInfo> DistributeDialog;

		public PXSelect<PMProject, Where<PMProject.contractID, Equal<Current<PMForecast.projectID>>>> Project;
		public PXSelect<PMTask, Where<PMTask.projectID, Equal<Current<PMForecast.projectID>>>> Tasks;

		public PXSelect<PMBudgetInfo, Where<PMBudgetInfo.projectID, Equal<Current<PMForecast.projectID>>,
			And2<Where<Current<PMForecastFilter.projectTaskID>, IsNull, Or<Current<PMForecastFilter.projectTaskID>, Equal<PMBudgetInfo.projectTaskID>>>,
			And2<Where<Current<PMForecastFilter.accountGroupType>, Equal<PMAccountType.all>, Or<Current<PMForecastFilter.accountGroupType>, Equal<PMBudgetInfo.accountGroupType>,
				Or<Where<Current<PMForecastFilter.accountGroupType>, Equal<AccountType.expense>, And<PMBudgetInfo.isExpense, Equal<True>>>>>>,
			And2<Where<Current<PMForecastFilter.accountGroupID>, IsNull, Or<Current<PMForecastFilter.accountGroupID>, Equal<PMBudgetInfo.accountGroupID>>>,
			And2<Where<Current<PMForecastFilter.inventoryID>, IsNull, Or<Current<PMForecastFilter.inventoryID>, Equal<PMBudgetInfo.inventoryID>>>,
			And2<Where<Current<PMForecastFilter.costCodeID>, IsNull, Or<Current<PMForecastFilter.costCodeID>, Equal<PMBudgetInfo.costCodeID>>>,
			And<Where<Current<PMForecastFilter.accountGroupID>, IsNull, Or<Current<PMForecastFilter.accountGroupID>, Equal<PMBudgetInfo.accountGroupID>>>>>>>>>>> Budgets;

		public PXSelect<PMForecastHistoryInfo, Where<PMForecastHistoryInfo.projectID, Equal<Current<PMForecast.projectID>>,
			And2<Where<Current<PMForecastHistoryInfo.projectTaskID>, IsNull, Or<Current<PMForecastFilter.projectTaskID>, Equal<PMForecastHistoryInfo.projectTaskID>>>,
			And2<Where<Current<PMForecastFilter.accountGroupType>, Equal<PMAccountType.all>, Or<Current<PMForecastFilter.accountGroupType>, Equal<PMForecastHistoryInfo.accountGroupType>,
				Or<Where<Current<PMForecastFilter.accountGroupType>, Equal<AccountType.expense>, And<PMForecastHistoryInfo.isExpense, Equal<True>>>>>>,
			And2<Where<Current<PMForecastHistoryInfo.accountGroupID>, IsNull, Or<Current<PMForecastFilter.accountGroupID>, Equal<PMForecastHistoryInfo.accountGroupID>>>,
			And2<Where<Current<PMForecastHistoryInfo.inventoryID>, IsNull, Or<Current<PMForecastFilter.inventoryID>, Equal<PMForecastHistoryInfo.inventoryID>>>,
			And2<Where<Current<PMForecastHistoryInfo.costCodeID>, IsNull, Or<Current<PMForecastFilter.costCodeID>, Equal<PMForecastHistoryInfo.costCodeID>>>,
			And<Where<Current<PMForecastHistoryInfo.accountGroupID>, IsNull, Or<Current<PMForecastFilter.accountGroupID>, Equal<PMForecastHistoryInfo.accountGroupID>>>>>>>>>>> Actuals;


		public PXSelect<PMForecastDetailInfo, Where<PMForecastDetailInfo.projectID, Equal<Current<PMForecast.projectID>>,
			And<PMForecastDetailInfo.revisionID, Equal<Current<PMForecast.revisionID>>,
			And2<Where<Current<PMForecastFilter.projectTaskID>, IsNull, Or<Current<PMForecastFilter.projectTaskID>, Equal<PMForecastDetailInfo.projectTaskID>>>,
			And2<Where<Current<PMForecastFilter.accountGroupType>, Equal<PMAccountType.all>, Or<Current<PMForecastFilter.accountGroupType>, Equal<PMForecastDetailInfo.accountGroupType>,
				Or<Where<Current<PMForecastFilter.accountGroupType>, Equal<AccountType.expense>, And<PMForecastDetailInfo.isExpense, Equal<True>>>>>>,
			And2<Where<Current<PMForecastFilter.accountGroupID>, IsNull, Or<Current<PMForecastFilter.accountGroupID>, Equal<PMForecastDetailInfo.accountGroupID>>>,
			And2<Where<Current<PMForecastFilter.inventoryID>, IsNull, Or<Current<PMForecastFilter.inventoryID>, Equal<PMForecastDetailInfo.inventoryID>>>,
			And2<Where<Current<PMForecastFilter.costCodeID>, IsNull, Or<Current<PMForecastFilter.costCodeID>, Equal<PMForecastDetailInfo.costCodeID>>>,
			And<Where<Current<PMForecastFilter.accountGroupID>, IsNull, Or<Current<PMForecastFilter.accountGroupID>, Equal<PMForecastDetailInfo.accountGroupID>>>>>>>>>>>> Details;

		public Dictionary<int, PMTask> TaskLookup;
		public Dictionary<BudgetKeyTuple, PMBudget> BudgetLookup;
		private IFinPeriodRepository finPeriodsRepo;
		public virtual IFinPeriodRepository FinPeriodRepository
		{
			get
			{
				if (finPeriodsRepo == null)
				{
					finPeriodsRepo = new FinPeriodRepository(this);
				}

				return finPeriodsRepo;
			}
		}

		public ForecastMaint()
		{
			CopyPaste.SetVisible(false);
		}


		public IEnumerable items()
		{
			PXDelegateResult delResult = new PXDelegateResult();
			delResult.IsResultFiltered = true;
			delResult.IsResultSorted = true;

			if (Revisions.Current != null && Revisions.Current.ProjectID != null && Revisions.Current.RevisionID != null)
			{
				if (PXView.Searches != null && PXView.Searches.Length == 6 && PXView.MaximumRows == 1 && PXView.Searches[0] != null)
				{
					PMForecastRecord single = SelectSingleRecord();
					if (single != null)
						delResult.Add(single);
					delResult.IsResultTruncated = true;
				}
				else
				{
					List<PMForecastRecord> list = new List<PMForecastRecord>(200);

					foreach (Summary summary in GetSummaries())
					{
						list.AddRange(summary.GetList());
					}

					int startIndex = PXView.StartRow;
					if (PXView.ReverseOrder)
					{
						startIndex = Math.Max(0, list.Count + PXView.StartRow);
					}

					int rowsAvailable;
					if (PXView.MaximumRows != 0)
					{
						rowsAvailable = Math.Min(PXView.MaximumRows, list.Count - startIndex);
					}
					else
					{
						rowsAvailable = list.Count - startIndex;
					}

					delResult.AddRange(list.GetRange(startIndex, rowsAvailable));
					delResult.IsResultTruncated = false;
				}
			}

			return delResult;
		}

		private List<Summary> GetSummaries()
		{
			string[] sortColumns = (string[])PXView.SortColumns.Clone();
			for (int i = 0; i < sortColumns.Length; i++)
			{
				if (sortColumns[i] == nameof(PMForecastRecord.ProjectTask))
				{
					sortColumns[i] = nameof(PMForecastRecord.ProjectTaskID);
				}
			}

			PXView budgetView = new PXView(this, false, Budgets.View.BqlSelect);
			int totalRowsBudget = 0;
			int startRow = 0;
			List<object> resultsetBudget = budgetView.Select(null, null, null, sortColumns, PXView.Descendings, null, ref startRow, 0, ref totalRowsBudget);

			Dictionary<BudgetKeyTuple, Summary> groups = new Dictionary<BudgetKeyTuple, Summary>();
			List<Summary> summaries = new List<Summary>(resultsetBudget.Count);
			
			foreach (PMBudgetInfo budget in resultsetBudget)
			{
				var s = new Summary(budget);
				summaries.Add(s);
				groups.Add(budget.GetBudgetKey(), s);
			}

			foreach (PMForecastDetailInfo detail in Details.Select())
			{
				Summary summary = null;
				if (groups.TryGetValue(detail.GetBudgetKey(), out summary))
				{
					summary.Add(detail);
				}
			}

			foreach (PMForecastHistoryInfo history in Actuals.Select())
			{
				Summary summary = null;
				if (groups.TryGetValue(history.GetBudgetKey(), out summary))
				{
					summary.Add(history);
				}
			}

			return summaries;
		}


		private PMForecastRecord SelectSingleRecord()
		{
			PMBudgetInfo singleBudget = Budgets.Search<PMBudgetInfo.projectID, PMBudgetInfo.projectTaskID, PMBudgetInfo.accountGroupID, PMBudgetInfo.inventoryID, PMBudgetInfo.costCodeID>(
					   PXView.Searches[0], PXView.Searches[1], PXView.Searches[2], PXView.Searches[3], PXView.Searches[4]);

			string finPeriodSearch = (string)PXView.Searches[5];

			if (finPeriodSearch == PMForecastRecord.SummaryFinPeriod)
			{
				if (singleBudget != null)
					return Summary.CreateSummaryRecord(singleBudget);
			}
			else if (singleBudget != null && finPeriodSearch != PMForecastRecord.TotalFinPeriod && finPeriodSearch != PMForecastRecord.DifferenceFinPeriod)
			{
				var select = new PXSelect<PMForecastDetailInfo,
					Where<PMForecastDetailInfo.projectID, Equal<Current<PMForecast.projectID>>,
					And<PMForecastDetailInfo.revisionID, Equal<Current<PMForecast.revisionID>>,
					And<PMForecastDetailInfo.projectTaskID, Equal<Required<PMForecastDetailInfo.projectTaskID>>,
					And<PMForecastDetailInfo.accountGroupID, Equal<Required<PMForecastDetailInfo.accountGroupID>>,
					And<PMForecastDetailInfo.inventoryID, Equal<Required<PMForecastDetailInfo.inventoryID>>,
					And<PMForecastDetailInfo.costCodeID, Equal<Required<PMForecastDetailInfo.costCodeID>>,
					And<PMForecastDetailInfo.periodID, Equal<Required<PMForecastDetailInfo.periodID>>>>>>>>>>(this);

				PMForecastDetailInfo singleDetail = select.Select(singleBudget.ProjectTaskID, singleBudget.AccountGroupID, singleBudget.InventoryID, singleBudget.CostCodeID, finPeriodSearch);

				var selectActuals = new PXSelect<PMForecastHistoryInfo,
					Where<PMForecastHistoryInfo.projectID, Equal<Current<PMForecast.projectID>>,
					And<PMForecastHistoryInfo.projectTaskID, Equal<Required<PMForecastDetailInfo.projectTaskID>>,
					And<PMForecastHistoryInfo.accountGroupID, Equal<Required<PMForecastDetailInfo.accountGroupID>>,
					And<PMForecastHistoryInfo.inventoryID, Equal<Required<PMForecastDetailInfo.inventoryID>>,
					And<PMForecastHistoryInfo.costCodeID, Equal<Required<PMForecastDetailInfo.costCodeID>>,
					And<PMForecastHistoryInfo.periodID, Equal<Required<PMForecastDetailInfo.periodID>>>>>>>>>(this);

				PMForecastHistoryInfo singleActuals = selectActuals.Select(singleBudget.ProjectTaskID, singleBudget.AccountGroupID, singleBudget.InventoryID, singleBudget.CostCodeID, finPeriodSearch);
				
				if (singleDetail != null)
					return Summary.CreateDetailRecord(singleDetail, singleBudget, singleActuals);				
			}
			else if (singleBudget != null && finPeriodSearch == PMForecastRecord.DifferenceFinPeriod)
			{
				Summary summary = GetSummaryFromBudget(singleBudget);
				return Summary.CreateDifferenceRecord(singleBudget, summary.GetTotalRecord());				
			}
			else if (singleBudget != null && finPeriodSearch == PMForecastRecord.TotalFinPeriod)
			{
				Summary summary = GetSummaryFromBudget(singleBudget);
				return summary.GetTotalRecord();
			}

			return null;
		}

		#region DAC Attributes Override

		#region PMProject 

		[PXMergeAttributes(Method = MergeMethod.Merge)]
		[PXUIField(DisplayName = "Project Currency", IsReadOnly = true, FieldClass = nameof(FeaturesSet.ProjectMultiCurrency))]
		protected virtual void PMProject_CuryID_CacheAttached(PXCache sender) { }

		#endregion

		#endregion

		public PXAction<PMForecast> addPeriods;
		[PXUIField(DisplayName = "Add Periods", MapEnableRights = PXCacheRights.Update, MapViewRights = PXCacheRights.Select)]
		[PXButton()]
		public virtual IEnumerable AddPeriods(PXAdapter adapter)
		{
			if (Items.Current != null)
			{
				if (AddPeriodDialog.View.Answer == WebDialogResult.None)
				{
					AddPeriodDialog.Cache.Clear();
					PMForecastAddPeriodDialogInfo filterdata = AddPeriodDialog.Cache.Insert() as PMForecastAddPeriodDialogInfo;
				}

				if (AddPeriodDialog.AskExt() != WebDialogResult.OK)
				{
					return adapter.Get();
				}

				PMBudgetInfo budget = GetBudgetInfoFromKey(Items.Current.GetBudgetKey());
				if (budget != null)
				{
					DateTime? startDate = AddPeriodDialog.Current.StartDate;
					DateTime? endDate = AddPeriodDialog.Current.EndDate;

					if (startDate == null)
					{
						startDate = GetMinStartDate(budget);
					}

					if (endDate == null)
					{
						endDate = GetMaxEndDate(budget).GetValueOrDefault(startDate.Value);
					}

					GeneratePeriodsForBudget(budget, startDate.Value, endDate.Value);
				}
			}

			return adapter.Get();
		}

		public PXAction<PMForecast> settleBalances;
		[PXUIField(DisplayName = "Update Project Budget", MapEnableRights = PXCacheRights.Update, MapViewRights = PXCacheRights.Select)]
		[PXButton()]
		public virtual IEnumerable SettleBalances(PXAdapter adapter)
		{
			if (Items.Current != null)
			{			
				PMBudgetInfo budget = GetBudgetInfoFromKey(Items.Current.GetBudgetKey());
				if (budget != null)
				{
					SettleBalancesProc(budget);
				}
			}

			return adapter.Get();
		}

		public PXAction<PMForecast> addMissingLines;
		[PXUIField(DisplayName = "Update Forecast Lines", MapEnableRights = PXCacheRights.Update, MapViewRights = PXCacheRights.Select)]
		[PXButton()]
		public virtual IEnumerable AddMissingLines(PXAdapter adapter)
		{
			if (Items.Current != null)
			{
				PMBudgetInfo budget = GetBudgetInfoFromKey(Items.Current.GetBudgetKey());
				if (budget != null)
				{
					AddMissingLinesProc(budget);
				}
			}

			return adapter.Get();
		}


		public PXAction<PMForecast> generatePeriods;
		[PXUIField(DisplayName = "Generate Periods", MapEnableRights = PXCacheRights.Update, MapViewRights = PXCacheRights.Select)]
		[PXButton()]
		public virtual IEnumerable GeneratePeriods(PXAdapter adapter)
		{
			foreach (PMBudgetInfo budget in Budgets.Select())
			{
				DateTime startDate = GetMinStartDate(budget);
				DateTime endDate = GetMaxEndDate(budget).GetValueOrDefault(startDate);
				if (endDate < startDate)
				{
					startDate = endDate;
				}
				GeneratePeriodsForBudget(budget, startDate, endDate);
			}
			return adapter.Get();
		}

		public PXAction<PMForecast> copyRevision;
		[PXUIField(DisplayName = "Copy Revision", MapEnableRights = PXCacheRights.Update, MapViewRights = PXCacheRights.Select)]
		[PXButton()]
		public virtual IEnumerable CopyRevision(PXAdapter adapter)
		{
			this.Save.Press();

			if (CopyDialog.View.Answer == WebDialogResult.None)
			{
				CopyDialog.Cache.Clear();
				PMForecastCopyDialogInfo filterdata = CopyDialog.Cache.Insert() as PMForecastCopyDialogInfo;
			}

			if (CopyDialog.AskExt() != WebDialogResult.OK || string.IsNullOrEmpty(CopyDialog.Current.RevisionID))
			{
				return adapter.Get();
			}

			return new PMForecast[] { CreateNewRevision(CopyDialog.Current) };
		}

		public PXAction<PMForecast> distribute;
		[PXUIField(DisplayName = "Distribute", MapEnableRights = PXCacheRights.Update, MapViewRights = PXCacheRights.Select)]
		[PXButton()]
		public virtual IEnumerable Distribute(PXAdapter adapter)
		{
			this.Save.Press();

			if (DistributeDialog.View.Answer == WebDialogResult.None)
			{
				DistributeDialog.Cache.Clear();
				DistributeDialog.Cache.Insert();
			}

			if (DistributeDialog.AskExt() == WebDialogResult.OK)
			{
				DistributeForecast(DistributeDialog.Current);
			}

			return adapter.Get();
		}
				
		private void DistributeForecast(PMForecastDistributeDialogInfo filterdata)
		{
			if (filterdata.ApplyOption == PMForecastDistributeDialogInfo.AllRecords)
			{
				foreach (Summary summary in GetSummaries())
				{
					DistributeForecast(filterdata, summary);
				}
			}
			else
			{
				if (Items.Current != null)
				{
					PMBudgetInfo budget = GetBudgetInfoFromKey(Items.Current.GetBudgetKey());
					if (budget != null)
					{
						Summary summary = new Summary(budget);

						var select = new PXSelect<PMForecastDetailInfo,
							Where<PMForecastDetailInfo.projectID, Equal<Required<PMForecastDetailInfo.projectID>>,
							And<PMForecastDetailInfo.revisionID, Equal<Current<PMForecast.revisionID>>,
							And<PMForecastDetailInfo.projectTaskID, Equal<Required<PMForecastDetailInfo.projectTaskID>>,
							And<PMForecastDetailInfo.accountGroupID, Equal<Required<PMForecastDetailInfo.accountGroupID>>,
							And<PMForecastDetailInfo.inventoryID, Equal<Required<PMForecastDetailInfo.inventoryID>>,
							And<PMForecastDetailInfo.costCodeID, Equal<Required<PMForecastDetailInfo.costCodeID>>>>>>>>>(this);

						foreach(PMForecastDetailInfo detail in select.Select(budget.ProjectID, budget.ProjectTaskID, budget.AccountGroupID, budget.InventoryID, budget.CostCodeID))
						{
							summary.Add(detail);
						}

						DistributeForecast(filterdata, summary);
					}
				}
			}
		}

		private void DistributeForecast(PMForecastDistributeDialogInfo filterdata, Summary summary)
		{
			//Redistribution:

			if (filterdata.ValueOption == PMForecastDistributeDialogInfo.Redistribute)
				DistributeForecastRedistribution(filterdata, summary);

			if (filterdata.ValueOption == PMForecastDistributeDialogInfo.AppendVariance)
				DistributeForecastAppendVariance(filterdata, summary);
		}

		private void DistributeForecastRedistribution(PMForecastDistributeDialogInfo filterdata, Summary summary)
		{
			if (summary.Details.Count == 0)
				return;

			decimal qtyLast = summary.Budget.Qty.GetValueOrDefault();
			decimal revisedQtyLast = summary.Budget.RevisedQty.GetValueOrDefault();
			decimal amtLast = summary.Budget.CuryAmount.GetValueOrDefault();
			decimal revisedAmtLast = summary.Budget.CuryRevisedAmount.GetValueOrDefault();
						
			decimal qtyRaw = summary.Budget.Qty.GetValueOrDefault() / summary.Details.Count;
			decimal revisedQtyRaw = summary.Budget.RevisedQty.GetValueOrDefault() / summary.Details.Count;
			decimal amtRaw = summary.Budget.CuryAmount.GetValueOrDefault() / summary.Details.Count;
			decimal revisedAmtRaw = summary.Budget.CuryRevisedAmount.GetValueOrDefault() / summary.Details.Count;

			decimal qty = PXDBQuantityAttribute.Round(DistibuteRound(qtyRaw, summary.Details.Count - 1));
			decimal revisedQty = PXDBQuantityAttribute.Round(DistibuteRound(revisedQtyRaw, summary.Details.Count - 1));
			decimal amt = PXCurrencyAttribute.BaseRound(this, DistibuteRound(amtRaw, summary.Details.Count - 1));
			decimal revisedAmt = PXCurrencyAttribute.BaseRound(this, DistibuteRound(revisedAmtRaw, summary.Details.Count - 1));

			for (int i = 0; i < summary.Details.Count - 1; i++)
			{
				if (filterdata.Qty == true && filterdata.ValueOption == PMForecastDistributeDialogInfo.Redistribute)
				{
					summary.Details[i].Qty = qty;
				}

				if (filterdata.RevisedQty == true && filterdata.ValueOption == PMForecastDistributeDialogInfo.Redistribute)
				{
					summary.Details[i].RevisedQty = revisedQty;
				}

				if (filterdata.Amount == true && filterdata.ValueOption == PMForecastDistributeDialogInfo.Redistribute)
				{
					summary.Details[i].CuryAmount = amt;
				}

				if (filterdata.RevisedAmount == true && filterdata.ValueOption == PMForecastDistributeDialogInfo.Redistribute)
				{
					summary.Details[i].CuryRevisedAmount = revisedAmt;
				}

				Details.Update(summary.Details[i]);

				qtyLast -= qty;
				revisedQtyLast -= revisedQty;
				amtLast -= amt;
				revisedAmtLast -= revisedAmt;


			}

			if (filterdata.Qty == true && filterdata.ValueOption == PMForecastDistributeDialogInfo.Redistribute)
			{
				summary.Details[summary.Details.Count - 1].Qty = qtyLast;
			}

			if (filterdata.RevisedQty == true && filterdata.ValueOption == PMForecastDistributeDialogInfo.Redistribute)
			{
				summary.Details[summary.Details.Count - 1].RevisedQty = revisedQtyLast;
			}

			if (filterdata.Amount == true && filterdata.ValueOption == PMForecastDistributeDialogInfo.Redistribute)
			{
				summary.Details[summary.Details.Count - 1].CuryAmount = amtLast;
			}

			if (filterdata.RevisedAmount == true && filterdata.ValueOption == PMForecastDistributeDialogInfo.Redistribute)
			{
				summary.Details[summary.Details.Count - 1].CuryRevisedAmount = revisedAmtLast;
			}

			Details.Update(summary.Details[summary.Details.Count - 1]);
		}

		private void DistributeForecastAppendVariance(PMForecastDistributeDialogInfo filterdata, Summary summary)
		{
			List<PMForecastRecord> records = summary.GetList();
			PMForecastRecord delta = records[records.Count - 1];
			if (records.Count == 0)
				return;

			if (!delta.IsDifference)
				return;

			decimal qtyLast = summary.Budget.Qty.GetValueOrDefault();
			decimal revisedQtyLast = summary.Budget.RevisedQty.GetValueOrDefault();
			decimal amtLast = summary.Budget.CuryAmount.GetValueOrDefault();
			decimal revisedAmtLast = summary.Budget.CuryRevisedAmount.GetValueOrDefault();

			for (int i = 0; i < summary.Details.Count - 1; i++)
			{
				decimal qtyRaw = 0;
				if (summary.Budget.Qty.GetValueOrDefault() != 0)
					qtyRaw = summary.Details[i].Qty.GetValueOrDefault() + summary.Details[i].Qty.GetValueOrDefault() * delta.Qty.GetValueOrDefault() / summary.Budget.Qty.GetValueOrDefault();

				decimal revisedQtyRaw = 0;
				if (summary.Budget.RevisedQty.GetValueOrDefault() != 0)
					revisedQtyRaw = summary.Details[i].RevisedQty.GetValueOrDefault() + summary.Details[i].RevisedQty.GetValueOrDefault() * delta.RevisedQty.GetValueOrDefault() / summary.Budget.RevisedQty.GetValueOrDefault();

				decimal amtRaw = 0;
				if (summary.Budget.CuryAmount.GetValueOrDefault() != 0)
					amtRaw = summary.Details[i].CuryAmount.GetValueOrDefault() + summary.Details[i].CuryAmount.GetValueOrDefault() * delta.CuryAmount.GetValueOrDefault() / summary.Budget.CuryAmount.GetValueOrDefault();

				decimal revisedAmtRaw = 0;
				if (summary.Budget.CuryRevisedAmount.GetValueOrDefault() != 0)
					revisedAmtRaw = summary.Details[i].CuryRevisedAmount.GetValueOrDefault() + summary.Details[i].CuryRevisedAmount.GetValueOrDefault() * delta.CuryRevisedAmount.GetValueOrDefault() / summary.Budget.CuryRevisedAmount.GetValueOrDefault();

				decimal qty = PXDBQuantityAttribute.Round(DistibuteRound(qtyRaw, summary.Details.Count - 1));
				decimal revisedQty = PXDBQuantityAttribute.Round(DistibuteRound(revisedQtyRaw, summary.Details.Count - 1));
				decimal amt = PXCurrencyAttribute.BaseRound(this, DistibuteRound(amtRaw, summary.Details.Count - 1));
				decimal revisedAmt = PXCurrencyAttribute.BaseRound(this, DistibuteRound(revisedAmtRaw, summary.Details.Count - 1));
				
				if (filterdata.Qty == true && filterdata.ValueOption == PMForecastDistributeDialogInfo.AppendVariance)
				{
					summary.Details[i].Qty = qty;
				}

				if (filterdata.RevisedQty == true && filterdata.ValueOption == PMForecastDistributeDialogInfo.AppendVariance)
				{
					summary.Details[i].RevisedQty = revisedQty;
				}

				if (filterdata.Amount == true && filterdata.ValueOption == PMForecastDistributeDialogInfo.AppendVariance)
				{
					summary.Details[i].CuryAmount = amt;
				}

				if (filterdata.RevisedAmount == true && filterdata.ValueOption == PMForecastDistributeDialogInfo.AppendVariance)
				{
					summary.Details[i].CuryRevisedAmount = revisedAmt;
				}

				Details.Update(summary.Details[i]);

				qtyLast -= qty;
				revisedQtyLast -= revisedQty;
				amtLast -= amt;
				revisedAmtLast -= revisedAmt;
			}

			if (filterdata.Qty == true && filterdata.ValueOption == PMForecastDistributeDialogInfo.AppendVariance)
			{
				summary.Details[summary.Details.Count - 1].Qty = qtyLast;
			}

			if (filterdata.RevisedQty == true && filterdata.ValueOption == PMForecastDistributeDialogInfo.AppendVariance)
			{
				summary.Details[summary.Details.Count - 1].RevisedQty = revisedQtyLast;
			}

			if (filterdata.Amount == true && filterdata.ValueOption == PMForecastDistributeDialogInfo.AppendVariance)
			{
				summary.Details[summary.Details.Count - 1].CuryAmount = amtLast;
			}

			if (filterdata.RevisedAmount == true && filterdata.ValueOption == PMForecastDistributeDialogInfo.AppendVariance)
			{
				summary.Details[summary.Details.Count - 1].CuryRevisedAmount = revisedAmtLast;
			}

			Details.Update(summary.Details[summary.Details.Count - 1]);
		}
				
		private decimal DistibuteRound(decimal value, int rowCount)
		{
			int valDigit = (int)Math.Log10((double)value);
			int rowDigit = (int)Math.Log10((double)rowCount);
			int roundIndex = rowDigit - valDigit + 1;
			if (rowCount > 1 && value > 0)
			{
				if (roundIndex >= 0)
				{
					return Math.Round(value, roundIndex);
				}
				else
				{
					return Math.Round(value / (decimal)Math.Pow(10, -roundIndex)) * (decimal)Math.Pow(10, -roundIndex);
				}
			}
			else
			{
				return value;
			}
		}

		private DateTime GetMinStartDate(PMBudgetInfo row)
		{
			DateTime? minDate = row.PlannedStartDate;

			var selectFirstTran = new PXSelect<PMTran, Where<PMTran.taskID, Equal<Required<PMTran.taskID>>>, OrderBy<Asc<PMTran.date>>>(this);

			PMTran first = selectFirstTran.SelectWindowed(0, 1, row.ProjectTaskID);
			if (first != null)
			{
				if (minDate == null)
				{
					minDate = first.Date;
				}
				else if (first.Date < minDate)
				{
					minDate = first.Date;
				}
			}
			else if (minDate == null)
			{
				PMTask task = PXSelect<PMTask, Where<PMTask.taskID, Equal<Required<PMTask.taskID>>>>.Select(this, row.ProjectTaskID);
				if (task != null && task.StartDate != null)
				{
					minDate = task.StartDate;
				}

				if (minDate == null)
				{
					PMProject project = Project.Select();
					if (project != null)
					{
						minDate = project.StartDate;
					}
				}
			}

			if (minDate == null)
			{
				minDate = Accessinfo.BusinessDate.GetValueOrDefault(DateTime.Now);
			}

			return minDate.Value;
		}

		private DateTime? GetMaxEndDate(PMBudgetInfo row)
		{
			DateTime? maxDate = row.PlannedEndDate;

			var selectLastTran = new PXSelect<PMTran, Where<PMTran.taskID, Equal<Required<PMTran.taskID>>>, OrderBy<Desc<PMTran.date>>>(this);

			PMTran last = selectLastTran.SelectWindowed(0, 1, row.ProjectTaskID);
			if (last != null)
			{
				if (maxDate == null)
				{
					maxDate = last.Date;
				}
				else if (last.Date > maxDate)
				{
					maxDate = last.Date;
				}
			}

			return maxDate;
		}

		private void GeneratePeriodsForBudget(PMBudgetInfo row, DateTime startDate, DateTime endDate)
		{
			var selectDetails = new PXSelect<PMForecastDetailInfo,
				Where<PMForecastDetailInfo.projectID, Equal<Required<PMForecastDetailInfo.projectID>>,
				And<PMForecastDetailInfo.revisionID, Equal<Current<PMForecast.revisionID>>,
				And<PMForecastDetailInfo.projectTaskID, Equal<Required<PMForecastDetailInfo.projectTaskID>>,
				And<PMForecastDetailInfo.accountGroupID, Equal<Required<PMForecastDetailInfo.accountGroupID>>,
				And<PMForecastDetailInfo.inventoryID, Equal<Required<PMForecastDetailInfo.inventoryID>>,
				And<PMForecastDetailInfo.costCodeID, Equal<Required<PMForecastDetailInfo.costCodeID>>>>>>>>>(this);
			HashSet<string> existing = new HashSet<string>();

			foreach (PMForecastDetailInfo detail in selectDetails.Select(row.ProjectID, row.ProjectTaskID, row.AccountGroupID, row.InventoryID, row.CostCodeID))
			{
				existing.Add(detail.PeriodID);
			}


			foreach (FinPeriod period in FinPeriodRepository.PeriodsBetweenInclusive(startDate, endDate, FinPeriod.organizationID.MasterValue))
			{
				if (!existing.Contains(period.FinPeriodID))
				{
					PMForecastDetailInfo record = new PMForecastDetailInfo();
					record.RevisionID = Revisions.Current.RevisionID;
					record.ProjectID = row.ProjectID;
					record.ProjectTaskID = row.ProjectTaskID;
					record.AccountGroupID = row.AccountGroupID;
					record.InventoryID = row.InventoryID;
					record.CostCodeID = row.CostCodeID;
					record.PeriodID = period.FinPeriodID;
					record.AccountGroupType = row.AccountGroupType;

					Details.Insert(record);
				}
			}
		}

		private void SettleBalancesProc(PMBudgetInfo row)
		{
			Summary summary = GetSummaryFromBudget(row);
			PMProject project = Project.Select();
			PMBudget delta = summary.GetDelta();
			bool hasChanges = false;
			if (project?.BudgetFinalized != true && delta.Qty.GetValueOrDefault() != 0)
			{
				row.Qty += delta.Qty.Value;
				hasChanges = true;
			}
			if (project?.BudgetFinalized != true && delta.CuryAmount.GetValueOrDefault() != 0)
			{
				row.CuryAmount += delta.CuryAmount.Value;
				hasChanges = true;
			}
			if (project?.ChangeOrderWorkflow != true && delta.RevisedQty.GetValueOrDefault() != 0)
			{
				row.RevisedQty += delta.RevisedQty.Value;
				hasChanges = true;
			}
			if (project?.ChangeOrderWorkflow != true && delta.CuryRevisedAmount.GetValueOrDefault() != 0)
			{
				row.CuryRevisedAmount += delta.CuryRevisedAmount.Value;
				hasChanges = true;
			}

			if (hasChanges)
			{
				Budgets.Update(row);
			}
		}

		private void AddMissingLinesProc(PMBudgetInfo row)
		{
			Summary summary = GetSummaryFromBudget(row);

			foreach (string finPeriod in summary.GetMissingPeriods())
			{
				PMForecastDetailInfo newDetail = new PMForecastDetailInfo();
				newDetail.ProjectID = Revisions.Current.ProjectID;
				newDetail.RevisionID = Revisions.Current.RevisionID;
				newDetail.ProjectTaskID = row.ProjectTaskID;
				newDetail.AccountGroupID = row.AccountGroupID;
				newDetail.AccountGroupType = row.AccountGroupType;
				newDetail.InventoryID = row.InventoryID;
				newDetail.CostCodeID = row.CostCodeID;
				newDetail.PeriodID = finPeriod;

				Details.Insert(newDetail);
			}
		}

		private Summary GetSummaryFromBudget(PMBudgetInfo row)
		{
			Summary summary = new Summary(row);

			var select = new PXSelect<PMForecastDetailInfo,
					Where<PMForecastDetailInfo.projectID, Equal<Current<PMForecast.projectID>>,
					And<PMForecastDetailInfo.revisionID, Equal<Current<PMForecast.revisionID>>,
					And<PMForecastDetailInfo.projectTaskID, Equal<Required<PMForecastDetailInfo.projectTaskID>>,
					And<PMForecastDetailInfo.accountGroupID, Equal<Required<PMForecastDetailInfo.accountGroupID>>,
					And<PMForecastDetailInfo.inventoryID, Equal<Required<PMForecastDetailInfo.inventoryID>>,
					And<PMForecastDetailInfo.costCodeID, Equal<Required<PMForecastDetailInfo.costCodeID>>>>>>>>>(this);

			foreach (PMForecastDetailInfo detail in select.Select(row.ProjectTaskID, row.AccountGroupID, row.InventoryID, row.CostCodeID))
			{
				summary.Add(detail);
			}

			var selectActuals = new PXSelect<PMForecastHistoryInfo,
				Where<PMForecastHistoryInfo.projectID, Equal<Current<PMForecast.projectID>>,
				And<PMForecastHistoryInfo.projectTaskID, Equal<Required<PMForecastDetailInfo.projectTaskID>>,
				And<PMForecastHistoryInfo.accountGroupID, Equal<Required<PMForecastDetailInfo.accountGroupID>>,
				And<PMForecastHistoryInfo.inventoryID, Equal<Required<PMForecastDetailInfo.inventoryID>>,
				And<PMForecastHistoryInfo.costCodeID, Equal<Required<PMForecastDetailInfo.costCodeID>>>>>>>>(this);

			foreach (PMForecastHistoryInfo history in selectActuals.Select(row.ProjectTaskID, row.AccountGroupID, row.InventoryID, row.CostCodeID))
			{
				summary.Add(history);
			}

			return summary;
		}

		private PMForecast CreateNewRevision(PMForecastCopyDialogInfo info)
		{
			var select = new PXSelect<PMForecastDetailInfo,
				Where<PMForecastDetailInfo.projectID, Equal<Current<PMForecast.projectID>>,
			And<PMForecastDetailInfo.revisionID, Equal<Required<PMForecast.revisionID>>>>>(this);

			string sourceRevision = Revisions.Current.RevisionID;

			PMForecast newRevision = Revisions.Cache.CreateCopy(Revisions.Current) as PMForecast;
			newRevision.RevisionID = info.RevisionID;

			newRevision = Revisions.Insert(newRevision);

			foreach (PMForecastDetailInfo item in select.Select(sourceRevision))
			{
				item.RevisionID = newRevision.RevisionID;
				Details.Insert(item);
			}

			return newRevision;
		}
				
		protected virtual void _(Events.RowUpdated<PMForecastRecord> e)
		{
			//Import 
			//Note: Since this is a virtual DAC import of new records will always be via Update. There will never be an Insert
			//event because of how the Cache.Locate is implemented for Virtual Dacs. 

			PMForecastDetailInfo detail = GetDetailInfo(e.Row);
			if (detail != null)
			{
				if (e.Row.Qty != null)
				{
					detail.Qty = e.Row.Qty;
				}

				if (e.Row.CuryAmount != null)
				{
					detail.CuryAmount = e.Row.CuryAmount;
				}

				if (e.Row.RevisedQty != null)
				{
					detail.RevisedQty = e.Row.RevisedQty;
				}

				if (e.Row.CuryRevisedAmount != null)
				{
					detail.CuryRevisedAmount = e.Row.CuryRevisedAmount;
				}

				Details.Update(detail);
			}
			else
			{
				PMAccountGroup accountGroup = PXSelect<PMAccountGroup, Where<PMAccountGroup.groupID, Equal<Required<PMAccountGroup.groupID>>>>.Select(this, e.Row.AccountGroupID);

				detail = new PMForecastDetailInfo();
				detail.RevisionID = Revisions.Current.RevisionID;
				detail.ProjectID = e.Row.ProjectID;
				detail.ProjectTaskID = e.Row.ProjectTaskID;
				detail.AccountGroupID = e.Row.AccountGroupID;
				detail.InventoryID = e.Row.InventoryID;
				detail.CostCodeID = e.Row.CostCodeID;
				detail.PeriodID = e.Row.FinPeriodID;
				detail.AccountGroupType = accountGroup.Type;
				detail.Qty = e.Row.Qty;
				detail.CuryAmount = e.Row.CuryAmount;
				detail.RevisedQty = e.Row.RevisedQty;
				detail.CuryRevisedAmount = e.Row.CuryRevisedAmount;
				Details.Insert(detail);
			}
		}

		protected virtual void _(Events.RowUpdated<PMForecastDetailInfo> e)
		{
			Items.View.RequestRefresh();
		}

		protected virtual void _(Events.RowSelected<PMForecastRecord> e)
		{
			PXUIFieldAttribute.SetEnabled<PMForecastRecord.projectTask>(e.Cache, e.Row, e.Row == null);
			PXUIFieldAttribute.SetEnabled<PMForecastRecord.accountGroup>(e.Cache, e.Row, e.Row == null);
			PXUIFieldAttribute.SetEnabled<PMForecastRecord.inventory>(e.Cache, e.Row, e.Row == null);
			PXUIFieldAttribute.SetEnabled<PMForecastRecord.costCode>(e.Cache, e.Row, e.Row == null);
			PXUIFieldAttribute.SetEnabled<PMForecastRecord.period>(e.Cache, e.Row, e.Row == null);

			PMProject project = Project.Select();
			if (e.Row != null)
			{
				PXUIFieldAttribute.SetEnabled<PMForecastRecord.qty>(e.Cache, e.Row, !e.Row.IsSummary && !e.Row.IsTotal && !e.Row.IsDifference);
				PXUIFieldAttribute.SetEnabled<PMForecastRecord.curyAmount>(e.Cache, e.Row, !e.Row.IsSummary && !e.Row.IsTotal && !e.Row.IsDifference);
				PXUIFieldAttribute.SetEnabled<PMForecastRecord.revisedQty>(e.Cache, e.Row, !e.Row.IsSummary && !e.Row.IsTotal && !e.Row.IsDifference);
				PXUIFieldAttribute.SetEnabled<PMForecastRecord.curyRevisedAmount>(e.Cache, e.Row, !e.Row.IsSummary && !e.Row.IsTotal && !e.Row.IsDifference);
				PXUIFieldAttribute.SetEnabled<PMForecastRecord.description>(e.Cache, e.Row, !e.Row.IsSummary && !e.Row.IsTotal && !e.Row.IsDifference);

				if (e.Row.IsDifference)
				{
					PXUIFieldAttribute.SetWarning<PMForecastRecord.qty>(e.Cache, e.Row, null);
					PXUIFieldAttribute.SetWarning<PMForecastRecord.curyAmount>(e.Cache, e.Row, null);
					PXUIFieldAttribute.SetWarning<PMForecastRecord.revisedQty>(e.Cache, e.Row, null);
					PXUIFieldAttribute.SetWarning<PMForecastRecord.curyRevisedAmount>(e.Cache, e.Row, null);
					if (project.BudgetFinalized == true)
					{
						if (e.Row.Qty != 0)
						{
							PXUIFieldAttribute.SetWarning<PMForecastRecord.qty>(e.Cache, e.Row, Messages.LockedBudgetLineCannotBeUpdated);
						}
						if (e.Row.CuryAmount != 0)
						{
							PXUIFieldAttribute.SetWarning<PMForecastRecord.curyAmount>(e.Cache, e.Row, Messages.LockedBudgetLineCannotBeUpdated);
						}
					}

					if (project.ChangeOrderWorkflow == true)
					{
						if (e.Row.RevisedQty != 0)
						{
							PXUIFieldAttribute.SetWarning<PMForecastRecord.revisedQty>(e.Cache, e.Row, Messages.ChangeOrderWorkflowLineCannotBeUpdated);
						}
						if (e.Row.CuryRevisedAmount != 0)
						{
							PXUIFieldAttribute.SetWarning<PMForecastRecord.curyRevisedAmount>(e.Cache, e.Row, Messages.ChangeOrderWorkflowLineCannotBeUpdated);
						}
					}
				}
			}

			

		}

		protected virtual void _(Events.RowSelected<PMForecastFilter> e)
		{
			if (e.Row != null && e.Row.ProjectID != null)
			{
				PMProject project = Project.Select();
				if (project != null && project.BudgetFinalized == true && project.ChangeOrderWorkflow == true)
				{
					settleBalances.SetEnabled(false);
				}
				else
				{
					settleBalances.SetEnabled(true);
				}
			}
		}

		protected virtual void _(Events.RowDeleting<PMForecastRecord> e)
		{
			e.Cancel = e.Row.IsSummary || e.Row.IsTotal || e.Row.IsDifference;
		}

		protected virtual void _(Events.RowDeleted<PMForecastRecord> e)
		{
			PMForecastDetailInfo detail = GetDetailInfo(e.Row);
			if (detail != null)
			{
				Details.Delete(detail);
			}
		}

		protected virtual void _(Events.RowPersisting<PMForecastRecord> e)
		{
			e.Cancel = true;
		}


		protected virtual void _(Events.FieldDefaulting<PMForecastAddPeriodDialogInfo, PMForecastAddPeriodDialogInfo.startPeriodID> e)
		{
			if (Items.Current != null)
			{
				PMBudgetInfo budget = GetBudgetInfoFromKey(Items.Current.GetBudgetKey());
				if (budget != null)
				{
					DateTime? date = GetMinStartDate(budget);
					if (date != null)
					{
						var finPeriod = FinPeriodRepository.FindFinPeriodByDate(date, FinPeriod.organizationID.MasterValue);
						if (finPeriod != null)
						{
							e.NewValue = FinPeriodIDAttribute.FormatForDisplay(finPeriod.FinPeriodID);
						}
					}
				}
			}
		}

		protected virtual void _(Events.FieldDefaulting<PMForecastAddPeriodDialogInfo, PMForecastAddPeriodDialogInfo.endPeriodID> e)
		{
			if (Items.Current != null)
			{
				PMBudgetInfo budget = GetBudgetInfoFromKey(Items.Current.GetBudgetKey());
				{
					DateTime? date = GetMaxEndDate(budget);
					if (date != null)
					{
						var finPeriod = FinPeriodRepository.FindFinPeriodByDate(date, FinPeriod.organizationID.MasterValue);
						if (finPeriod != null)
						{
							e.NewValue = FinPeriodIDAttribute.FormatForDisplay(finPeriod.FinPeriodID);
						}
					}
				}
			}
		}

		protected virtual void _(Events.FieldUpdated<PMForecastAddPeriodDialogInfo, PMForecastAddPeriodDialogInfo.startPeriodID> e)
		{
			e.Row.StartDate = null;
			e.Cache.SetDefaultExt<PMForecastAddPeriodDialogInfo.startDate>(e.Row);
		}

		protected virtual void _(Events.FieldUpdated<PMForecastAddPeriodDialogInfo, PMForecastAddPeriodDialogInfo.endPeriodID> e)
		{
			e.Row.EndDate = null;
			e.Cache.SetDefaultExt<PMForecastAddPeriodDialogInfo.endDate>(e.Row);
		}

		protected virtual void _(Events.FieldDefaulting<PMForecastAddPeriodDialogInfo, PMForecastAddPeriodDialogInfo.startDate> e)
		{
			if (!string.IsNullOrEmpty(e.Row.StartPeriodID))
			{
				e.NewValue = FinPeriodRepository.FindByID(FinPeriod.organizationID.MasterValue, e.Row.StartPeriodID).StartDate;
			}
		}

		protected virtual void _(Events.FieldDefaulting<PMForecastAddPeriodDialogInfo, PMForecastAddPeriodDialogInfo.endDate> e)
		{
			if (!string.IsNullOrEmpty(e.Row.EndPeriodID))
			{
				e.NewValue = FinPeriodRepository.FindByID(FinPeriod.organizationID.MasterValue, e.Row.EndPeriodID).EndDateUI;
			}
		}

		protected virtual void _(Events.FieldVerifying<PMForecastCopyDialogInfo, PMForecastCopyDialogInfo.revisionID> e)
		{
			var select = new PXSelect<PMForecast, Where<PMForecast.projectID, Equal<Current<PMForecast.projectID>>,
				And<PMForecast.revisionID, Equal<Required<PMForecast.revisionID>>>>>(this);

			PMForecast duplicate = select.Select(e.NewValue);

			if (duplicate != null)
			{
				throw new PXSetPropertyException<PMForecast.revisionID>("Duplicate ID");
			}
		}

		protected virtual void _(Events.FieldDefaulting<PMForecastDistributeDialogInfo, PMForecastDistributeDialogInfo.qty> e)
		{
			PMProject project = Project.Select();
			if (project != null )
			{
				e.NewValue = project.BudgetFinalized != true;
			}
		}

		protected virtual void _(Events.FieldDefaulting<PMForecastDistributeDialogInfo, PMForecastDistributeDialogInfo.amount> e)
		{
			PMProject project = Project.Select();
			if (project != null)
			{
				e.NewValue = project.BudgetFinalized != true;
			}
		}

		private PMBudgetInfo GetBudgetInfoFromKey(BudgetKeyTuple key)
		{
			var selectBudgetInfo = new PXSelect<PMBudgetInfo,
						Where<PMBudgetInfo.projectID, Equal<Required<PMBudgetInfo.projectID>>,
						And<PMBudgetInfo.projectTaskID, Equal<Required<PMBudgetInfo.projectTaskID>>,
						And<PMBudgetInfo.accountGroupID, Equal<Required<PMBudgetInfo.accountGroupID>>,
						And<PMBudgetInfo.inventoryID, Equal<Required<PMBudgetInfo.inventoryID>>,
						And<PMBudgetInfo.costCodeID, Equal<Required<PMBudgetInfo.costCodeID>>>>>>>>(this);

			return selectBudgetInfo.Select(key.ProjectID, key.ProjectTaskID, key.AccountGroupID, key.InventoryID, key.CostCodeID);
		}


		private PMForecastDetailInfo GetPMForecastDetailInfoKey(PMForecastRecord row)
		{
			PMForecastDetailInfo key = new PMForecastDetailInfo();
			key.RevisionID = Revisions.Current.RevisionID;
			key.ProjectID = row.ProjectID;
			key.ProjectTaskID = row.ProjectTaskID;
			key.AccountGroupID = row.AccountGroupID;
			key.InventoryID = row.InventoryID;
			key.CostCodeID = row.CostCodeID;
			key.PeriodID = row.FinPeriodID;
			key.AccountGroupType = row.AccountGroupType;

			return key;
		}

		private PMForecastDetailInfo GetDetailInfo(PMForecastRecord row)
		{
			var select = new PXSelect<PMForecastDetailInfo,
				Where<PMForecastDetailInfo.projectID, Equal<Required<PMForecastDetailInfo.projectID>>,
				And<PMForecastDetailInfo.revisionID, Equal<Current<PMForecast.revisionID>>,
				And<PMForecastDetailInfo.projectTaskID, Equal<Required<PMForecastDetailInfo.projectTaskID>>,
				And<PMForecastDetailInfo.accountGroupID, Equal<Required<PMForecastDetailInfo.accountGroupID>>,
				And<PMForecastDetailInfo.inventoryID, Equal<Required<PMForecastDetailInfo.inventoryID>>,
				And<PMForecastDetailInfo.costCodeID, Equal<Required<PMForecastDetailInfo.costCodeID>>,
				And<PMForecastDetailInfo.periodID, Equal<Required<PMForecastDetailInfo.periodID>>>>>>>>>>(this);

			PMForecastDetailInfo detail = select.Select(row.ProjectID, row.ProjectTaskID, row.AccountGroupID, row.InventoryID, row.CostCodeID, row.FinPeriodID);

			return detail;
		}
		
		#region PMImport Implementation

		string projectTaskID;
		string accountGroupID;
		string inventoryID;
		string costCodeID;

		public bool PrepareImportRow(string viewName, IDictionary keys, IDictionary values)
		{
			if (viewName == nameof(Items))
			{
				string finPeriod = (string)values[nameof(PMForecastRecord.Period)];
				if (string.IsNullOrEmpty(finPeriod))
				{
					projectTaskID = (string)values[nameof(PMForecastRecord.ProjectTask)];
					accountGroupID = (string)values[nameof(PMForecastRecord.AccountGroup)];
					inventoryID = (string)values[nameof(PMForecastRecord.Inventory)];
					costCodeID = (string)values[nameof(PMForecastRecord.CostCode)];

					return false;
				}
				else if (finPeriod == "Total:" || finPeriod == "Delta:")
				{
					projectTaskID = null;
					accountGroupID = null;
					inventoryID = null;
					costCodeID = null;

					return false;
				}
				else
				{
					PMCostCode defaultCostCode = PXSelect<PMCostCode, Where<PMCostCode.isDefault, Equal<True>>>.Select(this);

					string[] parts = finPeriod.Split('-');
					keys[nameof(PMForecastRecord.ProjectTaskID)] = projectTaskID;
					keys[nameof(PMForecastRecord.AccountGroupID)] = accountGroupID;
					keys[nameof(PMForecastRecord.InventoryID)] = inventoryID;
					keys[nameof(PMForecastRecord.CostCodeID)] = costCodeID ?? defaultCostCode.CostCodeCD;
					keys[nameof(PMForecastRecord.FinPeriodID)] = parts[1] + parts[0];

					return true;
				}
			}

			return true;
		}

		public bool RowImporting(string viewName, object row)
		{
			return row == null;
		}

		public bool RowImported(string viewName, object row, object oldRow)
		{
			return oldRow == null;
		}

		public void PrepareItems(string viewName, IEnumerable items)
		{
		}
		#endregion

		#region Local Types

		public class Summary
		{
			public PMBudgetInfo Budget { get; private set; }
			private SortedList<string, PMForecastDetailInfo> details;
			private SortedList<string, PMForecastHistoryInfo> actuals;
			private SortedList<string, PMForecastHistoryInfo> missingActuals;
			private decimal totalQty = 0;
			private decimal curyTotalAmount = 0;
			private decimal totalAmount = 0;
			private decimal totalRevisedQty = 0;
			private decimal curyTotalRevisedAmount = 0;
			private decimal totalRevisedAmount = 0;
			private decimal totalChangeOrderQty = 0;
			private decimal curyTotalChangeOrderAmount = 0;
			private decimal totalActualQty = 0;
			private decimal curyTotalActualAmount = 0;
			private decimal totalActualAmount = 0;

			public Summary(PMBudgetInfo budget)
			{
				this.Budget = budget;
				this.details = new SortedList<string, PMForecastDetailInfo>();
				this.actuals = new SortedList<string, PMForecastHistoryInfo>();
				this.missingActuals = new SortedList<string, PMForecastHistoryInfo>();
			}

			public void Add(PMForecastDetailInfo detail)
			{
				details.Add(detail.PeriodID, detail);

				totalQty += detail.Qty.GetValueOrDefault();
				curyTotalAmount += detail.CuryAmount.GetValueOrDefault();
				totalRevisedQty += detail.RevisedQty.GetValueOrDefault();
				curyTotalRevisedAmount += detail.CuryRevisedAmount.GetValueOrDefault();
			}

			public void Add(PMForecastHistoryInfo history)
			{
				if (details.ContainsKey(history.PeriodID))
				{
					actuals.Add(history.PeriodID, history);
					totalChangeOrderQty += history.ChangeOrderQty.GetValueOrDefault();
					curyTotalChangeOrderAmount += history.CuryChangeOrderAmount.GetValueOrDefault();
					totalActualQty += history.ActualQty.GetValueOrDefault();
					curyTotalActualAmount += history.CuryActualAmount.GetValueOrDefault();
					totalActualAmount += history.ActualAmount.GetValueOrDefault();
				}
				else
				{
					missingActuals.Add(history.PeriodID, history);
				}
			}

			public List<PMForecastRecord> GetList()
			{
				List<PMForecastRecord> list = new List<PMForecastRecord>();

				list.Add(CreateSummaryRecord(Budget));

				

				foreach (PMForecastDetail detail in details.Values)
				{
					PMForecastHistoryInfo history = null;
					actuals.TryGetValue(detail.PeriodID, out history);
					list.Add(CreateDetailRecord(detail, Budget, history));
				}

				if (details.Count > 0)
				{
					PMForecastRecord totalRecord = GetTotalRecord();
					list.Add(totalRecord);

					var diff = CreateDifferenceRecord(Budget, totalRecord);
					if (diff.Qty != 0 || diff.CuryAmount != 0 || diff.RevisedQty != 0 || diff.CuryRevisedAmount != 0
						|| diff.ActualQty != 0 || diff.CuryActualAmount != 0 || diff.ChangeOrderQty != 0 || diff.CuryChangeOrderAmount != 0)
					{
						list.Add(diff);
					}
				}

				return list;
			}

			public PMForecastRecord GetTotalRecord()
			{
				PMForecastRecord totalRecord = CreateTotalRecord(Budget);
				totalRecord.Qty = totalQty;
				totalRecord.CuryAmount = curyTotalAmount;
				totalRecord.RevisedQty = totalRevisedQty;
				totalRecord.CuryRevisedAmount = curyTotalRevisedAmount;
				totalRecord.ChangeOrderQty = totalChangeOrderQty;
				totalRecord.CuryChangeOrderAmount = curyTotalChangeOrderAmount;
				totalRecord.ActualQty = totalActualQty;
				totalRecord.CuryActualAmount = curyTotalActualAmount;
				totalRecord.ActualAmount = totalActualAmount;

				return totalRecord;
			}


			public IList<PMForecastDetailInfo> Details
			{
				get { return details.Values;  }
			}

			public IList<string> GetMissingPeriods()
			{
				return missingActuals.Keys;
			}

			public PMBudget GetDelta()
			{
				PMBudget delta = new PMBudget();
				delta.Qty = totalQty - Budget.Qty.GetValueOrDefault();
				delta.CuryAmount = curyTotalAmount - Budget.CuryAmount.GetValueOrDefault();
				delta.Amount = totalAmount - Budget.Amount.GetValueOrDefault();
				delta.RevisedQty = totalRevisedQty - Budget.RevisedQty.GetValueOrDefault();
				delta.CuryRevisedAmount = curyTotalRevisedAmount - Budget.CuryRevisedAmount.GetValueOrDefault();
				delta.RevisedAmount = totalRevisedAmount - Budget.RevisedAmount.GetValueOrDefault();

				return delta;
			}


			public static PMForecastRecord CreateSummaryRecord(PMBudgetInfo budget)
			{
				PMForecastRecord record = new PMForecastRecord();
				//Keys:
				record.ProjectID = budget.ProjectID;
				record.ProjectTaskID = budget.ProjectTaskID;
				record.AccountGroupID = budget.AccountGroupID;
				record.InventoryID = budget.InventoryID;
				record.CostCodeID = budget.CostCodeID;
				record.FinPeriodID = PMForecastRecord.SummaryFinPeriod;

				//Visible Key properties:
				record.ProjectTask = budget.ProjectTaskID;
				record.AccountGroup = budget.AccountGroupID;
				record.Inventory = budget.InventoryID;
				record.CostCode = budget.CostCodeID;
				record.Period = null;

				//Other properties:
				record.AccountGroupType = budget.AccountGroupType;
				record.PlannedStartDate = budget.PlannedStartDate;
				record.PlannedEndDate = budget.PlannedEndDate;
				record.Description = budget.Description;
				record.Qty = budget.Qty;
				record.CuryAmount = budget.CuryAmount;
				record.RevisedQty = budget.RevisedQty;
				record.CuryRevisedAmount = budget.CuryRevisedAmount;
				record.ChangeOrderQty = budget.ChangeOrderQty;
				record.CuryChangeOrderAmount = budget.CuryChangeOrderAmount;
				record.ActualQty = budget.ActualQty;
				record.CuryActualAmount = budget.CuryActualAmount;
				record.ActualAmount = budget.ActualAmount;

				return record;
			}

			public static PMForecastRecord CreateTotalRecord(PMBudgetInfo budget)
			{
				PMForecastRecord record = new PMForecastRecord();
				//Keys:
				record.ProjectID = budget.ProjectID;
				record.ProjectTaskID = budget.ProjectTaskID;
				record.AccountGroupID = budget.AccountGroupID;
				record.InventoryID = budget.InventoryID;
				record.CostCodeID = budget.CostCodeID;
				record.FinPeriodID = PMForecastRecord.TotalFinPeriod;

				//Visible Key properties:
				record.ProjectTask = null;
				record.AccountGroup = null;
				record.Inventory = null;
				record.CostCode = null;
				record.Period = "Total:";

				//Other properties:
				record.AccountGroupType = budget.AccountGroupType;
				record.PlannedStartDate = null;
				record.PlannedEndDate = null;
				record.Description = null;
				record.Qty = 0;
				record.CuryAmount = 0;
				record.RevisedQty = 0;
				record.CuryRevisedAmount = 0;
				record.ChangeOrderQty = 0;
				record.CuryChangeOrderAmount = 0;
				record.ActualQty = 0;
				record.CuryActualAmount = 0;
				record.ActualAmount = 0;

				return record;
			}

			public static PMForecastRecord CreateDifferenceRecord(PMBudgetInfo budget, PMForecastRecord totalRecord)
			{
				PMForecastRecord record = new PMForecastRecord();
				//Keys:
				record.ProjectID = budget.ProjectID;
				record.ProjectTaskID = budget.ProjectTaskID;
				record.AccountGroupID = budget.AccountGroupID;
				record.InventoryID = budget.InventoryID;
				record.CostCodeID = budget.CostCodeID;
				record.FinPeriodID = PMForecastRecord.DifferenceFinPeriod;

				//Visible Key properties:
				record.ProjectTask = null;
				record.AccountGroup = null;
				record.Inventory = null;
				record.CostCode = null;
				record.Period = "Delta:";

				//Other properties:
				record.AccountGroupType = budget.AccountGroupType;
				record.PlannedStartDate = null;
				record.PlannedEndDate = null;
				record.Description = null;
				record.Qty = budget.Qty.GetValueOrDefault() - totalRecord.Qty.GetValueOrDefault();
				record.CuryAmount = budget.CuryAmount.GetValueOrDefault() - totalRecord.CuryAmount.GetValueOrDefault();
				record.RevisedQty = budget.RevisedQty.GetValueOrDefault() - totalRecord.RevisedQty.GetValueOrDefault();
				record.CuryRevisedAmount = budget.CuryRevisedAmount.GetValueOrDefault() - totalRecord.CuryRevisedAmount.GetValueOrDefault();
				record.ChangeOrderQty = budget.ChangeOrderQty.GetValueOrDefault() - totalRecord.ChangeOrderQty.GetValueOrDefault();
				record.CuryChangeOrderAmount = budget.CuryChangeOrderAmount.GetValueOrDefault() - totalRecord.CuryChangeOrderAmount.GetValueOrDefault();
				record.ActualQty = budget.ActualQty.GetValueOrDefault() - totalRecord.ActualQty.GetValueOrDefault(); ;
				record.CuryActualAmount = budget.CuryActualAmount.GetValueOrDefault() - totalRecord.CuryActualAmount.GetValueOrDefault();
				record.ActualAmount = budget.ActualAmount.GetValueOrDefault() - totalRecord.ActualAmount.GetValueOrDefault();

				return record;
			}

			public static PMForecastRecord CreateDetailRecord(PMForecastDetail detail, PMBudgetInfo budget, PMForecastHistoryInfo history)
			{
				PMForecastRecord record = new PMForecastRecord();
				//Keys:
				record.ProjectID = budget.ProjectID;
				record.ProjectTaskID = budget.ProjectTaskID;
				record.AccountGroupID = budget.AccountGroupID;
				record.InventoryID = budget.InventoryID;
				record.CostCodeID = budget.CostCodeID;
				record.FinPeriodID = detail.PeriodID;

				//Visible Key properties:
				record.ProjectTask = null;
				record.AccountGroup = null;
				record.Inventory = null;
				record.CostCode = null;
				record.Period = FinPeriodIDFormattingAttribute.FormatForError(detail.PeriodID);

				//Other properties:
				record.AccountGroupType = budget.AccountGroupType;
				record.PlannedStartDate = null;
				record.PlannedEndDate = null;
				record.Description = detail.Description;
				record.Qty = detail.Qty;
				record.CuryAmount = detail.CuryAmount;
				record.RevisedQty = detail.RevisedQty;
				record.CuryRevisedAmount = detail.CuryRevisedAmount;
				
				if (history != null)
				{
					record.ChangeOrderQty = history.ChangeOrderQty.GetValueOrDefault();
					record.CuryChangeOrderAmount = history.CuryChangeOrderAmount.GetValueOrDefault();
					record.ActualQty = history.ActualQty.GetValueOrDefault();
					record.CuryActualAmount = history.CuryActualAmount.GetValueOrDefault();
					record.ActualAmount = history.ActualAmount.GetValueOrDefault();
				}

				return record;
			}
		}

		[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
		public class PMForecastFilter : IBqlTable
		{
			#region ProjectID
			public abstract class projectID : PX.Data.BQL.BqlInt.Field<projectID>
			{
			}
			protected Int32? _ProjectID;
			[PXDefault(typeof(PMForecast.projectID))]
			[PXDBInt]
			public virtual Int32? ProjectID
			{
				get
				{
					return this._ProjectID;
				}
				set
				{
					this._ProjectID = value;
				}
			}
			#endregion
			#region ProjectTaskID
			public abstract class projectTaskID : PX.Data.BQL.BqlInt.Field<projectTaskID>
			{
			}
			[ProjectTask(typeof(projectID))]
			public virtual Int32? ProjectTaskID
			{
				get;
				set;
			}
			#endregion
			#region AccountGroupType
			public abstract class accountGroupType : PX.Data.BQL.BqlString.Field<accountGroupType>
			{
			}
			[PXDBString(1)]
			[PXDefault(GL.AccountType.Expense)]
			[PMAccountType.FilterList()]
			[PXUIField(DisplayName = "Type")]
			public virtual string AccountGroupType
			{
				get;
				set;

			}
			#endregion
			#region AccountGroupID
			public abstract class accountGroupID : PX.Data.BQL.BqlInt.Field<accountGroupID>
			{
			}
			[AccountGroupAttribute()]
			public virtual Int32? AccountGroupID
			{
				get;
				set;

			}
			#endregion
			#region InventoryID
			public abstract class inventoryID : PX.Data.BQL.BqlInt.Field<inventoryID>
			{
			}
			[PXDBInt]
			[PXUIField(DisplayName = "Inventory ID")]
			[PMInventorySelector(Filterable = true)]
			public virtual Int32? InventoryID
			{
				get;
				set;
			}
			#endregion
			#region CostCodeID
			public abstract class costCodeID : PX.Data.BQL.BqlInt.Field<costCodeID>
			{
			}
			[CostCode(Filterable = false, SkipVerification = true)]
			public virtual Int32? CostCodeID
			{
				get;
				set;
			}
			#endregion
		}

		[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
		public class PMForecastAddPeriodDialogInfo : IBqlTable
		{
			#region StartPeriodID
			public abstract class startPeriodID : PX.Data.BQL.BqlString.Field<startPeriodID>
			{
			}
		    [FinPeriodSelector(null, null, masterPeriodBasedOnOrganizationPeriods: false)]
			[PXUIField(DisplayName = "Period From")]
			public virtual string StartPeriodID
			{
				get;
				set;
			}
			#endregion
			#region EndPeriodID
			public abstract class endPeriodID : PX.Data.BQL.BqlString.Field<endPeriodID>
			{
			}
            [FinPeriodSelector(null, null, masterPeriodBasedOnOrganizationPeriods: false)]
            [PXUIField(DisplayName = "Period To")]
			public virtual string EndPeriodID
			{
				get;
				set;
			}
			#endregion

			#region StartDate
			public abstract class startDate : PX.Data.BQL.BqlDateTime.Field<startDate>
			{
			}

			[PXDBDate()]
			[PXUIField(DisplayName = "Start Date", Enabled = false)]
			public virtual DateTime? StartDate
			{
				get;
				set;
			}
			#endregion
			#region EndDate
			public abstract class endDate : PX.Data.BQL.BqlDateTime.Field<endDate>
			{
			}
			[PXDBDate()]
			[PXUIField(DisplayName = "End Date", Enabled = false)]
			public virtual DateTime? EndDate
			{
				get;
				set;
			}
			#endregion

		}

		[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
		public class PMForecastCopyDialogInfo : IBqlTable
		{
			#region RevisionID
			public abstract class revisionID : PX.Data.BQL.BqlString.Field<revisionID>
			{
			}

			[PXDBString(15)]
			[PXDefault]
			[PXUIField(DisplayName = "New Revision")]
			public virtual string RevisionID
			{
				get;
				set;
			}
			#endregion

		}

		[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
		public class PMForecastDistributeDialogInfo : IBqlTable
		{
			public const string AllRecords = "0";
			public const string Redistribute = "0";
			public const string SelectedRecord = "1";
			public const string AppendVariance = "1";

			#region ValueOption
			public abstract class valueOption : PX.Data.BQL.BqlString.Field<valueOption>
			{
			}

			[PXStringList(new string[] { "0", "1" }, new string[] { "Distribute Total", "Add Delta" })]
			[PXDBString(1)]
			[PXDefault("0")]
			public virtual string ValueOption
			{
				get;
				set;
			}
			#endregion

			#region Qty
			public abstract class qty : PX.Data.BQL.BqlBool.Field<qty>
			{
			}

			[PXDBBool]
			[PXUIField(DisplayName = "Original Budgeted Quantity")]
			public virtual bool? Qty
			{
				get;
				set;
			}
			#endregion			
			#region Amount
			public abstract class amount : PX.Data.BQL.BqlBool.Field<amount>
			{
			}

			[PXDBBool]
			[PXUIField(DisplayName = "Original Budgeted Amount")]
			public virtual bool? Amount
			{
				get;
				set;
			}
			#endregion			
			#region RevisedQty
			public abstract class revisedQty : PX.Data.BQL.BqlBool.Field<revisedQty>
			{
			}

			[PXDBBool]
			[PXDefault(true)]
			[PXUIField(DisplayName = "Revised Budgeted Quantity")]
			public virtual bool? RevisedQty
			{
				get;
				set;
			}
			#endregion			
			#region RevisedAmount
			public abstract class revisedAmount : PX.Data.BQL.BqlBool.Field<revisedAmount>
			{
			}

			[PXDBBool]
			[PXDefault(true)]
			[PXUIField(DisplayName = "Revised Budgeted Amount")]
			public virtual bool? RevisedAmount
			{
				get;
				set;
			}
			#endregion			

			#region ApplyOption
			public abstract class applyOption : PX.Data.BQL.BqlString.Field<applyOption>
			{
			}
			[PXStringList(new string[] { AllRecords, SelectedRecord, },
				new string[] { "All Budget Lines",
					"Selected Budget Line" })]
			[PXDBString(1)]
			[PXDefault("0")]
			public virtual string ApplyOption
			{
				get;
				set;
			}
			#endregion
		}

		#endregion
	}
}
