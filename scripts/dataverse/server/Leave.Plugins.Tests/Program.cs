using System.Reflection;
using Leave.Plugins;
using Leave.BalanceGuards;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

if (args.Contains("--calendar-grant")) {
    CalendarGrantChecks.Run();
    AnnualGrantOperationChecks.Run();
    return;
}

var passed = 0;
void Check(string label, Action<Fixture> action) { var f = new Fixture(); action(f); passed++; Console.WriteLine("PASS " + label); }
void Equal(object a, object b) { if (!Equals(a,b)) throw new Exception($"Expected {b}; got {a}."); }
void Denied(Action action, Fixture f) { try { action(); } catch (InvalidPluginExecutionException) { Equal(f.HistoryCount,0); return; } throw new Exception("Operation was not denied."); }
void SetPaternity(Fixture f) {
    var type=f.LeaveType;
    f.Rows.Remove((type.LogicalName,type.Id));
    type.Id=PaternityApprovalRules.TypeId;type["cr2c9_leavetypename"]="Paternity Leave";
    f.Rows[(type.LogicalName,type.Id)]=type;
    f.Request["cr2c9_leavetypename"]=type.ToEntityReference();
}
void SetMarriage(Fixture f) {
    var type=f.LeaveType;
    f.Rows.Remove((type.LogicalName,type.Id));
    type.Id=MarriageApprovalRules.TypeId;type["cr2c9_leavetypename"]="Marriage Leave";
    f.Rows[(type.LogicalName,type.Id)]=type;
    f.Request["cr2c9_leavetypename"]=type.ToEntityReference();
}
void SetCompassionate(Fixture f) {
    var type=f.LeaveType;
    f.Rows.Remove((type.LogicalName,type.Id));
    type.Id=CompassionateApprovalRules.TypeId;type["cr2c9_leavetypename"]="Compassionate Leave";
    f.Rows[(type.LogicalName,type.Id)]=type;
    f.Request["cr2c9_leavetypename"]=type.ToEntityReference();
}
foreach (var table in new[] { "cr2c9_carryforwardreview", "cr2c9_carryforwardcredit", "cr2c9_carryforwardusage" })
foreach (var message in new[] { "Create", "Update", "Delete", "CreateMultiple", "UpdateMultiple" })
    Check("carry-forward storage rejects " + table + " " + message,
        f => Denied(() => f.Run(new CarryForwardWriteGuard(), message, new Entity(table, Guid.NewGuid())), f));
Equal(UniformAnnualSchedule.Dates(new DateTime(2026, 11, 20), new DateTime(2026, 11, 23)).Length, 2);
var standardCarry = CarryForwardDraft.Calculate(64, null, false);
Equal(standardCarry.CarryForwardHours, 40);
Equal(standardCarry.SeparatelyRecordedUnusedHours, 24);
var specialCarry = CarryForwardDraft.Calculate(64, 56, true);
Equal(specialCarry.CarryForwardHours, 56);
Equal(specialCarry.SeparatelyRecordedUnusedHours, 8);
Equal(CarryForwardDraft.Calculate(20, null, false).CarryForwardHours, 20);
try { CarryForwardDraft.Calculate(64, 56, false); throw new Exception("Unverified exception was accepted."); }
catch (ArgumentException) { }
try { CarryForwardDraft.Calculate(-4, null, false); throw new Exception("Negative unused leave was accepted."); }
catch (ArgumentException) { }
var closingEmployee = Guid.NewGuid();
var closingType = Guid.NewGuid();
var closingSource = new BalancePeriod(Guid.NewGuid(), closingEmployee, closingType,
    new DateTime(2026, 1, 1), new DateTime(2026, 12, 31));
var closingTarget = new BalancePeriod(Guid.NewGuid(), closingEmployee, closingType,
    new DateTime(2027, 1, 1), new DateTime(2027, 12, 31));
var closingRequest = Guid.NewGuid();
var closingHistory = new[] {
    new BalanceEntry(closingSource.Id, "earned-2026", BalanceEntryKind.Grant, 112),
    new BalanceEntry(closingSource.Id, "approved-2026", BalanceEntryKind.ApprovedLeave, -48, closingRequest)
};
var closingPreview = CarryForwardReview.Prepare(closingType, closingSource, closingTarget, closingHistory,
    null, false, new DateTime(2027, 1, 1));
Equal(closingPreview.OriginalCalculation.SourceUnusedHours, 64);
Equal(closingPreview.ReviewedCarryForwardHours, 40);
Equal(closingPreview.SeparatelyRecordedUnusedHours, 24);
var adjustedPreview = closingPreview.WithHrAdjustment(32, "Verified exception", "approval-document-id");
Equal(adjustedPreview.OriginalCalculation.CarryForwardHours, 40);
Equal(adjustedPreview.ReviewedCarryForwardHours, 32);
Equal(adjustedPreview.SeparatelyRecordedUnusedHours, 32);
Equal(adjustedPreview.OperationKey, closingPreview.OperationKey);
Equal(adjustedPreview.SourceLedgerHash, closingPreview.SourceLedgerHash);
var confirmation = CarryForwardConfirmation.Plan(adjustedPreview, closingHistory,
    Array.Empty<CarryForwardCredit>(), new DateTime(2027, 1, 3));
Equal(confirmation.CreditToPost.Hours, 32);
Equal(confirmation.SeparatelyRecordedUnusedHours, 32);
Equal(confirmation.UseBy, new DateTime(2027, 3, 31));
var postedCredit = confirmation.CreditToPost;
var postedCreditId = Guid.NewGuid();
var targetGrant = new BalanceEntry(closingTarget.Id, "earned-2027", BalanceEntryKind.Grant, 80);
var splitRequest = Guid.NewGuid();
var splitDates = new[] { new AnnualLeaveDate(new DateTime(2027, 3, 31), 8),
    new AnnualLeaveDate(new DateTime(2027, 4, 1), 8) };
var periodApproval = CarryForwardPeriodPlan.Approval(closingTarget,
    new[] { targetGrant }, 80, postedCreditId, postedCredit, new DateTime(2027, 3, 31),
    Array.Empty<CarryForwardUsageEntry>(), splitRequest, splitDates);
Equal(periodApproval.CarryEntry.Hours, 8);
Equal(periodApproval.AnnualEntry.Hours, -8);
var splitCancellation = Guid.NewGuid();
var periodReversal = CarryForwardPeriodPlan.Cancellation(closingTarget,
    new[] { targetGrant, periodApproval.AnnualEntry }, postedCreditId, postedCredit,
    new[] { periodApproval.CarryEntry }, splitRequest, splitCancellation, splitDates);
Equal(periodReversal.AnnualEntry.Hours, 8);
Equal(periodReversal.AnnualEntry.Reverses, periodApproval.AnnualEntry.OperationKey);
Equal(periodReversal.CarryEntry.Hours, 8);
Equal(periodReversal.CarryEntry.Reverses, periodApproval.CarryEntry.OperationKey);
try { CarryForwardPeriodPlan.Cancellation(closingTarget,
    new[] { targetGrant, periodApproval.AnnualEntry, periodReversal.AnnualEntry },
    postedCreditId, postedCredit, new[] { periodApproval.CarryEntry, periodReversal.CarryEntry },
    splitRequest, Guid.NewGuid(), splitDates);
    throw new Exception("Duplicate split cancellation was accepted."); }
catch (InvalidOperationException) { }
var carryOnlyRequest = Guid.NewGuid();
var carryOnlyDate = new[] { new AnnualLeaveDate(new DateTime(2027, 2, 1), 8) };
var carryOnly = CarryForwardPeriodPlan.Approval(closingTarget, new[] { targetGrant }, 80,
    postedCreditId, postedCredit, new DateTime(2027, 3, 31),
    Array.Empty<CarryForwardUsageEntry>(), carryOnlyRequest, carryOnlyDate);
Equal(carryOnly.AnnualEntry, null);
Equal(carryOnly.CarryEntry.Hours, 8);
var carryOnlyReversal = CarryForwardPeriodPlan.Cancellation(closingTarget,
    new[] { targetGrant }, postedCreditId, postedCredit, new[] { carryOnly.CarryEntry },
    carryOnlyRequest, Guid.NewGuid(), carryOnlyDate);
Equal(carryOnlyReversal.AnnualEntry, null);
Equal(carryOnlyReversal.CarryEntry.Hours, 8);
try { CarryForwardPeriodPlan.Cancellation(closingTarget,
    new[] { targetGrant, periodApproval.AnnualEntry }, postedCreditId, postedCredit,
    Array.Empty<CarryForwardUsageEntry>(), splitRequest, Guid.NewGuid(), splitDates);
    throw new Exception("A missing carried debit was accepted at cancellation."); }
catch (InvalidOperationException) { }
var carryUseBy = new DateTime(2027, 3, 31);
var splitApproval = CarryForwardAllocation.Plan(postedCredit, carryUseBy, 20,
    new[] { new AnnualLeaveDate(new DateTime(2027, 4, 1), 8),
        new AnnualLeaveDate(new DateTime(2027, 3, 31), 8),
        new AnnualLeaveDate(new DateTime(2027, 3, 30), 8) }, 12);
Equal(splitApproval.CarryForwardHours, 12);
Equal(splitApproval.CurrentYearHours, 12);
Equal(splitApproval.CarryForwardRemainingHours, 0);
var usageCreditId = Guid.NewGuid();
var usageRequestId = Guid.NewGuid();
var usageCancellationId = Guid.NewGuid();
var usageDebit = new CarryForwardUsageEntry(usageCreditId, usageRequestId,
    usageRequestId.ToString("D") + ":carry-approved:" + usageCreditId.ToString("D"),
    CarryForwardUsageKind.ApprovalDebit, 20);
Equal(CarryForwardUsageLedger.AvailableHours(usageCreditId, 32,
    new[] { usageDebit }), 12);
var ledgerSplit = CarryForwardUsageLedger.PlanApproval(usageCreditId, postedCredit,
    carryUseBy, new[] { usageDebit }, Guid.NewGuid(),
    new[] { new AnnualLeaveDate(new DateTime(2027, 3, 31), 8),
        new AnnualLeaveDate(new DateTime(2027, 4, 1), 8) }, 8);
Equal(ledgerSplit.CarryForwardHours, 8);
Equal(ledgerSplit.CurrentYearHours, 8);
var exactCarryReversal = CarryForwardUsageLedger.PlanCancellation(usageCreditId, 32,
    new[] { usageDebit }, usageRequestId, usageCancellationId);
Equal(exactCarryReversal.Hours, 20);
Equal(exactCarryReversal.Reverses, usageDebit.OperationKey);
Equal(CarryForwardUsageLedger.AvailableHours(usageCreditId, 32,
    new[] { usageDebit, exactCarryReversal }), 32);
