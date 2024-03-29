using PX.Common;
using PX.Data;
using PX.Objects.AP;
using PX.Objects.AR;
using PX.Objects.CS;
using PX.Objects.FS.ParallelProcessing;
using PX.Objects.SO;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using PX.Objects.PM;
using PX.Objects.IN;

namespace PX.Objects.FS
{
    public class ErrorInfo
    {
        public int? SOID;
        public int? AppointmentID;
        public string ErrorMessage;
        public bool HeaderError;
    }

    public abstract class CreateInvoiceBase<TGraph, TPostLine> : PXGraph<TGraph>, IInvoiceProcessGraph
        where TGraph : PX.Data.PXGraph
        where TPostLine : class, PX.Data.IBqlTable, IPostLine, new()
    {
        protected StringBuilder groupKey = null;
        protected string billingBy = null;

        #region Selects
        [PXHidden]
        public PXSetup<FSSetup> SetupRecord;
        public PXFilter<CreateInvoiceFilter> Filter;
        public PXCancel<CreateInvoiceFilter> Cancel;

        [PXFilterable]
        public PXFilteredProcessing<TPostLine, CreateInvoiceFilter,
                                Where<
                                    True, Equal<False>>> PostLines;
        #endregion

        #region Actions
        
        #region FilterManually
        public PXAction<CreateInvoiceFilter> filterManually;
        [PXUIField(DisplayName = "Apply Filters")]
        public virtual IEnumerable FilterManually(PXAdapter adapter)
        {
            Filter.Current.LoadData = true;
            return adapter.Get();
        }

        #endregion
        #region OpenReviewTemporaryBatch
        public PXAction<CreateInvoiceFilter> openReviewTemporaryBatch;
        [PXUIField(DisplayName = "View Temporary Batches", MapEnableRights = PXCacheRights.Select, MapViewRights = PXCacheRights.Select)]
        [PXButton(VisibleOnProcessingResults = true)]
        public virtual void OpenReviewTemporaryBatch()
        {
            ReviewInvoiceBatches graphReviewInvoiceBatches = PXGraph.CreateInstance<ReviewInvoiceBatches>();
            PXRedirectHelper.TryRedirect(graphReviewInvoiceBatches, PXRedirectHelper.WindowMode.NewWindow);
        }
        #endregion
        #region FixServiceOrdersWithoutBillingSettings
        public PXAction<CreateInvoiceFilter> fixServiceOrdersWithoutBillingSettings;
        [PXUIField(DisplayName = "Fix Service Orders Without Billing Settings", Visible = false)]
        public virtual IEnumerable FixServiceOrdersWithoutBillingSettings(PXAdapter adapter)
        {
            SharedFunctions.UpdateBillingInfoInDocsLO(this, null, null);
            return adapter.Get();
        }
        #endregion
        #endregion

        public CreateInvoiceBase()
        {
            IncludeReviewInvoiceBatchesAction();
        }

        public OnDocumentHeaderInsertedDelegate OnDocumentHeaderInserted { get; set; }

        public OnTransactionInsertedDelegate OnTransactionInserted { get; set; }

        public BeforeSaveDelegate BeforeSave { get; set; }

        public AfterCreateInvoiceDelegate AfterCreateInvoice { get; set; }

        public PXGraph GetGraph()
        {
            return this;
        }

        #region Event Handlers
        protected virtual void CreateInvoiceFilter_RowUpdated(PXCache cache, PXRowUpdatedEventArgs e)
        {
            if (e.Row == null)
            {
                return;
            }

            Filter.Current.LoadData = false;
        }

        protected virtual void CreateInvoiceFilter_RowSelected(PXCache cache, PXRowSelectedEventArgs e)
        {
            if (e.Row == null)
            {
                return;
            }

            CreateInvoiceFilter createInvoiceFilterRow = (CreateInvoiceFilter)e.Row;

            if (SetupRecord.Current != null)
            {
                filterManually.SetVisible(SetupRecord.Current.FilterInvoicingManually == true);
            }

            SharedFunctions.WarnUserWithServiceOrdersWithoutBillingSettings(cache, createInvoiceFilterRow, fixServiceOrdersWithoutBillingSettings);
            HideOrShowInvoiceActions(cache, createInvoiceFilterRow);
        }
        #endregion

        #region Invoicing Methods
        public static Guid CreateInvoices(CreateInvoiceBase<TGraph, TPostLine> processGraph, List<TPostLine> postLineRows, CreateInvoiceFilter filter, object parentGUID, JobExecutor<InvoicingProcessStepGroupShared> jobExecutor, PXQuickProcess.ActionFlow quickProcessFlow)
        {
            PXTrace.WriteInformation("Data preparation started.");

            Guid currentProcessID = processGraph.SaveUserSelection(postLineRows);

            PXResultset<FSPostDoc> billingCycles =
                                PXSelectGroupBy<FSPostDoc,
                                Where<
                                    FSPostDoc.processID, Equal<Required<FSPostDoc.processID>>>,
                                Aggregate<
                                    GroupBy<FSPostDoc.billingCycleID>>,
                                OrderBy<
                                    Asc<FSPostDoc.billingCycleID>>>
                                .Select(processGraph, currentProcessID);

            foreach (FSPostDoc billingCycle in billingCycles)
            {
                if (filter.SOQuickProcess == true && quickProcessFlow == PXQuickProcess.ActionFlow.NoFlow)
                {
                    quickProcessFlow = PXQuickProcess.ActionFlow.HasNextInFlow;
                }

                processGraph.CreatePostingBatchesForBillingCycle(currentProcessID, (int)billingCycle.BillingCycleID, filter, postLineRows, jobExecutor, quickProcessFlow);
            }
            PXTrace.WriteInformation("Data preparation completed.");

            PXTrace.WriteInformation("Invoice generation started.");
            jobExecutor.ExecuteJobs(processGraph.Accessinfo.BranchID, PXAccess.GetCompanyName(), parentGUID);
            PXTrace.WriteInformation("Invoice generation completed.");
            
            PXTrace.WriteInformation("Clean of unprocessed documents started.");
            processGraph.DeletePostDocsWithError(currentProcessID);
            PXTrace.WriteInformation("Clean of unprocessed documents completed.");

            PXTrace.WriteInformation("External tax calculation started.");
            processGraph.CalculateExternalTaxes(currentProcessID);
            PXTrace.WriteInformation("External tax calculation completed.");

            InvoicingFunctions.ApplyInvoiceActions(processGraph.GetGraph(), filter, currentProcessID);

            return currentProcessID;
        }

        protected virtual void CreatePostingBatches_ARAP(Guid currentProcessID, int billingCycleID, CreateInvoiceFilter filter, PXResultset<FSPostDoc> billingCycleOptionsGroups, List<TPostLine> postLineRows, JobExecutor<InvoicingProcessStepGroupShared> jobExecutor, PXQuickProcess.ActionFlow quickProcessFlow)
        {
            var arInvoiceList = new List<FSPostDoc>();
            var apInvoiceList = new List<FSPostDoc>();
            decimal? invoiceTotal = 0;

            foreach (FSPostDoc billingCycleOptionsGroup in billingCycleOptionsGroups)
            {
                GetInvoiceLines(currentProcessID, billingCycleID, billingCycleOptionsGroup.GroupKey, true, out invoiceTotal, filter.PostTo);

                if (invoiceTotal < 0 && billingCycleOptionsGroup.PostNegBalanceToAP == true)
                {
                    billingCycleOptionsGroup.InvtMult = -1;
                    apInvoiceList.Add(billingCycleOptionsGroup);
                }
                else
                {
                    if (invoiceTotal < 0)
                    {
                        billingCycleOptionsGroup.InvtMult = -1;
                    }
                    else
                    {
                        billingCycleOptionsGroup.InvtMult = 1;
                    }

                    arInvoiceList.Add(billingCycleOptionsGroup);
                }
            }

            if (arInvoiceList.Count > 0)
            {
                Job job = CreatePostingBatchAndInvoicesJob(currentProcessID, billingCycleID, filter.UpToDate, filter.InvoiceDate, filter.InvoiceFinPeriodID, ID.Batch_PostTo.AR, arInvoiceList, postLineRows, jobExecutor.MainContext, quickProcessFlow);
                jobExecutor.JobList.Add(job);

                arInvoiceList.Clear();
            }            

            if (apInvoiceList.Count > 0)
            {
                Job job = CreatePostingBatchAndInvoicesJob(currentProcessID, billingCycleID, filter.UpToDate, filter.InvoiceDate, filter.InvoiceFinPeriodID, ID.Batch_PostTo.AP, apInvoiceList, postLineRows, jobExecutor.MainContext, quickProcessFlow);
                jobExecutor.JobList.Add(job);

                apInvoiceList.Clear();
            }
        }

        protected virtual Job CreatePostingBatchAndInvoicesJob(Guid currentProcessID, int billingCycleID, DateTime? upToDate, DateTime? invoiceDate, string invoiceFinPeriodID, string postTo, List<FSPostDoc> invoiceList, List<TPostLine> postLineRows, ExecutionContext executionContext, PXQuickProcess.ActionFlow quickProcessFlow)
        {
            var job = new Job(new PostBatchJobShared());
            Step step;

            step = new Step(PXMessages.LocalizeFormatNoPrefix(TX.Messages.CREATE_FSPOSTBATCH, billingCycleID), StepProcessingType.WaitStepCompletionBeforeNextStep, job);
            job.StepList.Add(step);

            var iparm = new InvoicingParm(step, executionContext, postTo, currentProcessID, billingCycleID, string.Empty, postLineRows, billingBy);
            iparm.IsGenerateInvoiceScreen = Filter.Current.IsGenerateInvoiceScreen;
            step.Parm = iparm;

            iparm.UpToDate = upToDate;
            iparm.InvoiceDate = invoiceDate;
            iparm.InvoiceFinPeriodID = invoiceFinPeriodID;

            step.StepMethod = CreatePostingBatch;
            step.CheckStepResultMethod = CreateCompletePostingBatchCheckResult;
                
            foreach (FSPostDoc invoiceItem in invoiceList)
            {
                decimal? invoiceTotal = 0;

                step = new Step(PXMessages.LocalizeFormatNoPrefix(TX.Messages.CREATE_INVOICE_BILLING_CYCLE, billingCycleID, invoiceItem.GroupKey), StepProcessingType.Independent, job);
                job.StepList.Add(step);
                step.Parm = new InvoicingParm(step, executionContext, postTo, currentProcessID, billingCycleID, invoiceItem.GroupKey, postLineRows, billingBy, invoiceItem.InvtMult, quickProcessFlow);
                step.Parm.IsGenerateInvoiceScreen = Filter.Current.IsGenerateInvoiceScreen;
                step.docLines = GetInvoiceLines(currentProcessID, billingCycleID, invoiceItem.GroupKey, false, out invoiceTotal, postTo);

                step.StepMethod = CreateInvoiceDocument;
                step.CheckStepResultMethod = CreateInvoiceDocumentCheckResult;
            }

            step = new Step(PXMessages.LocalizeFormatNoPrefix(TX.Messages.APPLY_PREPAYMENT_BILLING_CYCLE, billingCycleID), StepProcessingType.WaitCompletionOfAllPreviousStepsBeforeRun, job);
            job.StepList.Add(step);

            step.Parm = new InvoicingParm(step, executionContext, postTo, currentProcessID, billingCycleID, string.Empty, postLineRows, billingBy);
            step.Parm.IsGenerateInvoiceScreen = Filter.Current.IsGenerateInvoiceScreen;
            step.StepMethod = ApplyPrepayments;

            step = new Step(PXMessages.LocalizeFormatNoPrefix(TX.Messages.COMPLETE_FSPOSTBATCH_BILLING_CYCLE, billingCycleID), StepProcessingType.WaitCompletionOfAllPreviousStepsBeforeRun, job);
            job.StepList.Add(step);

            step.Parm = new InvoicingParm(step, executionContext, postTo, currentProcessID, billingCycleID, string.Empty, postLineRows, billingBy);
            step.Parm.IsGenerateInvoiceScreen = Filter.Current.IsGenerateInvoiceScreen;
            step.StepMethod = CompletePostingBatch;
            step.CheckStepResultMethod = CreateCompletePostingBatchCheckResult;

            return job;
        }

        public static void CreatePostingBatch(MethodParm parm)
        {
            var iparm = (InvoicingParm)parm;
            var postBatchShared = (PostBatchJobShared)iparm.MyStep.ParentJob.Shared;
            
            postBatchShared.PostBatchEntryGraph = PXGraph.CreateInstance<PostBatchEntry>();

            postBatchShared.FSPostBatchRow = postBatchShared.PostBatchEntryGraph.CreatePostingBatch(iparm.BillingCycleID, iparm.UpToDate, iparm.InvoiceDate, iparm.InvoiceFinPeriodID, iparm.TargetScreen);
        }

        public static void ApplyPrepayments(FSPostBatch fsPostBatchRow)
        {
            if (fsPostBatchRow != null && fsPostBatchRow.PostTo == ID.Batch_PostTo.SO)
            {
                SOOrderEntry graphSOOrderEntry = PXGraph.CreateInstance<SOOrderEntry>();

                var results = PXSelectJoinGroupBy<PostingBatchDetail,
                                InnerJoin<FSAdjust,
                                    On<PostingBatchDetail.sORefNbr, Equal<FSAdjust.adjdOrderNbr>,
                                    And<PostingBatchDetail.srvOrdType, Equal<FSAdjust.adjdOrderType>>>>,
                                Where<
                                    PostingBatchDetail.batchID, Equal<Required<FSPostBatch.batchID>>>,
                                Aggregate<
                                    GroupBy<PostingBatchDetail.sOID,
                                    GroupBy<PostingBatchDetail.appointmentID,
                                    GroupBy<PostingBatchDetail.noteID,
                                    GroupBy<PostingBatchDetail.srvOrdType,
                                    GroupBy<PostingBatchDetail.apRefNbr,
                                    GroupBy<PostingBatchDetail.aRPosted,
                                    GroupBy<PostingBatchDetail.aPPosted,
                                    GroupBy<PostingBatchDetail.sOPosted,
                                    GroupBy<PostingBatchDetail.sOInvPosted,
                                    GroupBy<PostingBatchDetail.iNPosted>>>>>>>>>>>>.Select(graphSOOrderEntry, fsPostBatchRow.BatchID);

                foreach (PXResult<PostingBatchDetail, FSAdjust> result in results)
                {
                    try
                    {
                        PostingBatchDetail postingBatchDetailRow = (PostingBatchDetail)result;
                        FSAdjust fsAdjustRow = (FSAdjust)result;

                        SOOrder sOOrderRow = null;
                        sOOrderRow = graphSOOrderEntry.Document.Current = graphSOOrderEntry.Document.Search<SOOrder.orderNbr>(postingBatchDetailRow.SOOrderNbr, postingBatchDetailRow.SOOrderType);

                        SharedClasses.SOPrepaymentHelper SOPrepaymentApplication = new SharedClasses.SOPrepaymentHelper();

                        foreach (SOLine soLineRow in graphSOOrderEntry.Transactions.Select())
                        {
                            FSxSOLine fSxSOLineRow = graphSOOrderEntry.Transactions.Cache.GetExtension<FSxSOLine>(soLineRow);

                            SOPrepaymentApplication.Add(soLineRow, fSxSOLineRow);
                        }

                        decimal CuryDocBal = 0m;

                        foreach (SharedClasses.SOPrepaymentBySO row in SOPrepaymentApplication.SOPrepaymentList)
                        {
                            PXResultset<ARPayment> PaymentList = row.GetPrepaymentBySO(graphSOOrderEntry);
                            int i = 0;

                            while (PaymentList != null && i < PaymentList.Count && row.unpaidAmount > 0)
                            {
                                if (string.Equals(((ARPayment)PaymentList[i]).CuryID, sOOrderRow.CuryID) == true)
                                {
                                    SOOrderEntry.SOAdjust sOAdjust = new SOOrderEntry.SOAdjust();
                                    sOAdjust.AdjgDocType = ARPaymentType.Prepayment;
                                    sOAdjust = graphSOOrderEntry.Adjustments.Current = graphSOOrderEntry.Adjustments.Insert(sOAdjust);

                                    graphSOOrderEntry.Adjustments.SetValueExt<SOOrderEntry.SOAdjust.adjgRefNbr>(sOAdjust, ((ARPayment)PaymentList[i]).RefNbr);

                                    CuryDocBal = sOAdjust.CuryDocBal ?? 0m;

                                    if (CuryDocBal > 0)
                                    {
                                        if (row.unpaidAmount > CuryDocBal)
                                        {
                                            graphSOOrderEntry.Adjustments.SetValueExt<SOOrderEntry.SOAdjust.curyAdjdAmt>(sOAdjust, CuryDocBal);
                                            row.unpaidAmount = row.unpaidAmount - CuryDocBal;
                                        }
                                        else
                                        {
                                            graphSOOrderEntry.Adjustments.SetValueExt<SOOrderEntry.SOAdjust.adjgRefNbr>(sOAdjust, ((ARPayment)PaymentList[i]).RefNbr);
                                            graphSOOrderEntry.Adjustments.SetValueExt<SOOrderEntry.SOAdjust.curyAdjdAmt>(sOAdjust, row.unpaidAmount);

                                            row.unpaidAmount = 0;
                                        }
                                    }
                                }

                                CuryDocBal = 0m;
                                i++;
                            }
                        }

                        foreach (SOOrderEntry.SOAdjust soAdjustRow in graphSOOrderEntry.Adjustments.Select())
                        {
                            if (soAdjustRow.CuryAdjdAmt == 0)
                            {
                                graphSOOrderEntry.Adjustments.Delete(soAdjustRow);
                            }
                        }

                        graphSOOrderEntry.Save.Press();
                    }
                    catch (Exception)
                    {

                    }
                }
            }
            else if (fsPostBatchRow != null && fsPostBatchRow.PostTo == ID.Batch_PostTo.SI)
            {
                SOInvoiceEntry graphSOInvoiceEntry = PXGraph.CreateInstance<SOInvoiceEntry>();

                var results = PXSelectJoinGroupBy<PostingBatchDetail,
                                InnerJoin<FSAdjust,
                                    On<PostingBatchDetail.sORefNbr, Equal<FSAdjust.adjdOrderNbr>,
                                    And<PostingBatchDetail.srvOrdType, Equal<FSAdjust.adjdOrderType>>>>,
                                Where<
                                    PostingBatchDetail.batchID, Equal<Required<FSPostBatch.batchID>>>,
                                Aggregate<
                                    GroupBy<PostingBatchDetail.sOID,
                                    GroupBy<PostingBatchDetail.appointmentID,
                                    GroupBy<PostingBatchDetail.noteID,
                                    GroupBy<PostingBatchDetail.srvOrdType,
                                    GroupBy<PostingBatchDetail.apRefNbr,
                                    GroupBy<PostingBatchDetail.aRPosted,
                                    GroupBy<PostingBatchDetail.aPPosted,
                                    GroupBy<PostingBatchDetail.sOPosted,
                                    GroupBy<PostingBatchDetail.iNPosted>>>>>>>>>>>
                                .Select(graphSOInvoiceEntry, fsPostBatchRow.BatchID);

                foreach (PXResult<PostingBatchDetail, FSAdjust> result in results)
                {
                    try
                    {
                        PostingBatchDetail postingBatchDetailRow = (PostingBatchDetail)result;
                        FSAdjust fsAdjustRow = (FSAdjust)result;

                        if (postingBatchDetailRow.SOInvDocType != ARInvoiceType.Invoice)
                        {
                            continue;
                        }

                        ARInvoice arInvoiceRow = graphSOInvoiceEntry.Document.Current = graphSOInvoiceEntry.Document.Search<ARInvoice.refNbr>(postingBatchDetailRow.SOInvRefNbr, postingBatchDetailRow.SOInvDocType);
                        graphSOInvoiceEntry.UnattendedMode = false;

                        SharedClasses.SOPrepaymentHelper SOPrepaymentApplication = new SharedClasses.SOPrepaymentHelper();

                        foreach (ARTran arTranRow in graphSOInvoiceEntry.Transactions.Select())
                        {
                            FSxARTran fsxARTranRow = graphSOInvoiceEntry.Transactions.Cache.GetExtension<FSxARTran>(arTranRow);

                            SOPrepaymentApplication.Add(arTranRow, fsxARTranRow);
                        }

                        decimal CuryDocBal = 0m;

                        foreach (SharedClasses.SOPrepaymentBySO row in SOPrepaymentApplication.SOPrepaymentList)
                        {
                            PXResultset<ARPayment> PaymentList = row.GetPrepaymentBySO(graphSOInvoiceEntry);
                            int i = 0;

                            while (PaymentList != null && i < PaymentList.Count && row.unpaidAmount > 0)
                            {
                                ARPayment arPaymentRow = (ARPayment)PaymentList[i];
                                if (string.Equals(arPaymentRow.CuryID, arInvoiceRow.CuryID) == true)
                                {
                                    ARAdjust2 arAdjust2Row = graphSOInvoiceEntry.Adjustments.Select().Where(x => ((ARAdjust2)x).AdjgRefNbr == arPaymentRow.RefNbr).FirstOrDefault();

                                    CuryDocBal = arAdjust2Row.CuryDocBal ?? 0m;

                                    if (CuryDocBal > 0)
                                    {
                                        if (row.unpaidAmount > CuryDocBal)
                                        {
                                            graphSOInvoiceEntry.Adjustments.SetValueExt<ARAdjust2.curyAdjdAmt>(arAdjust2Row, CuryDocBal);
                                            row.unpaidAmount = row.unpaidAmount - CuryDocBal;
                                        }
                                        else
                                        {
                                            graphSOInvoiceEntry.Adjustments.SetValueExt<ARAdjust2.curyAdjdAmt>(arAdjust2Row, row.unpaidAmount);
                                            row.unpaidAmount = 0;
                                        }
                                    }
                                }

                                CuryDocBal = 0m;
                                i++;
                            }
                        }

                        graphSOInvoiceEntry.Save.Press();
                    }
                    catch (Exception )
                    {

                    }
                }
            }
        }

        public static void ApplyPrepayments(MethodParm parm)
        {
            var iparm = (InvoicingParm)parm;
            var postBatchShared = (PostBatchJobShared)iparm.MyStep.ParentJob.Shared;

            ApplyPrepayments(postBatchShared.FSPostBatchRow);
        }

        public static void CompletePostingBatch(MethodParm parm)
        {
            var iparm = (InvoicingParm)parm;
            var postBatchShared = (PostBatchJobShared)iparm.MyStep.ParentJob.Shared;

            int DocumentsQty = 0;

            foreach (Step step in iparm.MyStep.ParentJob.StepList)
            {
                DocumentsQty += ((InvoicingParm)step.Parm).DocumentsQty;
            }

            postBatchShared.PostBatchEntryGraph.CompletePostingBatch(postBatchShared.FSPostBatchRow, DocumentsQty);
        }

        public static void CreateCompletePostingBatchCheckResult(MethodParm parm)
        {
            var iparm = (InvoicingParm)parm;
            var postBatchShared = (PostBatchJobShared)iparm.MyStep.ParentJob.Shared;

            if (iparm.Exception == null)
            {
                iparm.Exception = iparm.MyStep.ParentJob.Exception;
            }

            if (iparm.Exception != null)
            {
                lock (iparm.MyStep.ParentJob)
                {
                    if (iparm.MyStep.ParentJob.ExceptionProcessed == true)
                    {
                        return;
                    }

                    iparm.MyStep.ParentJob.ExceptionProcessed = true;
                    iparm.MyStep.ParentJob.Exception = iparm.Exception;
                }

                var exceptionWithContextMessage = ExceptionHelper.GetExceptionWithContextMessage(
                                                    PXMessages.LocalizeFormatNoPrefix(
                                                        TX.Messages.ERROR_CREATING_POSTING_BATCH,
                                                        postBatchShared.FSPostBatchRow == null ? string.Empty : postBatchShared.FSPostBatchRow.BatchNbr),
                                                    iparm.Exception);

                foreach (TPostLine postLineRow in iparm.PostLineRows)
                {
                    if (postLineRow.BillingCycleID == iparm.BillingCycleID)
                    {
                        postLineRow.BatchID = null;
                        postLineRow.ErrorFlag = true;
                        if(parm.IsGenerateInvoiceScreen == true)
                        {
                            PXProcessing<TPostLine>.SetError((int)postLineRow.RowIndex, exceptionWithContextMessage);
                        }
                        else
                        {
                            throw exceptionWithContextMessage;
                        }
                    }
                }

                try
                {
                    if (postBatchShared.FSPostBatchRow != null)
                    {
                        lock (postBatchShared.FSPostBatchRow)
                        {
                            if (postBatchShared.FSPostBatchRow.BatchID > 0)
                            {
                                while (postBatchShared.AbortedTasks.Count() > 0)
                                {
                                    var r = PXLongOperation.GetTaskList().FirstOrDefault(_ => _.Key == postBatchShared.AbortedTasks[0].ToString());
                                    if (r == null)
                                    {
                                        postBatchShared.AbortedTasks.RemoveAt(0);
                                    }
                                    else
                                    {
                                        System.Threading.Thread.Sleep(Processor.WAIT_TIME_IN_MILLISECONDS);
                                    }
                                }

                                postBatchShared.PostBatchEntryGraph.DeletePostingBatch(postBatchShared.FSPostBatchRow);
                                postBatchShared.FSPostBatchRow.BatchID = 0;
                            }
                        }
                    }
                }
                finally
                {
                    postBatchShared.Dispose();
                    iparm.MyStep.MyGroup.Shared.Dispose();
                }
            }
        }

        public virtual void CreateInvoiceDocument(MethodParm parm)
        {
            var iparm = (InvoicingParm)parm;
            var postBatchShared = (PostBatchJobShared)iparm.MyStep.ParentJob.Shared;
            var processShared = (InvoicingProcessStepGroupShared)iparm.MyStep.MyGroup.Shared;

            processShared.Initialize(iparm.TargetScreen, iparm.BillingBy);

            processShared.InvoiceGraph.IsInvoiceProcessRunning = true;

            OnTransactionInsertedDelegate onTransactionInserted = processShared.ProcessGraph.OnTransactionInserted;

            iparm.Exception = null;
            iparm.DocumentsQty = 0;

            FSCreatedDoc fsCreatedDocRow = null;
            List<DocLineExt> docLines = parm.MyStep.docLines;
            List<DocLineExt> docLinesGrouped = docLines.GroupBy(x => new { x.docLine.DocID, x.docLine.LineID }).Select(g => g.First()).ToList();

            int retryCount = 3;
            while (retryCount > 0)
            {
                processShared.InvoiceGraph.CreateInvoice(processShared.ProcessGraph.GetGraph(), docLines, docLinesGrouped, (short)iparm.InvtMult, postBatchShared.FSPostBatchRow.InvoiceDate, postBatchShared.FSPostBatchRow.FinPeriodID, processShared.ProcessGraph.OnDocumentHeaderInserted, onTransactionInserted, iparm.QuickProcessFlow);

                try
                {
                    using (var ts = new PXTransactionScope())
                    {
                        if (retryCount == 3)
                        {
                            DeallocateServiceOrders(processShared.ServiceOrderGraph, docLines);
                        }

                        fsCreatedDocRow = processShared.InvoiceGraph.PressSave((int)postBatchShared.FSPostBatchRow.BatchID, processShared.ProcessGraph.BeforeSave);

                        processShared.CacheFSCreatedDoc.Insert(fsCreatedDocRow);
                        processShared.CacheFSCreatedDoc.Persist(PXDBOperation.Insert);
                        UpdateUserSelection(
                            processShared.ProcessGraph.GetGraph(),
                            fsCreatedDocRow,
                            iparm.CurrentProcessID,
                            iparm.BillingCycleID,
                            iparm.GroupKey);

                        CreatePostRegister(
                            processShared.ProcessGraph.GetGraph(),
                            docLinesGrouped,
                            fsCreatedDocRow,
                            iparm.CurrentProcessID);

                        if (processShared.ProcessGraph.AfterCreateInvoice != null)
                        {
                            processShared.ProcessGraph.AfterCreateInvoice(processShared.InvoiceGraph.GetGraph(), fsCreatedDocRow);
                        }

                        ts.Complete();
                    }

                    retryCount = 0;
                }
                catch (Exception e)
                {
                    if ((e is PXDatabaseException
                            || e is SharedClasses.TransactionScopeException)
                            && retryCount > 0)
                    {
                        processShared.InvoiceGraph.Clear();
                        processShared.CacheFSCreatedDoc.Clear();

                        retryCount--;

                        PXTrace.WriteWarning(TX.Warning.RETRYING_CREATE_INVOICE_AFTER_ERROR, postBatchShared.FSPostBatchRow.BatchNbr, iparm.GroupKey, e.Message);
                    }
                    else
                    {
                        retryCount = 0;

                        List<ErrorInfo> errorList = processShared.InvoiceGraph.GetErrorInfo();

                        var exceptionWithContextMessage = ExceptionHelper.GetExceptionWithContextMessage(
                                    PXMessages.LocalizeFormatNoPrefix(TX.Messages.ERROR_CREATING_INVOICE_IN_POSTING_BATCH),
                                    e);

                        iparm.Exception = exceptionWithContextMessage;
                        iparm.ErrorList = errorList;

                        throw e;
                    }
                }
            }

            processShared.InvoiceGraph.IsInvoiceProcessRunning = false;
            processShared.InvoiceGraph.Clear();
            processShared.CacheFSCreatedDoc.Clear();

            if (iparm.Exception == null)
            {
                using (var ts = new PXTransactionScope())
                {
                    UpdatePostInfoAndPostDet(docLinesGrouped, postBatchShared.FSPostBatchRow, processShared.PostInfoEntryGraph, processShared.CacheFSPostDet, fsCreatedDocRow);
                    ts.Complete();
                }

                iparm.DocumentsQty = docLinesGrouped.GroupBy(y => y.docLine.DocID).Count();
            }

            processShared.Clear();
        }

        public static void CreateInvoiceDocumentCheckResult(MethodParm parm)
        {
            var iparm = (InvoicingParm)parm;
            var postBatchShared = (PostBatchJobShared)iparm.MyStep.ParentJob.Shared;
            int? batchID = postBatchShared.FSPostBatchRow == null ? null : postBatchShared.FSPostBatchRow.BatchID;

            if (iparm.MyStep.ParentJob.Canceled == true)
            {
                CreateCompletePostingBatchCheckResult(parm);
                return;
            }

            if (iparm.Exception == null)
            {
                foreach (TPostLine postLineRow in iparm.PostLineRows)
                {
                    if (postLineRow.BillingCycleID == iparm.BillingCycleID && postLineRow.GroupKey == iparm.GroupKey)
                    {
                        postLineRow.BatchID = batchID;
                        postLineRow.ErrorFlag = false;
                        PXProcessing<TPostLine>.SetInfo((int)postLineRow.RowIndex, PXMessages.LocalizeFormatNoPrefix(TX.Messages.RECORD_PROCESSED_SUCCESSFULLY));
                    }
                }
            }
            else
            {
                SetGroupKeyErrorInfoInLines(iparm);
            }
        }

        public static void SetGroupKeyErrorInfoInLines(InvoicingParm iparm)
                {
            ErrorInfo errorInfo;

            foreach (TPostLine postLineRow in iparm.PostLineRows.Where(x => x.GroupKey == iparm.GroupKey &&
                                                                            x.BillingCycleID == iparm.BillingCycleID))
                    {
                        postLineRow.BatchID = null;
                        postLineRow.ErrorFlag = true;

                errorInfo = iparm.ErrorList?.Find(e => e.SOID == postLineRow.SOID && e.AppointmentID == postLineRow.AppointmentID);
                StringBuilder errorMsgBuilder = new StringBuilder();
                errorMsgBuilder.Append(iparm.Exception.Message);

                if (errorInfo != null)
                {
                    errorMsgBuilder.Append(Environment.NewLine);
                   
                    if (errorInfo.HeaderError == false)
                    {
                    errorMsgBuilder.Append(PXMessages.LocalizeFormatNoPrefix(
                                                                     TX.Messages.INVOICE_POSSIBLE_ERRORS, errorInfo.AppointmentID != null ? 
                                                                     TX.PostDoc_EntityType.APPOINTMENT : TX.PostDoc_EntityType.SERVICE_ORDER));

                        errorMsgBuilder.Append(Environment.NewLine);
                    }

                    errorMsgBuilder.Append(errorInfo.ErrorMessage);
                }

                var newException = new PXException(errorMsgBuilder.ToString());

                if (errorInfo != null || GroupKeyHasErrors(postLineRow, iparm) == false)
                {
                    if (iparm.IsGenerateInvoiceScreen == true)
                    {
                        PXProcessing<TPostLine>.SetError((int)postLineRow.RowIndex, newException);
                    }
                    else
                    {
                        throw newException;
                    }
                }
                else
                {
                    PXProcessing<TPostLine>.SetWarning((int)postLineRow.RowIndex, newException);
                }
            }
        }

        private static bool GroupKeyHasErrors(TPostLine postLineRow, InvoicingParm iparm)
        {
            ErrorInfo errorInfoGroup = null;
            foreach (TPostLine postLineGroupRow in iparm.PostLineRows.Where(x => x.GroupKey == iparm.GroupKey &&
                                                                                (x.AppointmentID != postLineRow.AppointmentID ||
                                                                                    (postLineRow.AppointmentID == null && x.SOID != postLineRow.SOID))))
            {
                errorInfoGroup = iparm.ErrorList?.Find(e => e.SOID == postLineGroupRow.SOID && e.AppointmentID == postLineGroupRow.AppointmentID);
                if (errorInfoGroup != null)
                {
                    return true;
                }
            }

            return false;
        }

        protected virtual Guid SaveUserSelection(List<TPostLine> postLineRows)
        {
            Guid currentProcessID = Guid.NewGuid();
            int rowIndex = 0;
            var fsPostDoc = new FSPostDoc();
            string screenID = this.Accessinfo.ScreenID.Replace(".", string.Empty);

            foreach (TPostLine postLineRow in postLineRows)
            {
                fsPostDoc.ProcessID = currentProcessID;
                fsPostDoc.BillingCycleID = postLineRow.BillingCycleID;
                fsPostDoc.GroupKey = GetGroupKey(postLineRow);
                fsPostDoc.SOID = postLineRow.SOID;
                fsPostDoc.AppointmentID = postLineRow.AppointmentID;
                fsPostDoc.RowIndex = rowIndex;
                fsPostDoc.PostNegBalanceToAP = postLineRow.PostNegBalanceToAP;

                fsPostDoc.PostOrderType = postLineRow.PostOrderType;
                fsPostDoc.PostOrderTypeNegativeBalance = postLineRow.PostOrderTypeNegativeBalance;

                postLineRow.RowIndex = fsPostDoc.RowIndex;
                postLineRow.GroupKey = fsPostDoc.GroupKey;
                fsPostDoc.EntityType = postLineRow.EntityType;

                rowIndex++;

                PXDatabase.Insert<FSPostDoc>(
                        new PXDataFieldAssign<FSPostDoc.processID>(fsPostDoc.ProcessID),
                        new PXDataFieldAssign<FSPostDoc.billingCycleID>(fsPostDoc.BillingCycleID),
                        new PXDataFieldAssign<FSPostDoc.groupKey>(fsPostDoc.GroupKey),
                        new PXDataFieldAssign<FSPostDoc.entityType>(fsPostDoc.EntityType),
                        new PXDataFieldAssign<FSPostDoc.sOID>(fsPostDoc.SOID),
                        new PXDataFieldAssign<FSPostDoc.appointmentID>(fsPostDoc.AppointmentID),
                        new PXDataFieldAssign<FSPostDoc.rowIndex>(fsPostDoc.RowIndex),
                        new PXDataFieldAssign<FSPostDoc.postNegBalanceToAP>(fsPostDoc.PostNegBalanceToAP),
                        new PXDataFieldAssign<FSPostDoc.postOrderType>(fsPostDoc.PostOrderType),
                        new PXDataFieldAssign<FSPostDoc.postOrderTypeNegativeBalance>(fsPostDoc.PostOrderTypeNegativeBalance),
                        new PXDataFieldAssign<FSPostDoc.createdByID>(this.Accessinfo.UserID),
                        new PXDataFieldAssign<FSPostDoc.createdByScreenID>(screenID),
                        new PXDataFieldAssign<FSPostDoc.createdDateTime>(DateTime.Now));
            }

            return currentProcessID;
        }

        protected virtual void DeletePostDocsWithError(Guid currentProcessID)
        {
            PXDatabase.Delete<FSPostDoc>(
                new PXDataFieldRestrict<FSPostDoc.batchID>(PXDbType.Int, 4, null, PXComp.ISNULL),
                new PXDataFieldRestrict<FSPostDoc.processID>(currentProcessID));

            PXDatabase.Delete<FSPostDoc>(
                new PXDataFieldRestrict<FSPostDoc.batchID>(PXDbType.Int, 4, null, PXComp.ISNULL),
                new PXDataFieldRestrict<FSPostDoc.createdDateTime>(PXDbType.DateTime, 8, DateTime.Now.AddDays(-3), PXComp.LE));
        }

        protected virtual void CalculateExternalTaxes(Guid currentProcessID)
        {
            PXResultset<FSPostDoc> fsPostDocRows = PXSelectGroupBy<FSPostDoc,
                        Where<FSPostDoc.processID, Equal<Required<FSPostDoc.processID>>>,
                        Aggregate<
                            GroupBy<FSPostDoc.postedTO,
                            GroupBy<FSPostDoc.postDocType,
                            GroupBy<FSPostDoc.postRefNbr>>>>>.Select(this, currentProcessID);

            SOOrderEntry graphSOOrderEntry = null;
            ARInvoiceEntry graphARInvoiceEntry = null;
            APInvoiceEntry graphAPInvoiceEntry = null;
            bool forceInstanciateGraph = false;

            foreach (FSPostDoc fsPostDoc in fsPostDocRows)
            {
                if (fsPostDoc.PostedTO == ID.Batch_PostTo.SO)
                {
                    if (graphSOOrderEntry == null || forceInstanciateGraph == true)
                    {
                        graphSOOrderEntry = (SOOrderEntry)InvoicingFunctions.CreateInvoiceGraph(fsPostDoc.PostedTO).GetGraph();
                        forceInstanciateGraph = false;
                    }

                    SOOrder soOrderRow = graphSOOrderEntry.Document.Current = graphSOOrderEntry.Document.Search<SOOrder.orderNbr>(fsPostDoc.PostRefNbr, fsPostDoc.PostDocType);
                    if (soOrderRow != null && soOrderRow.IsTaxValid == false && graphSOOrderEntry.IsExternalTax(soOrderRow.TaxZoneID) == true)
                    {
                        graphSOOrderEntry.Document.Update(graphSOOrderEntry.Document.Current);
                        try
                        {
                            graphSOOrderEntry.Save.Press();
                        }
                        catch(Exception e)
                        {
                            PXTrace.WriteError("Error trying to calculate external taxes for the Sales Order {0}-{1} with the message: {2}",
                                                soOrderRow.OrderType, soOrderRow.RefNbr, e.Message);
                            graphSOOrderEntry.Clear(PXClearOption.ClearAll);
                            forceInstanciateGraph = true;
                        }
                    }
                }
                else if (fsPostDoc.PostedTO == ID.Batch_PostTo.AR)
                {
                    if (graphARInvoiceEntry == null || forceInstanciateGraph == true)
                    {
                        graphARInvoiceEntry = (ARInvoiceEntry)InvoicingFunctions.CreateInvoiceGraph(fsPostDoc.PostedTO).GetGraph();
                        forceInstanciateGraph = false;
                    }

                    ARInvoice arInvoiceRow = graphARInvoiceEntry.Document.Current = graphARInvoiceEntry.Document.Search<ARInvoice.refNbr>(fsPostDoc.PostRefNbr, fsPostDoc.PostDocType);
                    if (arInvoiceRow != null && arInvoiceRow.IsTaxValid == false && graphARInvoiceEntry.IsExternalTax(arInvoiceRow.TaxZoneID) == true)
                    {
                        graphARInvoiceEntry.Document.Update(graphARInvoiceEntry.Document.Current);
                        try
                        {
                            graphARInvoiceEntry.Save.Press();
                        }
                        catch (Exception e)
                        {
                            PXTrace.WriteError("Error trying to calculate external taxes for the AR Invoice {0}-{1} with the message: {2}",
                                                arInvoiceRow.DocType, arInvoiceRow.RefNbr, e.Message);
                            graphARInvoiceEntry.Clear(PXClearOption.ClearAll);
                            forceInstanciateGraph = true;
                        }
                    }
                }
                else if (fsPostDoc.PostedTO == ID.Batch_PostTo.AP)
                {
                    if (graphAPInvoiceEntry == null || forceInstanciateGraph == true)
                    {
                        graphAPInvoiceEntry = (APInvoiceEntry)InvoicingFunctions.CreateInvoiceGraph(fsPostDoc.PostedTO).GetGraph();
                        forceInstanciateGraph = false;
                    }

                    APInvoice apInvoiceRow = graphAPInvoiceEntry.Document.Current = graphAPInvoiceEntry.Document.Search<APInvoice.refNbr>(fsPostDoc.PostRefNbr, fsPostDoc.PostDocType);
                    if (apInvoiceRow != null && apInvoiceRow.IsTaxValid == false && graphAPInvoiceEntry.IsExternalTax(apInvoiceRow.TaxZoneID) == true)
                    {
                        graphAPInvoiceEntry.Document.Update(graphAPInvoiceEntry.Document.Current);
                        try
                        {
                            graphAPInvoiceEntry.Save.Press();
                        }
                        catch (Exception e)
                        {
                            PXTrace.WriteError("Error trying to calculate external taxes for the AP Bill {0}-{1} with the message: {2}",
                                                apInvoiceRow.DocType, apInvoiceRow.RefNbr, e.Message);
                            graphAPInvoiceEntry.Clear(PXClearOption.ClearAll);
                            forceInstanciateGraph = true;
                        }
                    }
                }
            }
        }

        protected virtual string GetGroupKey(TPostLine postLineRow)
        {
            if (groupKey == null)
            {
                groupKey = new StringBuilder();
            }
            else
            {
                groupKey.Clear();
            }

            groupKey.Append(postLineRow.BranchID.ToString()
                            + "|" + postLineRow.BillCustomerID.ToString()
                            + "|" + postLineRow.CuryID.ToString()
                            + "|" + (postLineRow.TaxZoneID == null ? "" : postLineRow.TaxZoneID.ToString())
                            + "[" + (postLineRow.BillingCycleType == null ? string.Empty : postLineRow.BillingCycleType.ToString()) + "]");

            if (postLineRow.ProjectID != null
                    && ProjectDefaultAttribute.IsNonProject(postLineRow.ProjectID) == false)
            {
                groupKey.Append(postLineRow.ProjectID.ToString() + "|");
            }

            string billLocationID = postLineRow.GroupBillByLocations == true ? postLineRow.BillLocationID.ToString() : string.Empty;

            if (postLineRow.BillingCycleType == ID.Billing_Cycle_Type.APPOINTMENT)
            {
                groupKey.Append(postLineRow.AppointmentID.ToString());
            }
            else if (postLineRow.BillingCycleType == ID.Billing_Cycle_Type.SERVICE_ORDER)
            {
                groupKey.Append(postLineRow.SOID.ToString());
            }
            else if (postLineRow.BillingCycleType == ID.Billing_Cycle_Type.TIME_FRAME)
            {
                groupKey.Append(billLocationID);
            }
            else if (postLineRow.BillingCycleType == ID.Billing_Cycle_Type.PURCHASE_ORDER)
            {
                string custPORefNbr = postLineRow.CustPORefNbr == null ? string.Empty : postLineRow.CustPORefNbr.Trim();
                groupKey.Append(custPORefNbr + "|" + billLocationID);
            }
            else if (postLineRow.BillingCycleType == ID.Billing_Cycle_Type.WORK_ORDER)
            {
                string custWorkOrderRefNbr = postLineRow.CustWorkOrderRefNbr == null ? string.Empty : postLineRow.CustWorkOrderRefNbr.Trim();
                groupKey.Append(custWorkOrderRefNbr + "|" + billLocationID);
            }
            else
            {
                throw new PXException(TX.Error.BILLING_CYCLE_TYPE_NOT_VALID);
            }

            return groupKey.ToString();
        }

        protected virtual void CreatePostingBatchesForBillingCycle(Guid currentProcessID, int billingCycleID, CreateInvoiceFilter filter, List<TPostLine> postLineRows, JobExecutor<InvoicingProcessStepGroupShared> jobExecutor, PXQuickProcess.ActionFlow quickProcessFlow)
        {
            PXResultset<FSPostDoc> billingCycleOptionsGroups =
                                PXSelectGroupBy<FSPostDoc,
                                Where<
                                    FSPostDoc.processID, Equal<Required<FSPostDoc.processID>>,
                                    And<FSPostDoc.billingCycleID, Equal<Required<FSPostDoc.billingCycleID>>>>,
                                Aggregate<
                                    GroupBy<FSPostDoc.groupKey>>,
                                OrderBy<
                                    Asc<FSPostDoc.groupKey>>>
                                .Select(this, currentProcessID, billingCycleID);
            
            if (filter.PostTo == ID.Batch_PostTo.AR_AP)
            {
                CreatePostingBatches_ARAP(currentProcessID, billingCycleID, filter, billingCycleOptionsGroups, postLineRows, jobExecutor, quickProcessFlow);
            }
            else if ((filter.PostTo == ID.Batch_PostTo.SO || filter.PostTo == ID.Batch_PostTo.SI) 
                        && PXAccess.FeatureInstalled<FeaturesSet.distributionModule>())
            {
                var soInvoiceList = new List<FSPostDoc>();
                decimal? invoiceTotal = 0;

                foreach (FSPostDoc billingCycleOptionsGroup in billingCycleOptionsGroups)
                {
                    GetInvoiceLines(currentProcessID, billingCycleID, billingCycleOptionsGroup.GroupKey, true, out invoiceTotal, filter.PostTo);

                    if (invoiceTotal < 0)
                    {
                        billingCycleOptionsGroup.InvtMult = -1;
                    }
                    else
                    {
                        billingCycleOptionsGroup.InvtMult = 1;
                    }

                    soInvoiceList.Add(billingCycleOptionsGroup);
                }

                Job job = CreatePostingBatchAndInvoicesJob(currentProcessID, billingCycleID, filter.UpToDate, filter.InvoiceDate, filter.InvoiceFinPeriodID, filter.PostTo, soInvoiceList, postLineRows, jobExecutor.MainContext, quickProcessFlow);
                jobExecutor.JobList.Add(job);
            }
        }

        public abstract List<DocLineExt> GetInvoiceLines(Guid currentProcessID, int billingCycleID, string groupKey, bool getOnlyTotal, out decimal? invoiceTotal, string postTo);

        public static void UpdateUserSelection(PXGraph graph, FSCreatedDoc fsCreatedDocRow, Guid currentProcessID, int? billingCycleID, string groupKey)
        {
            PXUpdate<
                Set<FSPostDoc.batchID, Required<FSPostDoc.batchID>,
                Set<FSPostDoc.postedTO, Required<FSPostDoc.postedTO>,
                Set<FSPostDoc.postDocType, Required<FSPostDoc.postDocType>,
                Set<FSPostDoc.postRefNbr, Required<FSPostDoc.postRefNbr>>>>>,
            FSPostDoc,
            Where<
                FSPostDoc.processID, Equal<Required<FSPostDoc.processID>>,
                And<FSPostDoc.billingCycleID, Equal<Required<FSPostDoc.billingCycleID>>,
                And<FSPostDoc.groupKey, Equal<Required<FSPostDoc.groupKey>>>>>>
            .Update(
                    graph,
                    fsCreatedDocRow.BatchID,
                    fsCreatedDocRow.PostTo,
                    fsCreatedDocRow.CreatedDocType,
                    fsCreatedDocRow.CreatedRefNbr,
                    currentProcessID,
                    billingCycleID,
                    groupKey);
        }

        public static void CreatePostRegister(PXGraph graph, List<DocLineExt> docLinesWithPostInfo, FSCreatedDoc fsCreatedDocRow, Guid currentProcessID)
        {
            PXCache<FSPostRegister> cacheFSPostRegister = new PXCache<FSPostRegister>(graph);
            List<DocLineExt> Docs = docLinesWithPostInfo.GroupBy(r => r.docLine.DocID).Select(g => g.First()).ToList();

            foreach (var row in Docs)
            {
                FSPostRegister fsPostRegisterRow = new FSPostRegister();

                fsPostRegisterRow.SrvOrdType = row.fsAppointment == null ? row.fsServiceOrder.SrvOrdType : row.fsAppointment.SrvOrdType;
                fsPostRegisterRow.RefNbr = row.fsAppointment == null ? row.fsServiceOrder.RefNbr : row.fsAppointment.RefNbr;
                fsPostRegisterRow.Type = ID.PostRegister_Type.INVOICE_POST;
                fsPostRegisterRow.BatchID = fsCreatedDocRow.BatchID;
                fsPostRegisterRow.EntityType = ID.PostDoc_EntityType.SERVICE_ORDER;
                fsPostRegisterRow.ProcessID = currentProcessID;
                fsPostRegisterRow.PostedTO = fsCreatedDocRow.PostTo;
                fsPostRegisterRow.PostDocType = fsCreatedDocRow.CreatedDocType;
                fsPostRegisterRow.PostRefNbr = fsCreatedDocRow.CreatedRefNbr;

                cacheFSPostRegister.Insert(fsPostRegisterRow);
            }

            cacheFSPostRegister.Persist(PXDBOperation.Insert);
        }

        /// <summary>
        /// This old version of the method is keeped only to avoid a breaking change in the minor update
        /// </summary>
        [Obsolete("Use the version of this method that receives the FSCreatedDoc parameter instead of this one.")]
        public virtual void UpdatePostInfoAndPostDet(List<DocLineExt> docLinesWithPostInfo, FSPostBatch fsPostBatchRow, PostInfoEntry graphPostInfoEntry, PXCache<FSPostDet> cacheFSPostDet)
        {
            UpdatePostInfoAndPostDet(docLinesWithPostInfo, fsPostBatchRow, graphPostInfoEntry, cacheFSPostDet, null);
        }

        public virtual void UpdatePostInfoAndPostDet(List<DocLineExt> docLinesWithPostInfo, FSPostBatch fsPostBatchRow, PostInfoEntry graphPostInfoEntry, PXCache<FSPostDet> cacheFSPostDet, FSCreatedDoc fsCreatedDocRow)
        {
            IDocLine docLine = null;
            FSPostDoc fsPostDocRow = null;
            FSPostInfo fsPostInfoRow = null;
            FSPostDet fsPostDetRow = null;
            bool insertingPostInfo;

            SOLine soLineRow = null;
            ARTran arTranRow = null;
            APTran apTranRow = null;

            foreach (DocLineExt docLineExt in docLinesWithPostInfo)
            {
                docLine = docLineExt.docLine;
                fsPostDocRow = docLineExt.fsPostDoc;
                fsPostInfoRow = docLineExt.fsPostInfo;

                fsPostDetRow = new FSPostDet();

                if (fsPostInfoRow == null || fsPostInfoRow.PostID == null)
                {
                    fsPostInfoRow = new FSPostInfo();
                    insertingPostInfo = true;
                }
                else
                {
                    insertingPostInfo = false;
                }

                if (fsPostDocRow.DocLineRef is SOLine)
                {
                    soLineRow = (SOLine)fsPostDocRow.DocLineRef;
                    fsPostInfoRow.SOPosted = true;

                    if (fsCreatedDocRow == null)
                    {
                    fsPostInfoRow.SOOrderType = soLineRow.OrderType;
                    fsPostInfoRow.SOOrderNbr = soLineRow.OrderNbr;
                    }
                    else
                    {
                        fsPostInfoRow.SOOrderType = fsCreatedDocRow.CreatedDocType;
                        fsPostInfoRow.SOOrderNbr = fsCreatedDocRow.CreatedRefNbr;
                    }

                    fsPostInfoRow.SOLineNbr = soLineRow.LineNbr;
                    
                    fsPostDetRow.SOPosted = fsPostInfoRow.SOPosted;
                    fsPostDetRow.SOOrderType = fsPostInfoRow.SOOrderType;
                    fsPostDetRow.SOOrderNbr = fsPostInfoRow.SOOrderNbr;
                    fsPostDetRow.SOLineNbr = fsPostInfoRow.SOLineNbr;
                }
                else if (fsPostDocRow.DocLineRef is ARTran 
                            && (fsPostBatchRow.PostTo == ID.Batch_PostTo.AR_AP || fsPostBatchRow.PostTo == ID.Batch_PostTo.AR))
                {
                    arTranRow = (ARTran)fsPostDocRow.DocLineRef;

                    fsPostInfoRow.ARPosted = true;

                    if (fsCreatedDocRow == null)
                    {
                    fsPostInfoRow.ARDocType = arTranRow.TranType;
                    fsPostInfoRow.ARRefNbr = arTranRow.RefNbr;
                    }
                    else
                    {
                        fsPostInfoRow.ARDocType = fsCreatedDocRow.CreatedDocType;
                        fsPostInfoRow.ARRefNbr = fsCreatedDocRow.CreatedRefNbr;
                    }

                    fsPostInfoRow.ARLineNbr = arTranRow.LineNbr;
                    
                    fsPostDetRow.ARPosted = fsPostInfoRow.ARPosted;
                    fsPostDetRow.ARDocType = fsPostInfoRow.ARDocType;
                    fsPostDetRow.ARRefNbr = fsPostInfoRow.ARRefNbr;
                    fsPostDetRow.ARLineNbr = fsPostInfoRow.ARLineNbr;
                }
                else if (fsPostDocRow.DocLineRef is ARTran
                            && fsPostBatchRow.PostTo == ID.Batch_PostTo.SI)
                {
                    arTranRow = (ARTran)fsPostDocRow.DocLineRef;

                    fsPostInfoRow.SOInvPosted = true;
                    fsPostInfoRow.SOInvDocType = arTranRow.TranType;
                    fsPostInfoRow.SOInvRefNbr = arTranRow.RefNbr;
                    fsPostInfoRow.SOInvLineNbr = arTranRow.LineNbr;

                    fsPostDetRow.SOInvPosted = fsPostInfoRow.SOInvPosted;
                    fsPostDetRow.SOInvDocType = fsPostInfoRow.SOInvDocType;
                    fsPostDetRow.SOInvRefNbr = fsPostInfoRow.SOInvRefNbr;
                    fsPostDetRow.SOInvLineNbr = fsPostInfoRow.SOInvLineNbr;
                }
                else if (fsPostDocRow.DocLineRef is APTran)
                {
                    apTranRow = (APTran)fsPostDocRow.DocLineRef;

                    fsPostInfoRow.APPosted = true;

                    if (fsCreatedDocRow == null)
                    {
                    fsPostInfoRow.APDocType = apTranRow.TranType;
                    fsPostInfoRow.APRefNbr = apTranRow.RefNbr;
                    }
                    else
                    {
                        fsPostInfoRow.APDocType = fsCreatedDocRow.CreatedDocType;
                        fsPostInfoRow.APRefNbr = fsCreatedDocRow.CreatedRefNbr;
                    }

                    fsPostInfoRow.APLineNbr = apTranRow.LineNbr;

                    fsPostDetRow.APPosted = fsPostInfoRow.APPosted;
                    fsPostDetRow.APDocType = fsPostInfoRow.APDocType;
                    fsPostDetRow.APRefNbr = fsPostInfoRow.APRefNbr;
                    fsPostDetRow.APLineNbr = fsPostInfoRow.APLineNbr;
                }

                fsPostInfoRow.SOID = docLine.DocID;

                if (insertingPostInfo == true)
                {
                    graphPostInfoEntry.PostInfoRecords.Current = graphPostInfoEntry.PostInfoRecords.Insert(fsPostInfoRow);
                }
                else
                {
                    graphPostInfoEntry.PostInfoRecords.Current = graphPostInfoEntry.PostInfoRecords.Update(fsPostInfoRow);
                }

                graphPostInfoEntry.Save.Press();
                fsPostInfoRow = graphPostInfoEntry.PostInfoRecords.Current;
                
                fsPostDetRow.BatchID = fsPostBatchRow.BatchID;
                fsPostDetRow.PostID = fsPostInfoRow.PostID;
                
                cacheFSPostDet.Insert(fsPostDetRow);

                if (insertingPostInfo == true)
                {
                    if (docLine.SourceTable == ID.TablePostSource.FSAPPOINTMENT_DET)
                    {
                        PXUpdate<
                                Set<FSAppointmentDet.postID, Required<FSAppointmentDet.postID>>,
                            FSAppointmentDet,
                            Where<
                                FSAppointmentDet.appDetID, Equal<Required<FSAppointmentDet.appDetID>>>>
                        .Update(cacheFSPostDet.Graph, fsPostInfoRow.PostID, docLine.LineID);
                    }
                    else if (docLine.SourceTable == ID.TablePostSource.FSSO_DET)
                    {
                        PXUpdate<
                                Set<FSSODet.postID, Required<FSSODet.postID>>,
                            FSSODet,
                            Where<
                                FSSODet.sODetID, Equal<Required<FSSODet.sODetID>>>>
                        .Update(cacheFSPostDet.Graph, fsPostInfoRow.PostID, docLine.LineID);
                    }
                }

                UpdateSourcePostDoc(cacheFSPostDet, fsPostBatchRow, fsPostDocRow);
            }

            cacheFSPostDet.Persist(PXDBOperation.Insert);
        }
        #endregion

        public abstract void UpdateSourcePostDoc(PXCache<FSPostDet> cacheFSPostDet, FSPostBatch fsPostBatchRow, FSPostDoc fsPostDocRow);

        #region Protected Methods
        protected virtual void IncludeReviewInvoiceBatchesAction()
        {
            var fsPostBatchRows = PXSelect<FSPostBatch, Where<FSPostBatch.status, Equal<FSPostBatch.status.temporary>>>.SelectWindowed(this, 0, 1);

            if (fsPostBatchRows.Count == 0)
            {
                openReviewTemporaryBatch.SetVisible(false);
            }
            else
            {
                openReviewTemporaryBatch.SetVisible(true);
            }
        }

        protected virtual void HideOrShowInvoiceActions(PXCache cache, CreateInvoiceFilter createInvoiceFilterRow)
        {
            bool postToSO = createInvoiceFilterRow.PostTo == ID.Batch_PostTo_Filter.SO;

            // @TODO: Temporary hide AP/AR actions until will be developed
            bool postToAPAR = createInvoiceFilterRow.PostTo == ID.Batch_PostTo_Filter.AR_AP & false;

            PXUIFieldAttribute.SetVisible<CreateInvoiceFilter.prepareInvoice>(cache, createInvoiceFilterRow, postToSO);
            PXUIFieldAttribute.SetVisible<CreateInvoiceFilter.emailSalesOrder>(cache, createInvoiceFilterRow, postToSO);
            PXUIFieldAttribute.SetVisible<CreateInvoiceFilter.sOQuickProcess>(cache, createInvoiceFilterRow, postToSO);
            PXUIFieldAttribute.SetVisible<CreateInvoiceFilter.releaseInvoice>(cache, createInvoiceFilterRow, postToAPAR || postToSO);
            PXUIFieldAttribute.SetVisible<CreateInvoiceFilter.emailInvoice>(cache, createInvoiceFilterRow, postToAPAR);
            PXUIFieldAttribute.SetVisible<CreateInvoiceFilter.releaseBill>(cache, createInvoiceFilterRow, postToAPAR);
            PXUIFieldAttribute.SetVisible<CreateInvoiceFilter.payBill>(cache, createInvoiceFilterRow, postToAPAR);
        }
        #endregion

        public static void DeallocateServiceOrders(ServiceOrderEntry docgraph, List<DocLineExt> docLines, bool ProcessRepeatedLines = false)
        {
            IEnumerable<IGrouping<int?, DocLineExt>> OrderList = docLines.Where(x => x.fsSODet != null).GroupBy(x => x.fsServiceOrder.SOID);

            foreach (IGrouping<int?, DocLineExt> order in OrderList)
            {
                List<DocLineExt> orderLines = order.OrderBy(x => x.fsSODet.SODetID).ThenByDescending(x => x.docLine.LotSerialNbr).ToList();
                FSServiceOrder fsServiceOrderRow = orderLines.First().fsServiceOrder;

                docgraph.Clear();
                FSServiceOrder currentServiceOrder = docgraph.ServiceOrderRecords.Current = docgraph.ServiceOrderRecords.Search<FSServiceOrder.refNbr>(fsServiceOrderRow.RefNbr, fsServiceOrderRow.SrvOrdType);

                if (currentServiceOrder.SrvOrdType != fsServiceOrderRow .SrvOrdType || currentServiceOrder.RefNbr != fsServiceOrderRow.RefNbr)
                {
                    throw new PXException(TX.Error.SERVICE_ORDER_NOT_FOUND);
                }

                int? SODetID = null;
                int? LineID = null;

                foreach (DocLineExt line in orderLines)
                {
                    if (ProcessRepeatedLines == false && SODetID == line.fsSODet.SODetID && LineID == line.docLine.LineID)
                    {
                        continue;
                    }

                    SODetID = line.fsSODet.SODetID;
                    LineID = line.docLine.LineID;

                    if (line.fsSODetSplit?.SplitLineNbr == null)
                    {
                        continue;
                    }

                    if (line.fsSODet.LineType == ID.LineType_All.INVENTORY_ITEM)
                    {
                        decimal? baseDeallocationQty = line.docLine.GetBaseQty(FieldType.BillableField);

                        FSSODetPart partLine = docgraph.ServiceOrderDetParts.Current = docgraph.ServiceOrderDetParts.Search<FSSODetPart.sODetID>(SODetID);
                        if (partLine.SODetID != line.fsSODet.SODetID)
                        {
                            throw new PXException(TX.Error.RECORD_NOT_FOUND);
                        }

                        foreach (FSSODetPartSplit partSplit in docgraph.partSplits.Select()?
                                    .Where(x => ((FSSODetPartSplit)x).Completed == false 
                                            && (
                                                ((FSSODetPartSplit)x).LotSerialNbr == line.docLine.LotSerialNbr
                                                || (string.IsNullOrEmpty(line.docLine.LotSerialNbr) 
                                                        && string.IsNullOrEmpty(((FSSODetPartSplit)x).LotSerialNbr)))))
                        {
                            baseDeallocationQty = DeallocateSplitLine<FSSODetPart>(docgraph, line, baseDeallocationQty, null, null, partLine, partSplit);
                            if (baseDeallocationQty <= 0)
                            {
                                break;
                            }
                        }

                        if (baseDeallocationQty > 0)
                        {
                            foreach (FSSODetPartSplit partSplit in docgraph.partSplits.Select()?.Where(x => ((FSSODetPartSplit)x).Completed == false))
                            {
                                baseDeallocationQty = DeallocateSplitLine<FSSODetPart>(docgraph, line, baseDeallocationQty, null, null, partLine, partSplit);
                                if (baseDeallocationQty <= 0)
                                {
                                    break;
                                }
                            }
                        }
                    }
                    else if (line.fsSODet.LineType == ID.LineType_All.SERVICE
                            || line.fsSODet.LineType == ID.LineType_All.NONSTOCKITEM)
                    {
                        decimal? baseDeallocationQty = line.fsSODet.BaseOpenQty;

                        FSSODetService serviceLine = docgraph.ServiceOrderDetServices.Current = docgraph.ServiceOrderDetServices.Search<FSSODetService.sODetID>(SODetID);
                        if (serviceLine.SODetID != line.fsSODet.SODetID)
                        {
                            throw new PXException(TX.Error.RECORD_NOT_FOUND);
                        }

                        if (baseDeallocationQty > 0)
                        {
                            foreach (FSSODetServiceSplit serviceSplit in docgraph.serviceSplits.Select()?.Where(x => ((FSSODetServiceSplit)x).Completed == false))
                            {
                                baseDeallocationQty = DeallocateSplitLine<FSSODetService>(docgraph, line, baseDeallocationQty, serviceLine, serviceSplit, null, null);
                                if (baseDeallocationQty <= 0)
                                {
                                    break;
                                }
                            }
                        }
                    }
                }

                docgraph.SelectTimeStamp();
                docgraph.SkipTaxCalcAndSave();
            }
        }

        protected static decimal? DeallocateSplitLine<FSSODetType>(ServiceOrderEntry docgraph, DocLineExt line, decimal? baseDeallocationQty,
                                    FSSODetService serviceLine, FSSODetServiceSplit serviceSplit, FSSODetPart partLine, FSSODetPartSplit partSplit)
            where FSSODetType : FSSODet
        {

            if (baseDeallocationQty <= 0)
            {
                return 0;
            }

            FSSODetSplit genericSplit = null;
            PXCache genericSplitCache = null;
            if (partSplit != null)
            {
                genericSplitCache = docgraph.partSplits.Cache;
                genericSplit = partSplit;
            }
            else
            {
                genericSplitCache = docgraph.serviceSplits.Cache;
                genericSplit = serviceSplit;
            }

            if (genericSplit.Completed == true)
            {
                return baseDeallocationQty;
            }

            if (String.IsNullOrEmpty(line.docLine.LotSerialNbr) == false
                                && genericSplit.LotSerialNbr != line.docLine.LotSerialNbr)
            {
                return baseDeallocationQty;
            }

            if (line.docLine.SiteID != null && line.docLine.SiteID != genericSplit.SiteID)
            {
                return baseDeallocationQty;
            }

            if (line.docLine.SiteLocationID != null && line.docLine.SiteLocationID != genericSplit.LocationID)
            {
                return baseDeallocationQty;
            }

            if (genericSplit.Completed == true)
            {
                return 0;
            }

            PXRowUpdating cancel_handler = new PXRowUpdating((sender, e) => { e.Cancel = true; });
            docgraph.RowUpdating.AddHandler<FSSODetPart>(cancel_handler);

            decimal? baseOpenQty = genericSplit.BaseQty - genericSplit.BaseShippedQty;

            if (baseOpenQty <= baseDeallocationQty)
            {
                genericSplit.BaseShippedQty += baseOpenQty;

                genericSplit.ShippedQty = INUnitAttribute.ConvertFromBase(genericSplitCache, genericSplit.InventoryID, genericSplit.UOM, (decimal)genericSplit.BaseShippedQty, INPrecision.QUANTITY);
                genericSplit.Completed = true;

                if (partSplit != null)
                {
                    docgraph.lsPartSelect.SuppressedMode = true;
                    genericSplit = partSplit = docgraph.partSplits.Update(partSplit);
                    docgraph.lsPartSelect.SuppressedMode = false;
                }
                else
                {
                    docgraph.lsServiceSelect.SuppressedMode = true;
                    genericSplit = serviceSplit = docgraph.serviceSplits.Update(serviceSplit);
                    docgraph.lsServiceSelect.SuppressedMode = false;
                }

                docgraph.Caches[typeof(INItemPlan)].Delete(line.inItemPlan);
                genericSplit.PlanID = null;

                baseDeallocationQty -= baseOpenQty;
            }
            else
            {
                genericSplit.BaseQty = genericSplit.BaseShippedQty = baseDeallocationQty;

                genericSplit.ShippedQty = INUnitAttribute.ConvertFromBase(genericSplitCache, genericSplit.InventoryID, genericSplit.UOM, (decimal)genericSplit.BaseShippedQty, INPrecision.QUANTITY);
                genericSplit.Qty = genericSplit.ShippedQty;

                genericSplit.Completed = true;

                if (partSplit != null)
                {
                    docgraph.lsPartSelect.SuppressedMode = true;
                    genericSplit = partSplit = docgraph.partSplits.Update(partSplit);
                    docgraph.lsPartSelect.SuppressedMode = false;
                }
                else
                {
                    docgraph.lsServiceSelect.SuppressedMode = true;
                    genericSplit = serviceSplit = docgraph.serviceSplits.Update(serviceSplit);
                    docgraph.lsServiceSelect.SuppressedMode = false;
                }

                docgraph.Caches[typeof(INItemPlan)].Delete(line.inItemPlan);
                genericSplit.PlanID = null;

                FSSODetSplit newSplit = null;
                FSSODetPartSplit newPartSplit = null;
                FSSODetServiceSplit newServiceSplit = null;

                if (partSplit != null)
                {
                    newPartSplit = PXCache<FSSODetPartSplit>.CreateCopy(partSplit);
                    newSplit = newPartSplit;
                }
                else
                {
                    newServiceSplit = PXCache<FSSODetServiceSplit>.CreateCopy(serviceSplit);
                    newSplit = newServiceSplit;
                }

                newSplit.PlanID = null;
                newSplit.PlanType = genericSplit.PlanType;
                newSplit.ParentSplitLineNbr = newSplit.SplitLineNbr;
                newSplit.SplitLineNbr = null;
                newSplit.IsAllocated = false;
                newSplit.Completed = false;
                newSplit.ShipmentNbr = null;
                newSplit.LotSerialNbr = null;
                //clear PO references
                newSplit.POCreate = false;
                newSplit.POCompleted = false;
                newSplit.POSource = null;
                newSplit.POType = null;
                newSplit.PONbr = null;
                newSplit.POLineNbr = null;
                newSplit.POReceiptType = null;
                newSplit.POReceiptNbr = null;
                newSplit.VendorID = null;
                //clear SO references
                newSplit.SOOrderType = null;
                newSplit.SOOrderNbr = null;
                newSplit.SOLineNbr = null;
                newSplit.SOSplitLineNbr = null;
                newSplit.RefNoteID = null;

                newSplit.BaseReceivedQty = 0m;
                newSplit.ReceivedQty = 0m;
                newSplit.BaseShippedQty = 0m;
                newSplit.ShippedQty = 0m;

                newSplit.BaseQty = baseOpenQty - baseDeallocationQty;

                newSplit.Qty = INUnitAttribute.ConvertFromBase(genericSplitCache, newSplit.InventoryID, newSplit.UOM, (decimal)newSplit.BaseQty, INPrecision.QUANTITY);

                if (partSplit != null)
                {
                    docgraph.partSplits.Insert(newPartSplit);
                }
                else
                {
                    docgraph.serviceSplits.Insert(newServiceSplit);
                }

                baseDeallocationQty = 0;
            }
            docgraph.RowUpdating.RemoveHandler<FSSODetPart>(cancel_handler);

            ConfirmSingleLine<FSSODetType>(docgraph, genericSplit, serviceLine, partLine);

            return baseDeallocationQty;
        }

        protected static void ConfirmSingleLine<FSSODetType>(ServiceOrderEntry docgraph, FSSODetSplit shipline,
                                    FSSODetService serviceLine, FSSODetPart partLine)
            where FSSODetType : FSSODet
        {
            FSSODet genericLine = null;

            if (partLine != null)
            {
                genericLine = partLine;
                docgraph.lsPartSelect.SuppressedMode = true;
            }
            else
            {
                genericLine = serviceLine;
                docgraph.lsServiceSelect.SuppressedMode = true;
            }

            if (genericLine.BaseShippedQty < genericLine.BaseOrderQty && genericLine.IsFree == false)
            {
                genericLine.BaseShippedQty += shipline.BaseShippedQty;
                genericLine.ShippedQty += shipline.ShippedQty;
                genericLine.OpenQty = genericLine.OrderQty - genericLine.ShippedQty;
                genericLine.BaseOpenQty = genericLine.BaseOrderQty - genericLine.BaseShippedQty;
                genericLine.ClosedQty = genericLine.ShippedQty;
                genericLine.BaseClosedQty = genericLine.BaseShippedQty;

                docgraph.Caches[typeof(FSSODetType)].Update(genericLine);
            }
            else
            {
                genericLine.OpenQty = 0m;
                genericLine.ClosedQty = genericLine.OrderQty;
                genericLine.ShippedQty += shipline.ShippedQty;
                genericLine.BaseOpenQty = genericLine.BaseOrderQty - genericLine.BaseShippedQty;
                genericLine.BaseClosedQty = genericLine.BaseOrderQty;
                genericLine.Completed = true;

                PXCache cache = docgraph.Caches[typeof(FSSODetType)];
                cache.Update(genericLine);

                if (partLine != null)
                {
                    docgraph.lsPartSelect.CompleteSchedules(cache, partLine);
                }
                else
                {
                    docgraph.lsServiceSelect.CompleteSchedules(cache, serviceLine);
                }
            }

            if (partLine != null)
            {
                docgraph.lsPartSelect.SuppressedMode = false;
            }
            else
            {
                docgraph.lsServiceSelect.SuppressedMode = false;
            }
        }

        /*
        protected virtual void ConfirmSingleLine(ServiceOrderEntry docgraph, SOLine line, SOShipLine shipline, string lineShippingRule, ref bool backorderExists)
        {
            docgraph.lsselect.SuppressedMode = true;

            if (line.IsFree == true && line.ManualDisc == false)
            {
                if (!backorderExists)
                {
                    line.OpenQty = 0m;
                    line.Completed = true;
                    line.ClosedQty = line.OrderQty;
                    line.BaseClosedQty = line.BaseOrderQty;
                    line.OpenLine = false;

                    PXCache cache = docgraph.Caches[typeof(SOLine)];
                    cache.Update(line);
                    docgraph.lsselect.CompleteSchedules(cache, line);
                }
                else if (line.BaseShippedQty <= line.BaseOrderQty * line.CompleteQtyMin / 100m)
                {
                    line.OpenQty = line.OrderQty - line.ShippedQty;
                    line.BaseOpenQty = line.BaseOrderQty - line.BaseShippedQty;
                    line.ClosedQty = line.ShippedQty;
                    line.BaseClosedQty = line.BaseShippedQty;

                    docgraph.Caches[typeof(SOLine)].Update(line);
                }
            }
            else
            {
                if (lineShippingRule == SOShipComplete.BackOrderAllowed && line.BaseShippedQty < line.BaseOrderQty * line.CompleteQtyMin / 100m)
                {
                    line.OpenQty = line.OrderQty - line.ShippedQty;
                    line.BaseOpenQty = line.BaseOrderQty - line.BaseShippedQty;
                    line.ClosedQty = line.ShippedQty;
                    line.BaseClosedQty = line.BaseShippedQty;

                    docgraph.Caches[typeof(SOLine)].Update(line);

                    backorderExists = true;
                }
                else if (shipline.ShipmentNbr != null || lineShippingRule != SOShipComplete.ShipComplete)
                {
                    //Completed will be true for orders with locations enabled which requireshipping. check DefaultAttribute
                    if (line.OpenLine == true)
                    {
                        docgraph.Document.Current.OpenLineCntr--;
                    }

                    if (docgraph.Document.Current.OpenLineCntr <= 0)
                    {
                        docgraph.Document.Current.Completed = true;
                    }

                    line.OpenQty = 0m;
                    line.ClosedQty = line.OrderQty;
                    line.BaseClosedQty = line.BaseOrderQty;
                    line.OpenLine = false;
                    line.Completed = true;

                    if (lineShippingRule == SOShipComplete.CancelRemainder || line.BaseShippedQty >= line.BaseOrderQty * line.CompleteQtyMin / 100m)
                    {
                        line.UnbilledQty -= (line.OrderQty - line.ShippedQty);
                    }

                    PXCache cache = docgraph.Caches[typeof(SOLine)];
                    cache.Update(line);
                    docgraph.lsselect.CompleteSchedules(cache, line);
                }
            }
            docgraph.lsselect.SuppressedMode = false;
        }
        */
        #region Public Classes
        public class InvoicingParm : MethodParm
        {
            public string TargetScreen;
            public string BillingBy;

            public Guid CurrentProcessID;
            public int BillingCycleID;
            public string GroupKey;
            public PXQuickProcess.ActionFlow QuickProcessFlow;

            public List<TPostLine> PostLineRows;

            public short InvtMult;
            public int DocumentsQty;

            public DateTime? UpToDate = null;
            public DateTime? InvoiceDate = null;
            public string InvoiceFinPeriodID = null;

            public List<ErrorInfo> ErrorList = null;

            public InvoicingParm(Step myStep, ExecutionContext executionContext, string targetScreen, Guid currentProcessID, int billingCycleID, string groupKey, List<TPostLine> postLineRows, string billingBy)
                : base(myStep, executionContext)
            {
                TargetScreen = targetScreen;

                CurrentProcessID = currentProcessID;
                BillingCycleID = billingCycleID;
                GroupKey = groupKey;

                PostLineRows = postLineRows;

                InvtMult = 1;
                DocumentsQty = 0;
                BillingBy = billingBy;
            }

            public InvoicingParm(Step myStep, ExecutionContext executionContext, string module, Guid currentProcessID, int billingCycleID, string groupKey, List<TPostLine> postLineRows, string billingBy, short? invtMult, PXQuickProcess.ActionFlow quickProcessFlow)
                : base(myStep, executionContext)
            {
                TargetScreen = module;

                CurrentProcessID = currentProcessID;
                BillingCycleID = billingCycleID;
                GroupKey = groupKey;

                PostLineRows = postLineRows;

                if (invtMult != null)
                { 
                    InvtMult = (short)invtMult;
                }

                DocumentsQty = 0;
                BillingBy = billingBy;
                QuickProcessFlow = quickProcessFlow;
            }
        }

        public class PostBatchJobShared : Job.JobShared
        {
            public PostBatchEntry PostBatchEntryGraph;
            public FSPostBatch FSPostBatchRow;

            public override void Clear()
            {
                if (PostBatchEntryGraph != null)
                {
                    PostBatchEntryGraph.Clear(PXClearOption.ClearAll);
                }
            }

            public override void Dispose()
            {
                Clear();

                PostBatchEntryGraph = null;
                FSPostBatchRow = null;
            }
        }
        #endregion
    }