try { CarryForwardUsageLedger.PlanCancellation(usageCreditId, 32,
    new[] { usageDebit, exactCarryReversal }, usageRequestId, Guid.NewGuid());
    throw new Exception("A second carry-forward reversal was accepted."); }
catch (InvalidOperationException) { }
try { CarryForwardUsageLedger.PlanApproval(usageCreditId, postedCredit,
    carryUseBy, new[] { usageDebit }, usageRequestId,
    new[] { new AnnualLeaveDate(new DateTime(2027, 3, 31), 4) }, 0);
    throw new Exception("A duplicate carry-forward approval was accepted."); }
catch (InvalidOperationException) { }
try { CarryForwardUsageLedger.AvailableHours(usageCreditId, 32,
    new[] { new CarryForwardUsageEntry(usageCreditId, usageRequestId,
        "orphan-reversal", CarryForwardUsageKind.CancellationReversal, 20, "missing") });
    throw new Exception("An orphan carry-forward reversal was accepted."); }
catch (InvalidOperationException) { }
var afterDeadline = CarryForwardAllocation.Plan(postedCredit, carryUseBy, 0,
    new[] { new AnnualLeaveDate(new DateTime(2027, 4, 1), 8) }, 8);
Equal(afterDeadline.CarryForwardHours, 0);
Equal(afterDeadline.CurrentYearHours, 8);
Equal(afterDeadline.CarryForwardRemainingHours, 32);
try { CarryForwardAllocation.Plan(postedCredit, carryUseBy, 20,
    new[] { new AnnualLeaveDate(new DateTime(2027, 3, 31), 8),
        new AnnualLeaveDate(new DateTime(2027, 4, 1), 8) }, 0);
    throw new Exception("Current-year overspend after carry-first allocation was accepted."); }
catch (InvalidOperationException) { }
try { CarryForwardAllocation.Plan(postedCredit, carryUseBy, 36,
    new[] { new AnnualLeaveDate(new DateTime(2027, 3, 31), 4) }, 0);
    throw new Exception("Overused carry-forward credit was accepted."); }
catch (ArgumentException) { }
try { CarryForwardAllocation.Plan(postedCredit, carryUseBy, 0,
    new[] { new AnnualLeaveDate(new DateTime(2027, 3, 31), 4),
        new AnnualLeaveDate(new DateTime(2027, 3, 31), 4) }, 0);
    throw new Exception("Duplicate leave dates were accepted."); }
catch (ArgumentException) { }
var exactRetry = CarryForwardConfirmation.Plan(adjustedPreview, closingHistory,
    new[] { postedCredit }, new DateTime(2027, 1, 4));
Equal(exactRetry.AlreadyPosted, true);
Equal(exactRetry.CreditToPost, null);
try { CarryForwardConfirmation.Plan(adjustedPreview, closingHistory,
    new[] { postedCredit, postedCredit }, new DateTime(2027, 1, 4));
    throw new Exception("Duplicate carry-forward credits were accepted."); }
catch (InvalidOperationException) { }
try { CarryForwardConfirmation.Plan(adjustedPreview, closingHistory,
    new[] { new CarryForwardCredit(closingSource.Id, closingTarget.Id, adjustedPreview.OperationKey, 40) },
    new DateTime(2027, 1, 4)); throw new Exception("Changed carry-forward retry was accepted."); }
catch (InvalidOperationException) { }
var changedHistory = new[] {
    new BalanceEntry(closingSource.Id, "earned-2026", BalanceEntryKind.Grant, 108),
    new BalanceEntry(closingSource.Id, "approved-2026", BalanceEntryKind.ApprovedLeave, -44, closingRequest)
};
Equal(BalanceLedger.AvailableHours(closingSource, changedHistory), 64);
try { CarryForwardConfirmation.Plan(adjustedPreview, changedHistory,
    Array.Empty<CarryForwardCredit>(), new DateTime(2027, 1, 4));
    throw new Exception("Changed closing-year entries with the same total were accepted."); }
catch (InvalidOperationException) { }
try { CarryForwardConfirmation.Plan(adjustedPreview, closingHistory,
    Array.Empty<CarryForwardCredit>(), new DateTime(2026, 12, 31));
    throw new Exception("Premature carry-forward confirmation was accepted."); }
catch (ArgumentException) { }
try { CarryForwardConfirmation.Plan(adjustedPreview, closingHistory,
    Array.Empty<CarryForwardCredit>(), new DateTime(2027, 4, 1));
    throw new Exception("Expired carry-forward confirmation was accepted."); }
catch (ArgumentException) { }
var zeroPreview = closingPreview.WithHrAdjustment(0, "Written correction", "approval-document-id");
Equal(CarryForwardConfirmation.Plan(zeroPreview, closingHistory, Array.Empty<CarryForwardCredit>(),
    new DateTime(2027, 1, 3)).CreditToPost, null);
try { closingPreview.WithHrAdjustment(44, "Requested", "approval-document-id"); throw new Exception("Unapproved excess was accepted."); }
catch (ArgumentException) { }
try { closingPreview.WithHrAdjustment(32, "", "approval-document-id"); throw new Exception("Undocumented adjustment was accepted."); }
catch (ArgumentException) { }
try { CarryForwardReview.Prepare(closingType, closingSource, closingTarget, closingHistory,
    56, false, new DateTime(2027, 1, 1)); throw new Exception("Unverified special limit was accepted."); }
catch (ArgumentException) { }
try { CarryForwardReview.Prepare(Guid.NewGuid(), closingSource, closingTarget, closingHistory,
    null, false, new DateTime(2027, 1, 1));
    throw new Exception("Non-Annual Leave was accepted."); }
catch (ArgumentException) { }
try { CarryForwardReview.Prepare(closingType, closingSource, closingTarget, closingHistory,
    null, false, new DateTime(2026, 12, 31));
    throw new Exception("Premature year-end review was accepted."); }
catch (ArgumentException) { }
var specialPreview = CarryForwardReview.Prepare(closingType, closingSource, closingTarget, closingHistory,
    56, true, new DateTime(2027, 1, 1));
Equal(specialPreview.ReviewedCarryForwardHours, 56);
try { CarryForwardReview.Prepare(closingType, closingSource, new BalancePeriod(Guid.NewGuid(), closingEmployee, closingType,
    new DateTime(2028, 1, 1), new DateTime(2028, 12, 31)), closingHistory,
    null, false, new DateTime(2028, 1, 1));
    throw new Exception("Nonconsecutive carry-forward years were accepted."); }
catch (ArgumentException) { }
Equal(UniformAnnualSchedule.Dates(new DateTime(2026, 11, 20), new DateTime(2026, 11, 23))[1], new DateTime(2026, 11, 23));
Equal(LeaveRangeSchedule.Dates("Medical Leave", new DateTime(2026, 11, 20), new DateTime(2026, 11, 23)).Length, 2);
Equal(LeaveRangeSchedule.Dates("Paternity Leave", new DateTime(2026, 12, 30), new DateTime(2027, 1, 5)).Length, 7);
Equal(LeaveRangeSchedule.Dates("Marriage Leave", new DateTime(2026, 11, 20), new DateTime(2026, 11, 22)).Length, 3);
try { LeaveRangeSchedule.Dates("Marriage Leave", new DateTime(2026, 11, 20), new DateTime(2026, 11, 23)); throw new Exception("Extended marriage range was accepted."); }
catch (InvalidPluginExecutionException) { }
try { UniformAnnualSchedule.Dates(new DateTime(2026, 11, 21), new DateTime(2026, 11, 22)); throw new Exception("Weekend-only range was accepted."); }
catch (InvalidPluginExecutionException) { }
foreach (var hours in new[] { 4, 8 }) { var request = new Entity(Guard.Request); request["cr2c9_hoursperday"] = (decimal)hours; Equal(UniformAnnualSchedule.Hours(request), hours); }
try { var request = new Entity(Guard.Request); request["cr2c9_hoursperday"] = 6m; UniformAnnualSchedule.Hours(request); throw new Exception("Mixed duration was accepted."); }
catch (InvalidPluginExecutionException) { }
Check("approval replaces forged stamps and creates linked history", f => {
    var t=f.Decision(1); t["cr2c9_decisionbyname"]="forged"; t["cr2c9_decisionbyentraid"]=Guid.NewGuid().ToString(); t["cr2c9_decisionon"]=new DateTime(2000,1,1);
    f.Run(new LeaveRequestGuard(),"Update",t); Equal(t["cr2c9_decisionbyname"],"Verified Manager"); Equal(t["cr2c9_decisionbyentraid"],f.ManagerEntra.ToString());
    Equal(t.Contains(Guard.Expected),false); Equal(f.HistoryCount,1); Equal(((DateTime)t["cr2c9_decisionon"]).Kind,DateTimeKind.Utc);
    var h=f.Rows.Values.Single(x=>x.LogicalName==Guard.History); Equal(h.GetAttributeValue<EntityReference>("cr2c9_leaverequest").Id,f.RequestId);
    Equal(h.GetAttributeValue<EntityReference>("ownerid").Id,
        f.Request.GetAttributeValue<EntityReference>("ownerid").Id);
    Equal(h["cr2c9_decidedon"],t["cr2c9_decisionon"]); Equal(h["cr2c9_comments"],"Reviewed");
    var absence=f.Rows[(PublicAbsence.Table,f.RequestId)];
    Equal(absence.GetAttributeValue<string>("cr2c9_name"),"Employee One");
    Equal(absence.GetAttributeValue<int>("cr2c9_hoursperday"),8);
    Equal(absence.Attributes.ContainsKey("cr2c9_reason"),false);
    PublicAbsence.Remove(f,f.RequestId);
    Equal(f.Rows.ContainsKey((PublicAbsence.Table,f.RequestId)),false);
});
Check("rejection records final outcome", f=>{f.Run(new LeaveRequestGuard(),"Update",f.Decision(3)); Equal(f.HistoryCount,1); Equal(f.Rows.Values.Single(x=>x.LogicalName==Guard.History).GetAttributeValue<OptionSetValue>("cr2c9_outcome").Value,3); Equal(f.Rows.Values.Any(x=>x.LogicalName==PublicAbsence.Table),false);});
var sickStart = new DateTime(2026,10,19);
Equal(SickLeaveRules.WasSubmittedInTime(sickStart,new DateTime(2026,10,20,15,59,59,DateTimeKind.Utc)),true);
Equal(SickLeaveRules.WasSubmittedInTime(sickStart,new DateTime(2026,10,20,16,0,0,DateTimeKind.Utc)),true);
Equal(SickLeaveRules.WasSubmittedInTime(sickStart,new DateTime(2026,10,20,16,0,1,DateTimeKind.Utc)),false);
Check("new Sick request after 48 hours is rejected",f=>{
    f.AsEmployee();var type=f.LeaveType;f.Rows.Remove((type.LogicalName,type.Id));
    type.Id=SickLeaveRules.Type;type["cr2c9_leavetypename"]="Sick Leave";
    f.Rows[(type.LogicalName,type.Id)]=type;
    var request=f.Submission();request["cr2c9_startdate"]=DateTime.UtcNow.AddHours(8).Date.AddDays(-3);
    request["cr2c9_enddate"]=request["cr2c9_startdate"];
    Denied(()=>f.Run(new LeaveRequestGuard(),"Create",request),f);
});
Check("pending Sick edit rejects a start date outside its original 48-hour window",f=>{
    f.AsEmployee();var type=f.LeaveType;f.Rows.Remove((type.LogicalName,type.Id));
    type.Id=SickLeaveRules.Type;type["cr2c9_leavetypename"]="Sick Leave";
    f.Rows[(type.LogicalName,type.Id)]=type;
    f.Request["cr2c9_leavetypename"]=type.ToEntityReference();
    f.Request["cr2c9_hoursperday"]=8m;
    f.Request["createdon"]=new DateTime(2026,10,20,15,0,0,DateTimeKind.Utc);
    var edit=new Entity(Guard.Request,f.RequestId);
    edit["cr2c9_startdate"]=new DateTime(2026,10,18);
    Denied(()=>f.Run(new LeaveRequestGuard(),"Update",edit),f);
});
Check("pending Sick edit keeps a start date within its original 48-hour window",f=>{
    f.AsEmployee();var type=f.LeaveType;f.Rows.Remove((type.LogicalName,type.Id));
    type.Id=SickLeaveRules.Type;type["cr2c9_leavetypename"]="Sick Leave";
    f.Rows[(type.LogicalName,type.Id)]=type;
    f.Request["cr2c9_leavetypename"]=type.ToEntityReference();
    f.Request["cr2c9_hoursperday"]=8m;
    f.Request["createdon"]=new DateTime(2026,10,20,15,0,0,DateTimeKind.Utc);
    var edit=new Entity(Guard.Request,f.RequestId);
    edit["cr2c9_enddate"]=new DateTime(2026,10,23);
    f.Run(new LeaveRequestGuard(),"Update",edit);
});
Check("Sick Leave approval requires uploaded medical evidence",f=>{
    f.Request["cr2c9_leavetypename"]=new EntityReference("cr2c9_leavetype",SickLeaveRules.Type);
    f.Request["createdon"]=new DateTime(2026,10,19,0,0,0,DateTimeKind.Utc);
    Denied(()=>f.Run(new LeaveRequestGuard(),"Update",f.Decision()),f);
});
Check("Sick Leave approval accepts timely submission with an uploaded file",f=>{
    f.Request["cr2c9_leavetypename"]=new EntityReference("cr2c9_leavetype",SickLeaveRules.Type);
    f.Request["createdon"]=new DateTime(2026,10,19,0,0,0,DateTimeKind.Utc);
    var note=new Entity("annotation",Guid.NewGuid());note["objectid"]=f.Request.ToEntityReference();
    note["isdocument"]=true;note["filesize"]=23;note["filename"]="fictional-certificate.txt";
    f.Rows[(note.LogicalName,note.Id)]=note;
    f.Run(new LeaveRequestGuard(),"Update",f.Decision());Equal(f.HistoryCount,1);
});
Check("late Sick Leave submission cannot be approved",f=>{
    f.Request["cr2c9_leavetypename"]=new EntityReference("cr2c9_leavetype",SickLeaveRules.Type);
    f.Request["createdon"]=new DateTime(2026,10,20,16,0,1,DateTimeKind.Utc);
    Denied(()=>f.Run(new LeaveRequestGuard(),"Update",f.Decision()),f);
});
Check("Sick Leave can still be rejected", f=>{f.Request["cr2c9_leavetypename"]=new EntityReference("cr2c9_leavetype",Guid.NewGuid());f.Run(new LeaveRequestGuard(),"Update",f.Decision(3));Equal(f.HistoryCount,1);});
Check("another user cannot decide", f=>{f.Actor["azureactivedirectoryobjectid"]=Guid.NewGuid(); Denied(()=>f.Run(new LeaveRequestGuard(),"Update",f.Decision()),f);});
Check("direct HR role can decide without a current superior", f=>{f.AsHr();f.Employee.Attributes.Remove("cr2c9_superior");f.Run(new LeaveRequestGuard(),"Update",f.Decision());Equal(f.HistoryCount,1);Equal(f.Rows.Values.Single(x=>x.LogicalName==Guard.History).GetAttributeValue<string>("cr2c9_actorname"),"Verified HR");});
Check("direct HR role can reject without a current superior", f=>{f.AsHr();f.Employee.Attributes.Remove("cr2c9_superior");f.Run(new LeaveRequestGuard(),"Update",f.Decision(3));Equal(f.HistoryCount,1);});
Check("HR email alone does not authorize approval", f=>{f.Actor["azureactivedirectoryobjectid"]=Guid.NewGuid();f.Actor["internalemailaddress"]="KhorLord.Yong@Asteriskdigital.com.my";Denied(()=>f.Run(new LeaveRequestGuard(),"Update",f.Decision()),f);});
Check("HR cannot decide own request", f=>{f.AsHr();f.Employee["cr2c9_entrauserid"]=f.Actor.GetAttributeValue<Guid>("azureactivedirectoryobjectid").ToString();Denied(()=>f.Run(new LeaveRequestGuard(),"Update",f.Decision()),f);});
Check("direct HR role may decide cancellation without a synced Superior",f=>{
    f.AsHr();f.Employee.Attributes.Remove("cr2c9_superior");
    CancellationContract.Reviewer(f,f.Employee,
        f.Actor.GetAttributeValue<Guid>("azureactivedirectoryobjectid"),f.ActorId);
});
Check("HR cannot decide own cancellation",f=>{
    f.AsHr();var actor=f.Actor.GetAttributeValue<Guid>("azureactivedirectoryobjectid");
    f.Employee["cr2c9_entrauserid"]=actor.ToString("D");
    Denied(()=>CancellationContract.Reviewer(f,f.Employee,actor,f.ActorId),f);
});
Check("assigned Superior may decide cancellation",f=>{
    f.Manager["statecode"]=new OptionSetValue(0);
    CancellationContract.Reviewer(f,f.Employee,f.ManagerEntra,f.ActorId);
});
Check("HR email alone cannot decide cancellation",f=>{
    f.Actor["azureactivedirectoryobjectid"]=Guid.NewGuid();
    f.Actor["internalemailaddress"]="KhorLord.Yong@Asteriskdigital.com.my";
    f.Manager["statecode"]=new OptionSetValue(0);
    Denied(()=>CancellationContract.Reviewer(f,f.Employee,
        f.Actor.GetAttributeValue<Guid>("azureactivedirectoryobjectid"),f.ActorId),f);
});
Check("HR may correct past Emergency Leave with a recorded reason",f=>{
    var type=f.LeaveType;f.Rows.Remove((type.LogicalName,type.Id));type.Id=Guid.NewGuid();
    type["cr2c9_leavetypename"]="Emergency Leave";f.Rows[(type.LogicalName,type.Id)]=type;
    f.Request["cr2c9_leavetypename"]=type.ToEntityReference();
    f.Request["cr2c9_status"]=new OptionSetValue(1);
    f.AddFullDayDates(new DateTime(2026,10,19),3);
    f.AsHr();
    Equal(CancellationContract.Scope(f,f.Request,f.ActorId,"Wrong approval",
        new DateTime(2026,10,25)),true);
    type["statecode"]=new OptionSetValue(1);
    Equal(CancellationContract.Scope(f,f.Request,f.ActorId,"Wrong approval",
        new DateTime(2026,10,25)),true);
});
Check("past Emergency correction rejects Superior and blank reason",f=>{
    var type=f.LeaveType;f.Rows.Remove((type.LogicalName,type.Id));type.Id=Guid.NewGuid();
    type["cr2c9_leavetypename"]="Emergency Leave";f.Rows[(type.LogicalName,type.Id)]=type;
    f.Request["cr2c9_leavetypename"]=type.ToEntityReference();
    f.Request["cr2c9_status"]=new OptionSetValue(1);
    f.AddFullDayDates(new DateTime(2026,10,19),3);
    Denied(()=>CancellationContract.Scope(f,f.Request,f.ActorId,"Wrong approval",
        new DateTime(2026,10,25)),f);
    f.AsHr();
    Denied(()=>CancellationContract.Scope(f,f.Request,f.ActorId,"  ",
        new DateTime(2026,10,25)),f);
});
Check("HR cannot correct own past Emergency Leave",f=>{
    var type=f.LeaveType;f.Rows.Remove((type.LogicalName,type.Id));type.Id=Guid.NewGuid();
    type["cr2c9_leavetypename"]="Emergency Leave";f.Rows[(type.LogicalName,type.Id)]=type;
    f.Request["cr2c9_leavetypename"]=type.ToEntityReference();
    f.Request["cr2c9_status"]=new OptionSetValue(1);
    f.AddFullDayDates(new DateTime(2026,10,19),3);
    f.AsHr();var actor=f.Actor.GetAttributeValue<Guid>("azureactivedirectoryobjectid");
    f.Employee["cr2c9_entrauserid"]=actor.ToString("D");
    Equal(CancellationContract.Scope(f,f.Request,f.ActorId,"Wrong approval",
        new DateTime(2026,10,25)),true);
    Denied(()=>CancellationContract.Reviewer(f,f.Employee,actor,f.ActorId),f);
});
Check("past Annual Leave remains outside HR correction",f=>{
    f.Request["cr2c9_status"]=new OptionSetValue(1);
    f.AddFullDayDates(new DateTime(2026,10,19),3);
    f.AsHr();
    Denied(()=>CancellationContract.Scope(f,f.Request,f.ActorId,"Wrong approval",
        new DateTime(2026,10,25)),f);
});
Check("HR past Emergency correction creates a reasoned pending record",f=>{
    var type=f.LeaveType;f.Rows.Remove((type.LogicalName,type.Id));type.Id=Guid.NewGuid();
    type["cr2c9_leavetypename"]="Emergency Leave";f.Rows[(type.LogicalName,type.Id)]=type;
    f.Request["cr2c9_leavetypename"]=type.ToEntityReference();
    f.Request["cr2c9_status"]=new OptionSetValue(1);
    f.AddFullDayDates(new DateTime(2026,10,19),3);f.AsHr();
    f.Employee["statecode"]=new OptionSetValue(0);
    var correction=new Entity(CancellationContract.Table,Guid.NewGuid()){
        ["cr2c9_leaverequest"]=f.Request.ToEntityReference(),
        ["cr2c9_status"]=new OptionSetValue(0),
        ["cr2c9_reason"]="Incorrect Emergency Leave approval"
    };
    f.RunCancellationGuard("Create",correction);
    Equal(correction.GetAttributeValue<string>("cr2c9_name").StartsWith("HR correction of "),true);
    Equal(f.Request.GetAttributeValue<OptionSetValue>("cr2c9_status").Value,1);
    correction["cr2c9_name"]="HR correction of existing request";
    f.Rows[(correction.LogicalName,correction.Id)]=correction;
    var duplicate=new Entity(CancellationContract.Table,Guid.NewGuid()){
        ["cr2c9_leaverequest"]=f.Request.ToEntityReference(),
        ["cr2c9_status"]=new OptionSetValue(0),
        ["cr2c9_reason"]="Second correction attempt"
    };
    Denied(()=>f.RunCancellationGuard("Create",duplicate),f);
});
Check("self approval by employee identity is blocked", f=>{f.Employee["cr2c9_entrauserid"]=f.ManagerEntra.ToString(); Denied(()=>f.Run(new LeaveRequestGuard(),"Update",f.Decision()),f);});
Check("self-referential superior is blocked", f=>{f.Employee["cr2c9_superior"]=f.Employee.ToEntityReference(); Denied(()=>f.Run(new LeaveRequestGuard(),"Update",f.Decision()),f);});
Check("blank superior is blocked", f=>{f.Employee.Attributes.Remove("cr2c9_superior"); Denied(()=>f.Run(new LeaveRequestGuard(),"Update",f.Decision()),f);});
Check("inactive superior is blocked", f=>{f.Manager["cr2c9_isactive"]=false; Denied(()=>f.Run(new LeaveRequestGuard(),"Update",f.Decision()),f);});
Check("inactive employee is blocked", f=>{f.Employee["cr2c9_isactive"]=false; Denied(()=>f.Run(new LeaveRequestGuard(),"Update",f.Decision()),f);});
Check("invalid employee identity is blocked", f=>{f.Employee["cr2c9_entrauserid"]="not-an-identity"; Denied(()=>f.Run(new LeaveRequestGuard(),"Update",f.Decision()),f);});
Check("missing signed-in identity is blocked", f=>{f.Actor.Attributes.Remove("azureactivedirectoryobjectid"); Denied(()=>f.Run(new LeaveRequestGuard(),"Update",f.Decision()),f);});
Check("stale expected version is blocked", f=>{var t=f.Decision(); t[Guard.Expected]="0"; Denied(()=>f.Run(new LeaveRequestGuard(),"Update",t),f);});
Check("missing expected version is blocked", f=>{var t=f.Decision(); t.Attributes.Remove(Guard.Expected); Denied(()=>f.Run(new LeaveRequestGuard(),"Update",t),f);});
Check("CAS conflict prevents history and stamps", f=>{f.ConflictOnNextLock=true; var t=f.Decision(); Denied(()=>f.Run(new LeaveRequestGuard(),"Update",t),f); Equal(t.Contains("cr2c9_decisionon"),false);});
foreach(var s in new[]{1,2,3,4,5}) Check("saved status "+s+" cannot be decided",f=>{f.Request["cr2c9_status"]=new OptionSetValue(s); Denied(()=>f.Run(new LeaveRequestGuard(),"Update",f.Decision()),f);});
Check("review cannot alter employee or requested details",f=>{var t=f.Decision();t["cr2c9_reason"]="changed";Denied(()=>f.Run(new LeaveRequestGuard(),"Update",t),f);});
Check("a repeat Approved write is blocked",f=>{f.Request["cr2c9_status"]=new OptionSetValue(1);Denied(()=>f.Run(new LeaveRequestGuard(),"Update",f.Decision()),f);});
Check("history failure rolls back locks and prevents the decision",f=>{f.FailHistory=true;var t=f.Decision();Denied(()=>f.Run(new LeaveRequestGuard(),"Update",t),f);Equal(f.Request.RowVersion,"1");Equal(f.Request.GetAttributeValue<OptionSetValue>("cr2c9_status").Value,0);});
Check("direct history creation is blocked",f=>{var h=new Entity(Guard.History);h["cr2c9_operationkey"]=f.RequestId+":decision";h["cr2c9_leaverequest"]=f.Request.ToEntityReference();Denied(()=>f.Run(new LeaveDecisionHistoryGuard(),"Create",h),f);});
foreach(var m in new[]{"Update","Delete"}) Check("history "+m+" is blocked",f=>Denied(()=>f.Run(new LeaveDecisionHistoryGuard(),m,new Entity(Guard.History,Guid.NewGuid())),f));
Check("request deletion is blocked",f=>Denied(()=>f.Run(new LeaveRequestGuard(),"Delete",f.Request.ToEntityReference()),f));
Check("new requests cannot be preapproved",f=>{var t=new Entity(Guard.Request);t["cr2c9_status"]=new OptionSetValue(1);Denied(()=>f.Run(new LeaveRequestGuard(),"Create",t),f);});
Check("new requests cannot impersonate another employee",f=>{var t=new Entity(Guard.Request);t["cr2c9_status"]=new OptionSetValue(0);t["cr2c9_employeename"]=f.Employee.ToEntityReference();Denied(()=>f.Run(new LeaveRequestGuard(),"Create",t),f);});
Check("new own complete Pending Superior request is allowed",f=>{f.AsEmployee();f.Run(new LeaveRequestGuard(),"Create",f.Submission());});
foreach(var field in new[]{"cr2c9_leavetypename","cr2c9_startdate","cr2c9_enddate"})
    Check("missing submission "+field+" is blocked",f=>{f.AsEmployee();var t=f.Submission();t.Attributes.Remove(field);Denied(()=>f.Run(new LeaveRequestGuard(),"Create",t),f);});