    public class InvoicingProcessStepGroupShared : StepGroup.StepGroupShared
    {
        public IInvoiceProcessGraph ProcessGraph;

        public IInvoiceGraph InvoiceGraph;
        public PXCache<FSCreatedDoc> CacheFSCreatedDoc;

        public PostInfoEntry PostInfoEntryGraph;
        public PXCache<FSPostDet> CacheFSPostDet;

        public ServiceOrderEntry ServiceOrderGraph;

        public virtual void Initialize(string targetScreen, string billingBy)
        {
            if (ProcessGraph == null)
            {
                ProcessGraph = InvoicingFunctions.CreateInvoiceProcessGraph(billingBy);
            }
            else
            {
                ProcessGraph.Clear(PXClearOption.ClearAll);
            }

            if (InvoiceGraph == null)
            {
                InvoiceGraph = InvoicingFunctions.CreateInvoiceGraph(targetScreen);
            }
            else
            {
                InvoiceGraph.Clear();
            }

            if (ServiceOrderGraph == null)
            {
                ServiceOrderGraph = PXGraph.CreateInstance<ServiceOrderEntry>();
            }
            else
            {
                ServiceOrderGraph.Clear();
            }

            if (CacheFSCreatedDoc == null)
            {
                CacheFSCreatedDoc = new PXCache<FSCreatedDoc>(ProcessGraph.GetGraph());
            }
            else
            {
                CacheFSCreatedDoc.Clear();
            }

            if (PostInfoEntryGraph == null)
            {
                PostInfoEntryGraph = PXGraph.CreateInstance<PostInfoEntry>();
            }
            else
            {
                PostInfoEntryGraph.Clear(PXClearOption.ClearAll);
            }

            if (CacheFSPostDet == null)
            {
                CacheFSPostDet = new PXCache<FSPostDet>(PostInfoEntryGraph);
            }
            else
            {
                CacheFSPostDet.Clear();
            }
        }

        public override void Clear()
        {
            if (ProcessGraph != null)
            {
                ProcessGraph.Clear(PXClearOption.ClearAll);
            }

            if (InvoiceGraph != null)
            {
                InvoiceGraph.Clear();
            }

            if (CacheFSCreatedDoc != null)
            {
                CacheFSCreatedDoc.Clear();
            }

            if (PostInfoEntryGraph != null)
            {
                PostInfoEntryGraph.Clear(PXClearOption.ClearAll);
            }

            if (CacheFSPostDet != null)
            {
                CacheFSPostDet.Clear();
            }
        }

        public override void Dispose()
        {
            Clear();

            ProcessGraph = null;
            InvoiceGraph = null;
            CacheFSCreatedDoc = null;
            PostInfoEntryGraph = null;
            CacheFSPostDet = null;
        }
    }
}