foreach(var field in new[]{"cr2c9_leavetypename","cr2c9_startdate","cr2c9_enddate"})
    Check("explicit null submission "+field+" is blocked",f=>{f.AsEmployee();var t=f.Submission();t[field]=null;Denied(()=>f.Run(new LeaveRequestGuard(),"Create",t),f);});
Check("reversed submission range is blocked",f=>{f.AsEmployee();var t=f.Submission();t["cr2c9_enddate"]=new DateTime(2026,10,18);Denied(()=>f.Run(new LeaveRequestGuard(),"Create",t),f);});
foreach(var field in new[]{"cr2c9_startdate","cr2c9_enddate"})
    Check("time-bearing submission "+field+" is blocked",f=>{f.AsEmployee();var t=f.Submission();t[field]=new DateTime(2026,10,20,8,0,0);Denied(()=>f.Run(new LeaveRequestGuard(),"Create",t),f);});
Check("wrong-table leave type reference is blocked",f=>{f.AsEmployee();var t=f.Submission();t["cr2c9_leavetypename"]=f.Employee.ToEntityReference();Denied(()=>f.Run(new LeaveRequestGuard(),"Create",t),f);});
Check("empty leave type identity is blocked",f=>{f.AsEmployee();var t=f.Submission();t["cr2c9_leavetypename"]=new EntityReference("cr2c9_leavetype",Guid.Empty);Denied(()=>f.Run(new LeaveRequestGuard(),"Create",t),f);});
Check("inactive leave type is blocked",f=>{f.AsEmployee();f.LeaveType["statecode"]=new OptionSetValue(1);Denied(()=>f.Run(new LeaveRequestGuard(),"Create",f.Submission()),f);});
Check("missing leave-type state is blocked",f=>{f.AsEmployee();f.LeaveType.Attributes.Remove("statecode");Denied(()=>f.Run(new LeaveRequestGuard(),"Create",f.Submission()),f);});
Check("unnamed leave type is blocked",f=>{f.AsEmployee();f.LeaveType.Attributes.Remove("cr2c9_leavetypename");Denied(()=>f.Run(new LeaveRequestGuard(),"Create",f.Submission()),f);});
Check("same-day submission is allowed",f=>{f.AsEmployee();var t=f.Submission();t["cr2c9_enddate"]=t["cr2c9_startdate"];f.Run(new LeaveRequestGuard(),"Create",t);});
Check("past dates remain allowed for retrospective categories",f=>{f.AsEmployee();var t=f.Submission();t["cr2c9_startdate"]=new DateTime(2000,1,3);t["cr2c9_enddate"]=new DateTime(2000,1,4);f.Run(new LeaveRequestGuard(),"Create",t);});
Check("reason remains optional without a category rule",f=>{f.AsEmployee();var t=f.Submission();t.Attributes.Remove("cr2c9_reason");f.Run(new LeaveRequestGuard(),"Create",t);});
Check("missing/unreadable leave type fails before history or locks",f=>{f.AsEmployee();f.FailTypeRead=true;Denied(()=>f.Run(new LeaveRequestGuard(),"Create",f.Submission()),f);Equal(f.Request.RowVersion,"1");Equal(f.Employee.RowVersion,"1");});
Check("type lookup uses initiating caller read permissions",f=>{f.AsEmployee();f.Run(new LeaveRequestGuard(),"Create",f.Submission());Equal(f.TypeReadUser,f.ActorId);});
Check("maternity uses 98 consecutive calendar days",f=>{f.AsEmployee();f.LeaveType["cr2c9_leavetypename"]="Maternity Leave";var t=f.Submission();t["cr2c9_startdate"]=new DateTime(2026,12,1);t["cr2c9_enddate"]=new DateTime(2027,3,8);f.Run(new LeaveRequestGuard(),"Create",t);});
Check("maternity rejects a shortened range",f=>{f.AsEmployee();f.LeaveType["cr2c9_leavetypename"]="Maternity Leave";var t=f.Submission();t["cr2c9_startdate"]=new DateTime(2026,12,1);t["cr2c9_enddate"]=new DateTime(2027,3,7);Denied(()=>f.Run(new LeaveRequestGuard(),"Create",t),f);});
Check("maternity submission requires both notice dates",f=>{
    f.AsEmployee(); var old=f.LeaveType; f.Rows.Remove((old.LogicalName,old.Id));
    var maternity=new Entity("cr2c9_leavetype",MaternityApprovalRules.TypeId) { ["statecode"]=new OptionSetValue(0),["cr2c9_leavetypename"]="Maternity Leave" };
    f.Rows[(maternity.LogicalName,maternity.Id)]=maternity;
    var t=f.Submission();t["cr2c9_leavetypename"]=maternity.ToEntityReference();
    t["cr2c9_startdate"]=new DateTime(2026,12,1);t["cr2c9_enddate"]=new DateTime(2027,3,8);
    Denied(()=>f.Run(new LeaveRequestGuard(),"Create",t),f);
    t[MaternityApprovalRules.ExpectedConfinement]=new DateTime(2026,12,1);
    Denied(()=>f.Run(new LeaveRequestGuard(),"Create",t),f);
    t[MaternityApprovalRules.FormalNotice]=new DateTime(2026,10,3);
    f.Run(new LeaveRequestGuard(),"Create",t);
});
Check("maternity approval needs notice dates but no extra HR sign-off",f=>{
    var old=f.LeaveType;f.Rows.Remove((old.LogicalName,old.Id));
    var maternity=new Entity("cr2c9_leavetype",MaternityApprovalRules.TypeId) { ["statecode"]=new OptionSetValue(0),["cr2c9_leavetypename"]="Maternity Leave" };
    f.Rows[(maternity.LogicalName,maternity.Id)]=maternity;
    f.Request["cr2c9_leavetypename"]=maternity.ToEntityReference();
    f.Request["cr2c9_startdate"]=new DateTime(2026,12,1);f.Request["cr2c9_enddate"]=new DateTime(2027,3,8);
    Denied(()=>f.Run(new LeaveRequestGuard(),"Update",f.Decision()),f);
    f.Request[MaternityApprovalRules.ExpectedConfinement]=new DateTime(2026,12,1);
    f.Request[MaternityApprovalRules.FormalNotice]=new DateTime(2026,10,3);
    Denied(()=>f.Run(new LeaveRequestGuard(),"Update",f.Decision()),f);
    f.AddFullDayDates(new DateTime(2026,12,1),98);
    f.Run(new LeaveRequestGuard(),"Update",f.Decision());
    Equal(f.HistoryCount,1);
});
Check("paternity uses seven consecutive calendar days with submitted notice",f=>{f.AsEmployee();f.LeaveType["cr2c9_leavetypename"]="Paternity Leave";var t=f.Submission();t["cr2c9_startdate"]=new DateTime(2026,12,30);t["cr2c9_enddate"]=new DateTime(2027,1,5);t[PaternityApprovalRules.ExpectedConfinement]=new DateTime(2026,12,30);t[PaternityApprovalRules.Notice]=new DateTime(2026,11,20);f.Run(new LeaveRequestGuard(),"Create",t);});
Check("paternity rejects an extended range",f=>{f.AsEmployee();f.LeaveType["cr2c9_leavetypename"]="Paternity Leave";var t=f.Submission();t["cr2c9_startdate"]=new DateTime(2026,12,30);t["cr2c9_enddate"]=new DateTime(2027,1,6);Denied(()=>f.Run(new LeaveRequestGuard(),"Create",t),f);});
Check("paternity submission requires notice and expected or actual birth",f=>{
    f.AsEmployee();f.LeaveType["cr2c9_leavetypename"]="Paternity Leave";
    var t=f.Submission();t["cr2c9_enddate"]=new DateTime(2026,10,25);
    Denied(()=>f.Run(new LeaveRequestGuard(),"Create",t),f);
    t[PaternityApprovalRules.Notice]=new DateTime(2026,9,1);
    Denied(()=>f.Run(new LeaveRequestGuard(),"Create",t),f);
    t[PaternityApprovalRules.ExpectedConfinement]=new DateTime(2026,10,19);
    f.Run(new LeaveRequestGuard(),"Create",t);
});
Check("paternity approval requires birth and twelve months service but no extra HR sign-off",f=>{
    SetPaternity(f);
    f.Request["cr2c9_hoursperday"]=8m;
    f.Request["cr2c9_enddate"]=new DateTime(2026,10,25);
    f.Request[PaternityApprovalRules.ExpectedConfinement]=new DateTime(2026,10,19);
    f.Request[PaternityApprovalRules.Notice]=new DateTime(2026,9,1);
    f.Employee["cr2c9_datejoined"]=new DateTime(2025,10,20);
    Denied(()=>f.Run(new LeaveRequestGuard(),"Update",f.Decision()),f);
    f.Request[PaternityApprovalRules.ActualConfinement]=new DateTime(2026,10,19);
    Denied(()=>f.Run(new LeaveRequestGuard(),"Update",f.Decision()),f);
    f.Employee["cr2c9_datejoined"]=new DateTime(2025,10,19);
    Denied(()=>f.Run(new LeaveRequestGuard(),"Update",f.Decision()),f);
    f.AddFullDayDates(new DateTime(2026,10,19),7);
    f.Run(new LeaveRequestGuard(),"Update",f.Decision());
    Equal(f.HistoryCount,1);
});
Check("paternity approval refuses a sixth recorded confinement",f=>{
    SetPaternity(f);
    f.Request["cr2c9_hoursperday"]=8m;
    f.Request["cr2c9_enddate"]=new DateTime(2026,10,25);
    f.Request[PaternityApprovalRules.ActualConfinement]=new DateTime(2026,10,19);
    f.Request[PaternityApprovalRules.Notice]=new DateTime(2026,10,19);
    f.Request[PaternityApprovalRules.HrVerified]=DateTime.UtcNow;
    f.Employee["cr2c9_datejoined"]=new DateTime(2020,1,1);
    for(var i=0;i<5;i++){
        var previous=new Entity(Guard.Request,Guid.NewGuid()){
            ["cr2c9_employeename"]=f.Employee.ToEntityReference(),
            ["cr2c9_leavetypename"]=f.LeaveType.ToEntityReference(),
            ["cr2c9_status"]=new OptionSetValue(i==0?5:1),
            [PaternityApprovalRules.ActualConfinement]=new DateTime(2020+i,1,1)
        };
        f.Rows[(Guard.Request,previous.Id)]=previous;
    }
    Denied(()=>f.Run(new LeaveRequestGuard(),"Update",f.Decision()),f);
});
Check("paternity approval refuses a duplicate birth and mismatched start",f=>{
    SetPaternity(f);
    f.Request["cr2c9_hoursperday"]=8m;
    f.Request["cr2c9_enddate"]=new DateTime(2026,10,25);
    f.Request[PaternityApprovalRules.ExpectedConfinement]=new DateTime(2026,10,19);
    f.Request[PaternityApprovalRules.ActualConfinement]=new DateTime(2026,10,20);
    f.Request[PaternityApprovalRules.Notice]=new DateTime(2026,10,20);
    f.Request[PaternityApprovalRules.HrVerified]=DateTime.UtcNow;
    f.Employee["cr2c9_datejoined"]=new DateTime(2020,1,1);
    Denied(()=>f.Run(new LeaveRequestGuard(),"Update",f.Decision()),f);
    f.Request[PaternityApprovalRules.ActualConfinement]=new DateTime(2026,10,19);
    var previous=new Entity(Guard.Request,Guid.NewGuid()){
        ["cr2c9_employeename"]=f.Employee.ToEntityReference(),
        ["cr2c9_leavetypename"]=f.LeaveType.ToEntityReference(),
        ["cr2c9_status"]=new OptionSetValue(1),
        [PaternityApprovalRules.ActualConfinement]=new DateTime(2026,10,19)
    };
    f.Rows[(Guard.Request,previous.Id)]=previous;
    Denied(()=>f.Run(new LeaveRequestGuard(),"Update",f.Decision()),f);
});
Check("employee cannot forge paternity HR verification",f=>{
    f.AsEmployee();SetPaternity(f);
    var t=f.Submission();t["cr2c9_enddate"]=new DateTime(2026,10,25);
    t[PaternityApprovalRules.ExpectedConfinement]=new DateTime(2026,10,19);
    t[PaternityApprovalRules.Notice]=new DateTime(2026,9,1);
    t[PaternityApprovalRules.HrVerified]=DateTime.UtcNow;
    Denied(()=>f.Run(new LeaveRequestGuard(),"Create",t),f);
});
Check("paternity pending edit clears previous HR verification",f=>{
    f.AsEmployee();SetPaternity(f);
    f.Request["cr2c9_hoursperday"]=8m;
    f.Request["cr2c9_enddate"]=new DateTime(2026,10,25);
    f.Request[PaternityApprovalRules.ExpectedConfinement]=new DateTime(2026,10,19);
    f.Request[PaternityApprovalRules.Notice]=new DateTime(2026,9,1);
    f.Request[PaternityApprovalRules.HrVerified]=DateTime.UtcNow;
    var edit=new Entity(Guard.Request,f.RequestId){[PaternityApprovalRules.ExpectedConfinement]=new DateTime(2026,10,20)};
    f.Run(new LeaveRequestGuard(),"Update",edit);
    Equal(edit.Contains(PaternityApprovalRules.HrVerified),true);
    Equal(edit[PaternityApprovalRules.HrVerified],null);
});
Check("marriage submission requires first-marriage declaration and registration date",f=>{
    f.AsEmployee();SetMarriage(f);
    var t=f.Submission();t["cr2c9_enddate"]=new DateTime(2026,10,21);
    Denied(()=>f.Run(new LeaveRequestGuard(),"Create",t),f);
    t[MarriageApprovalRules.RegisteredOn]=new DateTime(2026,10,1);
    Denied(()=>f.Run(new LeaveRequestGuard(),"Create",t),f);
    t[MarriageApprovalRules.FirstMarriage]=true;
    f.Run(new LeaveRequestGuard(),"Create",t);
});
Check("marriage approval requires completed probation and uploaded certificate",f=>{
    SetMarriage(f);f.Request["cr2c9_hoursperday"]=8m;
    f.Request[MarriageApprovalRules.RegisteredOn]=new DateTime(2026,10,1);
    f.Request[MarriageApprovalRules.FirstMarriage]=true;
    Denied(()=>f.Run(new LeaveRequestGuard(),"Update",f.Decision()),f);
    f.Employee["cr2c9_probationcompletedon"]=new DateTime(2026,10,19);
    Denied(()=>f.Run(new LeaveRequestGuard(),"Update",f.Decision()),f);
    var certificate=new Entity("annotation",Guid.NewGuid()){
        ["objectid"]=f.RequestId,["isdocument"]=true,["filesize"]=120,
        ["filename"]="marriage-certificate.pdf"
    };
    f.Rows[(certificate.LogicalName,certificate.Id)]=certificate;
    Denied(()=>f.Run(new LeaveRequestGuard(),"Update",f.Decision()),f);
    f.AddFullDayDates(new DateTime(2026,10,19),3);
    f.Run(new LeaveRequestGuard(),"Update",f.Decision());
    Equal(f.HistoryCount,1);
});
Check("marriage approval rejects a second grant and late registration window",f=>{
    SetMarriage(f);f.Request["cr2c9_hoursperday"]=8m;
    f.Request[MarriageApprovalRules.RegisteredOn]=new DateTime(2025,10,1);
    f.Request[MarriageApprovalRules.FirstMarriage]=true;
    f.Employee["cr2c9_probationcompletedon"]=new DateTime(2025,1,1);
    Denied(()=>f.Run(new LeaveRequestGuard(),"Update",f.Decision()),f);
    f.Request[MarriageApprovalRules.RegisteredOn]=new DateTime(2026,10,1);
    var previous=new Entity(Guard.Request,Guid.NewGuid()){
        ["cr2c9_employeename"]=f.Employee.ToEntityReference(),
        ["cr2c9_leavetypename"]=f.LeaveType.ToEntityReference(),
        ["cr2c9_status"]=new OptionSetValue(1)
    };
    f.Rows[(previous.LogicalName,previous.Id)]=previous;
    Denied(()=>f.Run(new LeaveRequestGuard(),"Update",f.Decision()),f);
});
Check("compassionate submission requires an eligible relationship and three full calendar days",f=>{
    f.AsEmployee();SetCompassionate(f);
    var t=f.Submission();t["cr2c9_enddate"]=new DateTime(2026,10,21);
    Denied(()=>f.Run(new LeaveRequestGuard(),"Create",t),f);
    t[CompassionateApprovalRules.Relationship]="Cousin";
    Denied(()=>f.Run(new LeaveRequestGuard(),"Create",t),f);
    t[CompassionateApprovalRules.Relationship]="Sibling";
    f.Run(new LeaveRequestGuard(),"Create",t);
    t["cr2c9_hoursperday"]=4m;
    Denied(()=>f.Run(new LeaveRequestGuard(),"Create",t),f);
});
Check("compassionate approval requires saved full-day dates and uploaded evidence",f=>{
    SetCompassionate(f);f.Request["cr2c9_hoursperday"]=8m;
    f.Request["cr2c9_enddate"]=new DateTime(2026,10,21);
    f.Request[CompassionateApprovalRules.Relationship]="Natural parent";
    Denied(()=>f.Run(new LeaveRequestGuard(),"Update",f.Decision()),f);
    f.AddFullDayDates(new DateTime(2026,10,19),3);
    Denied(()=>f.Run(new LeaveRequestGuard(),"Update",f.Decision()),f);
    var evidence=new Entity("annotation",Guid.NewGuid()){
        ["objectid"]=f.RequestId,["isdocument"]=true,["filesize"]=120,
        ["filename"]="supporting-document.pdf"
    };
    f.Rows[(evidence.LogicalName,evidence.Id)]=evidence;
    f.Run(new LeaveRequestGuard(),"Update",f.Decision());
    Equal(f.HistoryCount,1);
});
Check("compassionate future-cancellation schedule must match exact full-day dates",f=>{
    SetCompassionate(f);
    f.AddFullDayDates(new DateTime(2026,10,19),3);
    Equal(CancellationContract.SpecialDates(f,f.Request,CompassionateApprovalRules.TypeId).Length,3);
    var one=f.Rows.Values.First(x=>x.LogicalName==Guard.Date);
    one["cr2c9_duration"]=new OptionSetValue(290040000);
    Denied(()=>CancellationContract.SpecialDates(f,f.Request,CompassionateApprovalRules.TypeId),f);
});
Check("emergency future cancellation validates its exact weekday schedule",f=>{
    var type=f.LeaveType;
    type["cr2c9_leavetypename"]="Emergency Leave";
    f.Request["cr2c9_status"]=new OptionSetValue(1);
    f.Request["cr2c9_startdate"]=new DateTime(2026,11,2);
    f.Request["cr2c9_enddate"]=new DateTime(2026,11,3);
    f.AddFullDayDates(new DateTime(2026,11,2),2);
    CancellationContract.Future(f,f.Request);
    f.Request["cr2c9_startdate"]=new DateTime(2026,10,19);
    f.Request["cr2c9_enddate"]=new DateTime(2026,10,20);
    Denied(()=>CancellationContract.Future(f,f.Request),f);
});
Check("training category distinguishes company-required and external requests",f=>{
    f.LeaveType["cr2c9_leavetypename"]=TrainingPolicy.TypeName;
    f.Request[TrainingPolicy.Category]=TrainingPolicy.CompanyRequired;
    Equal(TrainingPolicy.IsTraining(f,f.Request),true);
    Equal(TrainingPolicy.ChargesAnnual(f.Request),false);
    f.Request[TrainingPolicy.Category]=TrainingPolicy.EmployeeRequestedExternal;
    Equal(TrainingPolicy.ChargesAnnual(f.Request),true);
    f.Request[TrainingPolicy.Category]="Other";
    Denied(()=>TrainingPolicy.ChargesAnnual(f.Request),f);
});
Check("training approval needs exact uniform weekday entries",f=>{
    f.LeaveType["cr2c9_leavetypename"]=TrainingPolicy.TypeName;
    f.Request[TrainingPolicy.Category]=TrainingPolicy.EmployeeRequestedExternal;
    f.Request["cr2c9_startdate"]=new DateTime(2026,11,2);
    f.Request["cr2c9_enddate"]=new DateTime(2026,11,3);
    f.Request["cr2c9_hoursperday"]=8m;
    f.AddFullDayDates(new DateTime(2026,11,2),2);
    TrainingApprovalRules.RequireSavedDates(f,f.Request);
    var first=f.Rows.Values.First(x=>x.LogicalName==Guard.Date);
    first["cr2c9_duration"]=new OptionSetValue(290040000);
    Denied(()=>TrainingApprovalRules.RequireSavedDates(f,f.Request),f);
});
Check("both training categories support future cancellation",f=>{
    f.LeaveType["cr2c9_leavetypename"]=TrainingPolicy.TypeName;
    f.Request["cr2c9_status"]=new OptionSetValue(1);
    f.Request["cr2c9_startdate"]=new DateTime(2026,11,2);
    f.Request["cr2c9_enddate"]=new DateTime(2026,11,3);
    f.Request["cr2c9_hoursperday"]=8m;
    f.AddFullDayDates(new DateTime(2026,11,2),2);
    f.Request[TrainingPolicy.Category]=TrainingPolicy.CompanyRequired;
    CancellationContract.Future(f,f.Request);
    f.Request[TrainingPolicy.Category]=TrainingPolicy.EmployeeRequestedExternal;
    CancellationContract.Future(f,f.Request);
});
Check("training approval rejects a missing category",f=>{
    f.LeaveType["cr2c9_leavetypename"]=TrainingPolicy.TypeName;
    f.Request["cr2c9_startdate"]=new DateTime(2026,11,2);
    f.Request["cr2c9_enddate"]=new DateTime(2026,11,3);
    f.Request["cr2c9_hoursperday"]=8m;
    f.AddFullDayDates(new DateTime(2026,11,2),2);
    Denied(()=>f.Run(new LeaveRequestGuard(),"Update",f.Decision()),f);
});
Check("company-required training may be approved with exact saved dates",f=>{
    f.LeaveType["cr2c9_leavetypename"]=TrainingPolicy.TypeName;
    f.Request[TrainingPolicy.Category]=TrainingPolicy.CompanyRequired;
    f.Request["cr2c9_startdate"]=new DateTime(2026,11,2);
    f.Request["cr2c9_enddate"]=new DateTime(2026,11,3);
    f.Request["cr2c9_hoursperday"]=8m;
    f.AddFullDayDates(new DateTime(2026,11,2),2);
    f.Run(new LeaveRequestGuard(),"Update",f.Decision());
    Equal(f.HistoryCount,1);
});
Check("unpaid leave is an elective Superior decision with no paid-balance prerequisite",f=>{
    f.LeaveType["cr2c9_leavetypename"]=UnpaidPolicy.TypeName;
    f.Request["cr2c9_reason"]="Personal time off without pay";
    f.Request["cr2c9_startdate"]=new DateTime(2026,11,2);
    f.Request["cr2c9_enddate"]=new DateTime(2026,11,3);
    f.Request["cr2c9_hoursperday"]=8m;
    f.AddFullDayDates(new DateTime(2026,11,2),2);
    f.Run(new LeaveRequestGuard(),"Update",f.Decision());
    Equal(f.HistoryCount,1);
    f.Request["cr2c9_status"]=new OptionSetValue(1);
    CancellationContract.Future(f,f.Request);
});
Check("unpaid leave requires a written reason and exact weekdays",f=>{
    f.LeaveType["cr2c9_leavetypename"]=UnpaidPolicy.TypeName;
    f.Request["cr2c9_startdate"]=new DateTime(2026,11,2);
    f.Request["cr2c9_enddate"]=new DateTime(2026,11,3);
    f.Request["cr2c9_hoursperday"]=8m;
    f.AddFullDayDates(new DateTime(2026,11,2),2);
    Denied(()=>f.Run(new LeaveRequestGuard(),"Update",f.Decision()),f);
    f.Request["cr2c9_reason"]="Personal time off without pay";
    var first=f.Rows.Values.First(x=>x.LogicalName==Guard.Date);
    first["cr2c9_duration"]=new OptionSetValue(290040000);
    Denied(()=>f.Run(new LeaveRequestGuard(),"Update",f.Decision()),f);
});
Check("forged new decision stamps are blocked",f=>{f.AsEmployee();var t=new Entity(Guard.Request);t["cr2c9_status"]=new OptionSetValue(0);t["cr2c9_employeename"]=f.Employee.ToEntityReference();t["cr2c9_decisionon"]=DateTime.UtcNow;Denied(()=>f.Run(new LeaveRequestGuard(),"Create",t),f);});
Check("own pending direct date creation is blocked for four hours",f=>{f.AsEmployee();Denied(()=>f.Run(new LeaveDateGuard(),"Create",f.Date()),f);});
Check("own pending direct date creation is blocked for eight hours",f=>{f.AsEmployee();var t=f.Date();t["cr2c9_duration"]=new OptionSetValue(290040001);Denied(()=>f.Run(new LeaveDateGuard(),"Create",t),f);});
Check("superior cannot edit the employee's dates",f=>Denied(()=>f.Run(new LeaveDateGuard(),"Create",f.Date()),f));
foreach(var s in new[]{1,2,3,4,5}) Check("date entry on saved status "+s+" is blocked",f=>{f.AsEmployee();f.Request["cr2c9_status"]=new OptionSetValue(s);Denied(()=>f.Run(new LeaveDateGuard(),"Create",f.Date()),f);});
Check("arbitrary duration is blocked",f=>{f.AsEmployee();var t=f.Date();t["cr2c9_duration"]=new OptionSetValue(8);Denied(()=>f.Run(new LeaveDateGuard(),"Create",t),f);});
Check("missing duration is blocked",f=>{f.AsEmployee();var t=f.Date();t.Attributes.Remove("cr2c9_duration");Denied(()=>f.Run(new LeaveDateGuard(),"Create",t),f);});
Check("missing parent is blocked",f=>{f.AsEmployee();var t=f.Date();t.Attributes.Remove("cr2c9_leaverequest");Denied(()=>f.Run(new LeaveDateGuard(),"Create",t),f);});
Check("date with time is blocked",f=>{f.AsEmployee();var t=f.Date();t["cr2c9_leavedate"]=new DateTime(2026,10,20,4,0,0);Denied(()=>f.Run(new LeaveDateGuard(),"Create",t),f);});
Check("date entry cannot be reparented",f=>{f.AsEmployee();var old=f.Date();old.Id=Guid.NewGuid();f.Rows[(Guard.Date,old.Id)]=old;var t=new Entity(Guard.Date,old.Id);t["cr2c9_leaverequest"]=new EntityReference(Guard.Request,Guid.NewGuid());Denied(()=>f.Run(new LeaveDateGuard(),"Update",t),f);});
Check("a direct lock-field update is blocked",f=>{var t=new Entity(Guard.Request,f.RequestId);t[Guard.LockField]="spoof";Denied(()=>f.Run(new LeaveRequestGuard(),"Update",t),f);});
Check("depth-three platform CAS lock is accepted",f=>f.RunPlatformLock(true));
Check("depth-three lock without matching platform payload is blocked",f=>Denied(()=>f.RunPlatformLock(false),f));
Check("SYSTEM date regeneration under platform Upsert is accepted",f=>f.RunPlatformDate(true));
Check("SYSTEM date deletion without platform Upsert is blocked",f=>Denied(()=>f.RunPlatformDate(false),f));
Check("untrusted parent step cannot authorize internal locks",f=>{f.UntrustedParent=true;Denied(()=>f.Run(new LeaveRequestGuard(),"Update",f.Decision()),f);});
Check("pending Annual edit accepts a registered parent step without a populated display name",f=>{
    f.AsEmployee();f.MissingStepName=true;
    f.Request["cr2c9_leavetypename"]=new EntityReference("cr2c9_leavetype",UniformAnnualSchedule.AnnualType);
    f.LeaveType.Id=UniformAnnualSchedule.AnnualType;f.LeaveType["cr2c9_leavetypename"]="Annual Leave";f.Rows[("cr2c9_leavetype",f.LeaveType.Id)]=f.LeaveType;
    f.Request["cr2c9_hoursperday"]=8m;
    var t=new Entity(Guard.Request,f.RequestId);t["cr2c9_startdate"]=new DateTime(2026,10,19);
    t["cr2c9_enddate"]=new DateTime(2026,10,20);t["cr2c9_hoursperday"]=4m;
    f.Run(new LeaveRequestGuard(),"Update",t);
});
Check("pending Annual form submit ignores unchanged protected fields",f=>{
    f.AsEmployee();f.Request["cr2c9_leavetypename"]=new EntityReference("cr2c9_leavetype",UniformAnnualSchedule.AnnualType);
    f.LeaveType.Id=UniformAnnualSchedule.AnnualType;f.LeaveType["cr2c9_leavetypename"]="Annual Leave";f.Rows[("cr2c9_leavetype",f.LeaveType.Id)]=f.LeaveType;
    f.Request["cr2c9_hoursperday"]=8m;f.Request["cr2c9_reason"]="test";
    var t=new Entity(Guard.Request,f.RequestId);
    t["cr2c9_startdate"]=new DateTime(2026,10,19);t["cr2c9_enddate"]=new DateTime(2026,10,20);t["cr2c9_hoursperday"]=4m;
    t["cr2c9_employeename"]=f.Employee.ToEntityReference();
    t["cr2c9_leavetypename"]=f.Request["cr2c9_leavetypename"];
    t["cr2c9_reason"]="test";t["cr2c9_status"]=new OptionSetValue(0);
    f.Run(new LeaveRequestGuard(),"Update",t);
    Equal(t.Contains("cr2c9_employeename"),false);Equal(t.Contains("cr2c9_status"),false);
});
Check("pending Annual form cannot change the protected employee",f=>{
    f.AsEmployee();f.Request["cr2c9_leavetypename"]=new EntityReference("cr2c9_leavetype",UniformAnnualSchedule.AnnualType);
    f.LeaveType.Id=UniformAnnualSchedule.AnnualType;f.LeaveType["cr2c9_leavetypename"]="Annual Leave";f.Rows[("cr2c9_leavetype",f.LeaveType.Id)]=f.LeaveType;
    f.Request["cr2c9_hoursperday"]=8m;
    var t=new Entity(Guard.Request,f.RequestId);
    t["cr2c9_hoursperday"]=8m;
    t["cr2c9_employeename"]=new EntityReference(Guard.Employee,Guid.NewGuid());
    Denied(()=>f.Run(new LeaveRequestGuard(),"Update",t),f);
});
Check("pending Medical request edits on the same form",f=>{
    f.AsEmployee();f.Request["cr2c9_leavetypename"]=f.LeaveType.ToEntityReference();f.Request["cr2c9_hoursperday"]=8m;
    var t=new Entity(Guard.Request,f.RequestId);
    t["cr2c9_startdate"]=new DateTime(2026,10,19);t["cr2c9_enddate"]=new DateTime(2026,10,23);
    t["cr2c9_hoursperday"]=4m;t["cr2c9_reason"]="Updated reason";
    f.Run(new LeaveRequestGuard(),"Update",t);
});
Check("calendar leave rejects a half day",f=>{
    f.AsEmployee();f.LeaveType["cr2c9_leavetypename"]="Paternity Leave";
    var t=f.Submission();t["cr2c9_startdate"]=new DateTime(2026,10,19);
    t["cr2c9_enddate"]=new DateTime(2026,10,25);t["cr2c9_hoursperday"]=4m;
    Denied(()=>f.Run(new LeaveRequestGuard(),"Create",t),f);
});
Check("date before request range is blocked",f=>{f.AsEmployee();var t=f.Date();t["cr2c9_leavedate"]=new DateTime(2026,10,18);Denied(()=>f.Run(new LeaveDateGuard(),"Create",t),f);});
Check("date after request range is blocked",f=>{f.AsEmployee();var t=f.Date();t["cr2c9_leavedate"]=new DateTime(2026,10,22);Denied(()=>f.Run(new LeaveDateGuard(),"Create",t),f);});
Check("date with missing request range is blocked",f=>{f.AsEmployee();f.Request.Attributes.Remove("cr2c9_startdate");Denied(()=>f.Run(new LeaveDateGuard(),"Create",f.Date()),f);});
Check("date with reversed request range is blocked",f=>{f.AsEmployee();f.Request["cr2c9_enddate"]=new DateTime(2026,10,18);Denied(()=>f.Run(new LeaveDateGuard(),"Create",f.Date()),f);});
Check("own direct date update is blocked",f=>{f.AsEmployee();var old=f.Date();old.Id=Guid.NewGuid();f.Rows[(Guard.Date,old.Id)]=old;var t=new Entity(Guard.Date,old.Id);t["cr2c9_duration"]=new OptionSetValue(290040001);Denied(()=>f.Run(new LeaveDateGuard(),"Update",t),f);});
Check("own direct date deletion is blocked",f=>{f.AsEmployee();var old=f.Date();old.Id=Guid.NewGuid();f.Rows[(Guard.Date,old.Id)]=old;f.Request.Attributes.Remove("cr2c9_startdate");Denied(()=>f.Run(new LeaveDateGuard(),"Delete",old.ToEntityReference()),f);});
Check("blocked direct date write does not lock employee",f=>{f.AsEmployee();Denied(()=>f.Run(new LeaveDateGuard(),"Create",f.Date()),f);Equal(f.Employee.RowVersion,"1");});
Console.WriteLine($"{passed} server safeguard checks passed. These simulate the SDK; live transaction/concurrency and ordinary-role tests are still required.");
EntitlementChecks.Run();
BalanceLedgerChecks.Run();
CalendarGrantChecks.Run();
BalancePersistenceChecks.Run();
AnnualGrantOperationChecks.Run();
AnnualApprovalChecks.Run();
CarryForwardPreviewChecks.Run();
CarryForwardConfirmationChecks.Run();
CarryForwardExceptionChecks.Run();
SuperiorAccessChecks.Run();

public class ContextProxy : DispatchProxy {
    public Dictionary<string,object> Values=new();
    protected override object Invoke(MethodInfo method,object[] args) {
        if(method.Name.StartsWith("get_")) {var key=method.Name.Substring(4);return Values.TryGetValue(key,out var value)?value:method.ReturnType.IsValueType?Activator.CreateInstance(method.ReturnType):null;}
        if(method.Name.StartsWith("set_")){Values[method.Name.Substring(4)]=args[0];return null;} throw new NotSupportedException(method.Name);
    }
}
public sealed partial class Fixture : IOrganizationService, IOrganizationServiceFactory, IServiceProvider {
    public Guid RequestId=Guid.NewGuid(),EmployeeId=Guid.NewGuid(),ManagerId=Guid.NewGuid(),ActorId=Guid.NewGuid(),ManagerEntra=Guid.NewGuid(),EmployeeEntra=Guid.NewGuid();
    public Dictionary<(string,Guid),Entity> Rows=new(); public bool ConflictOnNextLock,FailHistory,UntrustedParent,MissingStepName,FailTypeRead,SkipBalanceGuardForCarryCoreTest,FailCarryCreditCreate; public Guid? TypeReadUser; Guid? serviceUser; IPluginExecutionContext context;
    public Entity LeaveType=>Rows.Values.Single(x=>x.LogicalName=="cr2c9_leavetype");
    public Entity Request=>Rows[(Guard.Request,RequestId)];public Entity Employee=>Rows[(Guard.Employee,EmployeeId)];public Entity Manager=>Rows[(Guard.Employee,ManagerId)];public Entity Actor=>Rows[("systemuser",ActorId)];
    public int HistoryCount=>Rows.Values.Count(x=>x.LogicalName==Guard.History);
    public Fixture(){
        foreach(var pair in new[]{(Guard.Request,RequestId),(Guard.Employee,EmployeeId),(Guard.Employee,ManagerId),("systemuser",ActorId)})Rows[pair]=new Entity(pair.Item1,pair.Item2){RowVersion="1"};
        Request["cr2c9_status"]=new OptionSetValue(0);Request["cr2c9_employeename"]=Employee.ToEntityReference();
        Request["ownerid"]=new EntityReference("systemuser",Guid.NewGuid());
        Request["cr2c9_startdate"]=new DateTime(2026,10,19);Request["cr2c9_enddate"]=new DateTime(2026,10,21);
        Request["cr2c9_hoursperday"]=8m;
        Employee["cr2c9_entrauserid"]=EmployeeEntra.ToString();Employee["cr2c9_superior"]=Manager.ToEntityReference();Employee["cr2c9_isactive"]=true;
        Employee["cr2c9_employeename"]="Employee One";
        Manager["cr2c9_entrauserid"]=ManagerEntra.ToString();Manager["cr2c9_isactive"]=true;
        Actor["azureactivedirectoryobjectid"]=ManagerEntra;Actor["fullname"]="Verified Manager";
        var type = new Entity("cr2c9_leavetype",UniformAnnualSchedule.AnnualType);type["statecode"]=new OptionSetValue(0);type["cr2c9_leavetypename"]="Annual Leave";Rows[(type.LogicalName,type.Id)]=type;
        Request["cr2c9_leavetypename"]=type.ToEntityReference();
    }
    public void AsEmployee(){Actor["azureactivedirectoryobjectid"]=EmployeeEntra;}
    public void AsHr(){
        Actor["azureactivedirectoryobjectid"]=Guid.NewGuid();Actor["fullname"]="Verified HR";
        var role=new Entity("role",Guid.NewGuid());role["name"]=DecisionAuthority.HrRole;Rows[(role.LogicalName,role.Id)]=role;
        var membership=new Entity("systemuserroles",Guid.NewGuid());membership["systemuserid"]=ActorId;membership["roleid"]=role.Id;Rows[(membership.LogicalName,membership.Id)]=membership;
    }
    public Entity Decision(int status=1){var t=new Entity(Guard.Request,RequestId);t["cr2c9_status"]=new OptionSetValue(status);t[Guard.Expected]=Request.RowVersion;t["cr2c9_decisioncomments"]="Reviewed";return t;}
    public Entity Submission(){var t=new Entity(Guard.Request);t["cr2c9_status"]=new OptionSetValue(0);t["cr2c9_employeename"]=Employee.ToEntityReference();t["cr2c9_leavetypename"]=LeaveType.ToEntityReference();t["cr2c9_startdate"]=new DateTime(2026,10,19);t["cr2c9_enddate"]=new DateTime(2026,10,20);t["cr2c9_hoursperday"]=8m;return t;}
    public Entity Date(){var t=new Entity(Guard.Date);t["cr2c9_leaverequest"]=Request.ToEntityReference();t["cr2c9_leavedate"]=new DateTime(2026,10,20);t["cr2c9_duration"]=new OptionSetValue(290040000);return t;}
    public void AddFullDayDates(DateTime start,int count){
        for(var i=0;i<count;i++){
            var row=new Entity(Guard.Date,Guid.NewGuid());
            row["cr2c9_leaverequest"]=Request.ToEntityReference();
            row["cr2c9_leavedate"]=start.AddDays(i);
            row["cr2c9_duration"]=new OptionSetValue(290040001);
            row["statecode"]=new OptionSetValue(0);
            Rows[(row.LogicalName,row.Id)]=row;
        }
    }
    Entity Copy(Entity source){var r=new Entity(source.LogicalName,source.Id){RowVersion=source.RowVersion};foreach(var a in source.Attributes)r[a.Key]=a.Value;return r;}
    IPluginExecutionContext Ctx(string message,object target,IPluginExecutionContext parent=null,
        string stepNameOverride=null){
        var c=DispatchProxy.Create<IPluginExecutionContext,ContextProxy>();var v=((ContextProxy)(object)c).Values;
        var e=target as Entity;var er=target as EntityReference;
        // Observed live Dataverse PreOperation payload includes these even when
        // absent from the caller's SDK Update target. Simulate nested writes too.
        if (message == "Update" && e != null) { e[e.LogicalName + "id"] = e.Id; e["modifiedon"] = DateTime.UtcNow;
            e["modifiedby"] = new EntityReference("systemuser", ActorId); e["modifiedonbehalfby"] = null;
            if (e.Contains("cr2c9_decisionon")) e["timezoneruleversionnumber"] = 4; }
        v["Stage"]=20;v["IsInTransaction"]=true;v["MessageName"]=message;v["PrimaryEntityName"]=e!=null?e.LogicalName:er.LogicalName;v["PrimaryEntityId"]=e!=null?e.Id:er.Id;
        var stepName=stepNameOverride ?? ((UntrustedParent ? "Untrusted step: " : "Leave safeguard: ") +
            (e != null ? e.LogicalName : er.LogicalName) + " " + message);
        var step=new Entity("sdkmessageprocessingstep",Guid.NewGuid());step["name"]=stepName;Rows[(step.LogicalName,step.Id)]=step;
        v["OwningExtension"] = new EntityReference("sdkmessageprocessingstep",step.Id) { Name = MissingStepName ? null : stepName };
        v["InitiatingUserId"]=ActorId;v["UserId"]=ActorId;v["InputParameters"]=new ParameterCollection{{"Target",target}};v["SharedVariables"]=new ParameterCollection();v["ParentContext"]=parent;return c;
    }
    public void Run(IPlugin plugin,string message,object target){
        var snapshot=Rows.ToDictionary(x=>x.Key,x=>Copy(x.Value));var previous=context;context=Ctx(message,target);
        try{plugin.Execute(this);}catch{Rows=snapshot;throw;}finally{context=previous;}
    }
    public void RunCancellationGuard(string message, Entity target){
        var snapshot=Rows.ToDictionary(x=>x.Key,x=>Copy(x.Value));var previous=context;
        context=Ctx(message,target,null,CancellationContract.GuardStep+message);
        try {new FutureCancellationGuard().Execute(this);}
        catch {Rows=snapshot;throw;}
        finally {context=previous;}
    }
    public void RunPlatformLock(bool matching){
        var lockTarget=new Entity(Guard.Request,RequestId);lockTarget[Guard.LockField]="server-lock";
        var parentTarget=new Entity(Guard.Request,RequestId);parentTarget[Guard.LockField]=matching?"server-lock":"different-lock";
        var core=Ctx("Update",parentTarget);var coreValues=((ContextProxy)(object)core).Values;
        coreValues["Stage"]=30;coreValues["Depth"]=2;coreValues["OwningExtension"]=new EntityReference("sdkmessage",Guid.NewGuid()){Name="ObjectModel Implementation"};
        var previous=context;context=Ctx("Update",lockTarget,core);((ContextProxy)(object)context).Values["Depth"]=3;
        try{new LeaveRequestGuard().Execute(this);}finally{context=previous;}
    }
    public void RunPlatformDate(bool genuine){
        Actor["isdisabled"]=true;Actor.Attributes.Remove("azureactivedirectoryobjectid");Request["cr2c9_hoursperday"]=8m;
        var date=Date();date.Id=Guid.NewGuid();Rows[(Guard.Date,date.Id)]=date;
        var envelope=Ctx("Upsert",new Entity(Guard.Request,RequestId));
        var envelopeValues=((ContextProxy)(object)envelope).Values;
        envelopeValues["Stage"]=30;envelopeValues["Depth"]=1;
        envelopeValues["OwningExtension"]=new EntityReference("sdkmessage",Guid.NewGuid()){Name="ObjectModel Implementation"};
        var parent=Ctx("Delete",date.ToEntityReference(),envelope);var parentValues=((ContextProxy)(object)parent).Values;
        parentValues["Stage"]=30;parentValues["Depth"]=1;
        parentValues["OwningExtension"]=new EntityReference("sdkmessage",Guid.NewGuid()){Name="ObjectModel Implementation"};
        if(!genuine)envelopeValues["MessageName"]="Update";
        var previous=context;context=Ctx("Delete",date.ToEntityReference(),parent);((ContextProxy)(object)context).Values["Depth"]=2;
        try{new LeaveDateGuard().Execute(this);}finally{context=previous;}
    }
    void Nested(IPlugin plugin,string message,object target){var parent=context;
        // Reproduce the intervening core context observed in live Dataverse.
        var core=Ctx(message,target,parent);((ContextProxy)(object)core).Values["Stage"]=30;
        ((ContextProxy)(object)core).Values["OwningExtension"]=new EntityReference("sdkmessage",Guid.NewGuid()){Name="ObjectModel Implementation"};
        context=Ctx(message,target,core);try{plugin.Execute(this);}finally{context=parent;}}
    public object GetService(Type t)=>t==typeof(IPluginExecutionContext)?context:t==typeof(IOrganizationServiceFactory)?this:null;
    public IOrganizationService CreateOrganizationService(Guid? id){serviceUser=id;return this;}
    public Entity Retrieve(string table,Guid id,ColumnSet cols){if(table=="cr2c9_leavetype"){TypeReadUser=serviceUser;if(FailTypeRead)throw new InvalidPluginExecutionException("No read access to the Leave Type");}return Copy(Rows[(table,id)]);}
    public OrganizationResponse Execute(OrganizationRequest request){
        if(request is RetrieveEntityRequest) {
            var metadata=new EntityMetadata();
            typeof(EntityMetadata).GetProperty("PrimaryNameAttribute")!.GetSetMethod(true)!
                .Invoke(metadata,new object[]{"cr2c9_leavetypename"});
            return new RetrieveEntityResponse { Results=new ParameterCollection{{"EntityMetadata",metadata}} };
        }
        if(request is UpdateRequest u){var stored=Rows[(u.Target.LogicalName,u.Target.Id)];
            if(ConflictOnNextLock){ConflictOnNextLock=false;throw new InvalidPluginExecutionException("Concurrency conflict");}
            if(u.ConcurrencyBehavior!=ConcurrencyBehavior.IfRowVersionMatches||stored.RowVersion!=u.Target.RowVersion)throw new InvalidPluginExecutionException("Concurrency conflict");
            if(u.Target.LogicalName==Guard.Request)Nested(new LeaveRequestGuard(),"Update",u.Target);
            if(u.Target.LogicalName==BalancePersistence.PeriodTable) {
                if(ConflictPeriod)throw new InvalidPluginExecutionException("Period concurrency conflict");
                if(!SkipBalanceGuardForCarryCoreTest)NestedBalance("Update",u.Target);
            }
            if(u.Target.LogicalName=="cr2c9_carryforwardcredit")NestedCarry("Update",u.Target);
            foreach(var a in u.Target.Attributes)stored[a.Key]=a.Value;stored.RowVersion=(long.Parse(stored.RowVersion)+1).ToString();return new UpdateResponse();
        }throw new NotSupportedException(request.RequestName);
    }
    public Guid Create(Entity entity){if(FailHistory)throw new InvalidPluginExecutionException("History unavailable");if(entity.LogicalName==Guard.History)Nested(new LeaveDecisionHistoryGuard(),"Create",entity);
        if(entity.LogicalName=="cr2c9_carryforwardcredit"&&FailCarryCreditCreate)throw new InvalidPluginExecutionException("Injected carry-credit failure");
        if(entity.LogicalName==BalancePersistence.EntryTable){NestedBalance("Create",entity);if(++EntryWrites==FailEntryNumber)throw new InvalidPluginExecutionException("Late ledger fault");}
        if(entity.LogicalName=="cr2c9_carryforwardusage")NestedCarry("Create",entity);
        if(entity.Id==Guid.Empty)entity.Id=Guid.NewGuid();Rows[(entity.LogicalName,entity.Id)]=Copy(entity);return entity.Id;}
    public void Update(Entity e)=>throw new NotSupportedException();public void Delete(string t,Guid id)=>Rows.Remove((t,id));
    public EntityCollection RetrieveMultiple(QueryBase q)=>QueryRows((QueryExpression)q);public void Associate(string t,Guid id,Relationship r,EntityReferenceCollection e)=>throw new NotSupportedException();public void Disassociate(string t,Guid id,Relationship r,EntityReferenceCollection e)=>throw new NotSupportedException();
}
