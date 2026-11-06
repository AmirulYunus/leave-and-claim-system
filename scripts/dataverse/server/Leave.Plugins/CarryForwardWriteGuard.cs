using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Leave.Plugins
{
    // Review creation and credit posting remain fail closed. Only the named
    // synchronous decision accounting steps may lock a confirmed credit and
    // append a narrowly shaped usage debit or exact cancellation reversal.
    // This is not deployed until the accounting callers switch together.
    public sealed class CarryForwardWriteGuard : IPlugin
    {
        public void Execute(IServiceProvider provider)
        {
            var context = (IPluginExecutionContext)provider.GetService(typeof(IPluginExecutionContext));
            if (context == null || (context.PrimaryEntityName != "cr2c9_carryforwardreview" &&
                context.PrimaryEntityName != "cr2c9_carryforwardcredit" &&
                context.PrimaryEntityName != "cr2c9_carryforwardusage" &&
                context.PrimaryEntityName != "cr2c9_carryforwardexception"))
                throw Guard.Error("Carry-forward write guard is registered on an unexpected table.");
            if (context.PrimaryEntityName == "cr2c9_carryforwardexception") {
                ExceptionWrite(provider, context); return;
            }
            var confirmation = ConfirmationParent(context);
            if (confirmation != null) { ConfirmationWrite(provider, context, confirmation); return; }
            if (context.PrimaryEntityName == "cr2c9_carryforwardreview" ||
                context.Stage != 20 || context.Mode != 0 || !context.IsInTransaction)
                throw Guard.Error("Carry-forward records are read-only until HR confirmation processing is activated.");
            var message = context.MessageName.EndsWith("Multiple", StringComparison.Ordinal)
                ? context.MessageName.Substring(0, context.MessageName.Length - "Multiple".Length)
                : context.MessageName;
            if (message != "Create" && message != "Update")
                throw Guard.Error("Carry-forward records are read-only until HR confirmation processing is activated.");
            Entity target;
            if (context.InputParameters.Contains("Targets")) {
                var rows = context.InputParameters["Targets"] as EntityCollection;
                if (rows == null || rows.Entities.Count != 1)
                    throw Guard.Error("Carry-forward accounting writes one row at a time.");
                target = rows.Entities.Single();
            } else target = context.InputParameters.Contains("Target")
                ? context.InputParameters["Target"] as Entity : null;
            if (target == null || target.LogicalName != context.PrimaryEntityName)
                throw Guard.Error("Invalid carry-forward accounting target.");
            var parent = AccountingParent(context);
            if (parent == null)
                throw Guard.Error("Carry-forward records are read-only until HR confirmation processing is activated.");
            if (context.PrimaryEntityName == "cr2c9_carryforwardcredit" && message == "Update") {
                if (target.Id == Guid.Empty ||
                    !Guid.TryParse(target.GetAttributeValue<string>("cr2c9_locktoken"), out _) ||
                    target.Attributes.Keys.Any(k => k != "cr2c9_locktoken" &&
                        k != "cr2c9_carryforwardcreditid" && !Guard.SystemUpdateColumn(k)))
                    throw Guard.Error("Decision accounting may only lock an existing carried credit.");
                return;
            }
            if (context.PrimaryEntityName != "cr2c9_carryforwardusage" || message != "Create")
                throw Guard.Error("Carry-forward credits and usage are immutable outside decision accounting.");
            var credit = target.GetAttributeValue<EntityReference>("cr2c9_credit");
            var request = target.GetAttributeValue<EntityReference>("cr2c9_leaverequest");
            var kind = target.GetAttributeValue<OptionSetValue>("cr2c9_entrykind")?.Value;
            var key = target.GetAttributeValue<string>("cr2c9_operationkey");
            var hours = target.GetAttributeValue<int>("cr2c9_hours");
            if (credit == null || credit.LogicalName != "cr2c9_carryforwardcredit" || credit.Id == Guid.Empty ||
                request == null || request.LogicalName != Guard.Request || request.Id == Guid.Empty ||
                hours <= 0 || hours % 4 != 0 || hours > 100000 ||
                string.IsNullOrWhiteSpace(key) || key.Length > 120 ||
                target.GetAttributeValue<string>("cr2c9_name") != key ||
                target.Attributes.Keys.Any(k => k != "cr2c9_name" && k != "cr2c9_operationkey" &&
                    k != "cr2c9_credit" && k != "cr2c9_leaverequest" && k != "cr2c9_entrykind" &&
                    k != "cr2c9_hours" && k != "ownerid" && k != "cr2c9_cancellation" &&
                    k != "cr2c9_reverses" && k != "cr2c9_carryforwardusageid" &&
                    !Guard.SystemUpdateColumn(k)))
                throw Guard.Error("The carry-forward usage entry has invalid scope or columns.");
            var service = Guard.Service(provider);
            var savedCredit = service.Retrieve("cr2c9_carryforwardcredit", credit.Id,
                new ColumnSet("cr2c9_hours", "ownerid", "cr2c9_targetperiod"));
            var owner = savedCredit.GetAttributeValue<EntityReference>("ownerid");
            var entryOwner = target.GetAttributeValue<EntityReference>("ownerid");
            if (owner == null || entryOwner == null || owner.Id != entryOwner.Id ||
                owner.LogicalName != entryOwner.LogicalName || hours > savedCredit.GetAttributeValue<int>("cr2c9_hours"))
                throw Guard.Error("Carry-forward usage ownership or amount differs from the confirmed credit.");
            var isApproval = parent.PrimaryEntityName == Guard.Request;
            if (isApproval) {
                if (request.Id != parent.PrimaryEntityId || kind != 290040000 ||
                    target.Contains("cr2c9_cancellation") || target.Contains("cr2c9_reverses") ||
                    key != request.Id.ToString("D") + ":carry-approved:" + credit.Id.ToString("D"))
                    throw Guard.Error("Only the parent approval may append its carried debit.");
            } else {
                var cancellation = target.GetAttributeValue<EntityReference>("cr2c9_cancellation");
                var reverses = target.GetAttributeValue<EntityReference>("cr2c9_reverses");
                if (kind != 290040001 || cancellation == null ||
                    cancellation.LogicalName != "cr2c9_leavecancellation" ||
                    cancellation.Id != parent.PrimaryEntityId || reverses == null ||
                    reverses.LogicalName != "cr2c9_carryforwardusage" || reverses.Id == Guid.Empty ||
                    key != cancellation.Id.ToString("D") + ":carry-reversed:" + credit.Id.ToString("D"))
                    throw Guard.Error("Only the parent cancellation may append an exact carried reversal.");
                var original = service.Retrieve("cr2c9_carryforwardusage", reverses.Id,
                    new ColumnSet("cr2c9_credit", "cr2c9_leaverequest", "cr2c9_entrykind", "cr2c9_hours"));
                if (original.GetAttributeValue<EntityReference>("cr2c9_credit")?.Id != credit.Id ||
                    original.GetAttributeValue<EntityReference>("cr2c9_leaverequest")?.Id != request.Id ||
                    original.GetAttributeValue<OptionSetValue>("cr2c9_entrykind")?.Value != 290040000 ||
                    original.GetAttributeValue<int>("cr2c9_hours") != hours)
                    throw Guard.Error("The exact original carried debit is required.");
            }
        }

        static IPluginExecutionContext ConfirmationParent(IPluginExecutionContext child)
        {
            for (var parent = child.ParentContext; parent != null; parent = parent.ParentContext)
                if (parent.MessageName == "cr2c9_ConfirmAnnualCarryForward" && parent.Stage == 30 &&
                    parent.Mode == 0 && parent.IsInTransaction &&
                    parent.InitiatingUserId == child.InitiatingUserId &&
                    parent.InputParameters.Contains("Target") &&
                    parent.InputParameters["Target"] is EntityReference employee &&
                    employee.LogicalName == Guard.Employee && employee.Id != Guid.Empty)
                    return parent;
            return null;
        }

        static void ExceptionWrite(IServiceProvider provider, IPluginExecutionContext child)
        {
            if (child.Stage != 20 || child.Mode != 0 || !child.IsInTransaction ||
                child.MessageName != "Create" ||
                !(child.InputParameters["Target"] is Entity row) ||
                row.LogicalName != "cr2c9_carryforwardexception")
                throw Guard.Error("Written carry-forward exceptions are immutable HR records.");
            IPluginExecutionContext parent = null;
            for (var p = child.ParentContext; p != null; p = p.ParentContext)
                if (p.MessageName == "cr2c9_RecordAnnualCarryForwardException" && p.Stage == 30 &&
                    p.Mode == 0 && p.IsInTransaction && p.InitiatingUserId == child.InitiatingUserId) {
                    parent = p; break;
                }
            if (parent == null || !(parent.InputParameters["Target"] is EntityReference employee) ||
                employee.LogicalName != Guard.Employee || employee.Id == Guid.Empty ||
                row.GetAttributeValue<EntityReference>("cr2c9_employee")?.Id != employee.Id)
                throw Guard.Error("Only the authorized HR written-approval action may create an exception.");
            var service = Guard.Service(provider);
            if (!DecisionAuthority.IsHr(service, child.InitiatingUserId))
                throw Guard.Error("An active Leave HR reviewer is required.");
            var actor = service.Retrieve("systemuser", child.InitiatingUserId,
                new ColumnSet("azureactivedirectoryobjectid", "isdisabled"));
            var actorEntra = actor.GetAttributeValue<Guid>("azureactivedirectoryobjectid");
            if (actorEntra == Guid.Empty || actor.GetAttributeValue<bool>("isdisabled") ||
                row.GetAttributeValue<string>("cr2c9_verifiedby") != actorEntra.ToString("D"))
                throw Guard.Error("The HR verification identity differs from the signer.");
            var employeeRow = service.Retrieve(Guard.Employee, employee.Id,
                new ColumnSet("cr2c9_superior"));
            var superiorRef = employeeRow.GetAttributeValue<EntityReference>("cr2c9_superior");
            if (superiorRef == null || superiorRef.LogicalName != Guard.Employee ||
                superiorRef.Id == employee.Id)
                throw Guard.Error("The current immediate Superior could not be verified.");
            var superiorRow = service.Retrieve(Guard.Employee, superiorRef.Id,
                new ColumnSet("cr2c9_entrauserid", "cr2c9_isactive"));
            if (!superiorRow.GetAttributeValue<bool>("cr2c9_isactive") ||
                superiorRow.GetAttributeValue<string>("cr2c9_entrauserid") !=
                    row.GetAttributeValue<string>("cr2c9_superiorentraid"))
                throw Guard.Error("The written approval is not from the current immediate Superior.");
            var source = row.GetAttributeValue<EntityReference>("cr2c9_sourceperiod");
            if (source == null || source.LogicalName != BalancePersistence.PeriodTable || source.Id == Guid.Empty)
                throw Guard.Error("The written exception must belong to a source Annual Leave period.");
            var period = service.Retrieve(BalancePersistence.PeriodTable, source.Id,
                new ColumnSet("cr2c9_employee", "cr2c9_leavetype", "cr2c9_periodstart", "cr2c9_periodend", "ownerid"));
            var today = EntitlementCalculator.MalaysiaDate(DateTime.UtcNow);
            var owner = period.GetAttributeValue<EntityReference>("ownerid");
            var writerOwner = row.GetAttributeValue<EntityReference>("ownerid");
            if (period.GetAttributeValue<EntityReference>("cr2c9_employee")?.Id != employee.Id ||
                owner == null || writerOwner == null || owner.Id != writerOwner.Id ||
                owner.LogicalName != writerOwner.LogicalName ||
                period.GetAttributeValue<EntityReference>("cr2c9_leavetype")?.Id !=
                    new Guid("959c93de-1197-4de4-a660-2e2c3266f0fe") ||
                period.GetAttributeValue<DateTime?>("cr2c9_periodstart") != new DateTime(today.Year, 1, 1) ||
                period.GetAttributeValue<DateTime?>("cr2c9_periodend") != new DateTime(today.Year, 12, 31) ||
                row.GetAttributeValue<int>("cr2c9_approvedlimithours") !=
                    (int)parent.InputParameters["ApprovedLimitHours"] ||
                row.GetAttributeValue<string>("cr2c9_documentreference") !=
                    ((string)parent.InputParameters["DocumentReference"]).Trim())
                throw Guard.Error("The written exception differs from the verified HR action.");
            if (!Guid.TryParse(row.GetAttributeValue<string>("cr2c9_superiorentraid"), out var superior))
                throw Guard.Error("The exception has no verified immediate Superior.");
            try { CarryForwardExceptionPolicy.Validate(today.Year, today,
                row.GetAttributeValue<int>("cr2c9_approvedlimithours"),
                row.GetAttributeValue<string>("cr2c9_documentreference"), superior); }
            catch (Exception e) { throw Guard.Error(e.Message); }
        }

        static void ConfirmationWrite(IServiceProvider provider, IPluginExecutionContext child,
            IPluginExecutionContext parent)
        {
            if (child.Stage != 20 || child.Mode != 0 || !child.IsInTransaction ||
                child.MessageName != "Create" ||
                (child.PrimaryEntityName != "cr2c9_carryforwardreview" &&
                 child.PrimaryEntityName != "cr2c9_carryforwardcredit") ||
                !(child.InputParameters["Target"] is Entity target) ||
                target.LogicalName != child.PrimaryEntityName ||
                parent.InputParameters.Keys.Any(k => k != "Target" && k != "x-ms-app-name" &&
                    k != "ReviewedCarryHours" && k != "AdjustmentReason" &&
                    k != "AdjustmentDocumentReference"))
                throw Guard.Error("Only individual server-confirmed carry-forward records may be created.");
            var employee = (EntityReference)parent.InputParameters["Target"];
            var service = Guard.Service(provider);
            var actor = service.Retrieve("systemuser", child.InitiatingUserId,
                new ColumnSet("isdisabled", "azureactivedirectoryobjectid"));
            var entra = actor.GetAttributeValue<Guid>("azureactivedirectoryobjectid");
            if (actor.GetAttributeValue<bool>("isdisabled") || entra == Guid.Empty ||
                !DecisionAuthority.IsHr(service, child.InitiatingUserId))
                throw Guard.Error("Only an active Leave HR reviewer may confirm carry-forward.");
            var today = EntitlementCalculator.MalaysiaDate(DateTime.UtcNow);
            if (today.Month > 3) throw Guard.Error("Carry-forward confirmation is closed after 31 March.");
            var sourceRef = target.GetAttributeValue<EntityReference>("cr2c9_sourceperiod");
            var targetRef = target.GetAttributeValue<EntityReference>("cr2c9_targetperiod");
            if (sourceRef == null || targetRef == null ||
                sourceRef.LogicalName != BalancePersistence.PeriodTable ||
                targetRef.LogicalName != BalancePersistence.PeriodTable ||
                sourceRef.Id == Guid.Empty || targetRef.Id == Guid.Empty || sourceRef.Id == targetRef.Id ||
                target.GetAttributeValue<EntityReference>("cr2c9_employee")?.Id != employee.Id)
                throw Guard.Error("Carry-forward confirmation has an invalid employee or period scope.");
            var columns = new ColumnSet("cr2c9_employee", "cr2c9_leavetype", "cr2c9_periodstart",
                "cr2c9_periodend", "ownerid");
            var source = service.Retrieve(BalancePersistence.PeriodTable, sourceRef.Id, columns);
            var next = service.Retrieve(BalancePersistence.PeriodTable, targetRef.Id, columns);
            var annual = new Guid("959c93de-1197-4de4-a660-2e2c3266f0fe");
            if (source.GetAttributeValue<EntityReference>("cr2c9_employee")?.Id != employee.Id ||
                next.GetAttributeValue<EntityReference>("cr2c9_employee")?.Id != employee.Id ||
                source.GetAttributeValue<EntityReference>("cr2c9_leavetype")?.Id != annual ||
                next.GetAttributeValue<EntityReference>("cr2c9_leavetype")?.Id != annual ||
                source.GetAttributeValue<DateTime?>("cr2c9_periodstart") != new DateTime(today.Year-1,1,1) ||
                source.GetAttributeValue<DateTime?>("cr2c9_periodend") != new DateTime(today.Year-1,12,31) ||
                next.GetAttributeValue<DateTime?>("cr2c9_periodstart") != new DateTime(today.Year,1,1) ||
                next.GetAttributeValue<DateTime?>("cr2c9_periodend") != new DateTime(today.Year,12,31))
                throw Guard.Error("Carry-forward requires consecutive Annual Leave calendar years.");
            var owner = source.GetAttributeValue<EntityReference>("ownerid");
            var writerOwner = target.GetAttributeValue<EntityReference>("ownerid");
            if (owner == null || writerOwner == null || owner.Id != writerOwner.Id ||
                owner.LogicalName != writerOwner.LogicalName)
                throw Guard.Error("Carry-forward ownership must match the closing-year period.");
            var sourceScope = new BalancePeriod(source.Id, employee.Id, annual,
                new DateTime(today.Year-1,1,1), new DateTime(today.Year-1,12,31));
            var nextScope = new BalancePeriod(next.Id, employee.Id, annual,
                new DateTime(today.Year,1,1), new DateTime(today.Year,12,31));
            if (child.PrimaryEntityName == "cr2c9_carryforwardreview") {
                var history = new BalancePersistence(service, child).ReadVerifiedHistory(sourceScope);
                var exceptionQuery = new QueryExpression("cr2c9_carryforwardexception") {
                    TopCount = 2, ColumnSet = new ColumnSet("cr2c9_employee", "cr2c9_sourceperiod",
                        "cr2c9_approvedlimithours", "cr2c9_documentreference",
                        "cr2c9_superiorentraid", "cr2c9_verifiedby", "cr2c9_verifiedon", "createdon")
                };
                exceptionQuery.Criteria.AddCondition("cr2c9_sourceperiod", ConditionOperator.Equal, source.Id);
                var exceptions = service.RetrieveMultiple(exceptionQuery).Entities;
                if (exceptions.Count > 1) throw Guard.Error("Duplicate written exceptions require investigation.");
                var exception = exceptions.SingleOrDefault();
                int? special = null;
                if (exception != null) {
                    if (exception.GetAttributeValue<EntityReference>("cr2c9_employee")?.Id != employee.Id ||
                        !Guid.TryParse(exception.GetAttributeValue<string>("cr2c9_superiorentraid"), out var superior) ||
                        string.IsNullOrWhiteSpace(exception.GetAttributeValue<string>("cr2c9_verifiedby")))
                        throw Guard.Error("The written exception has invalid HR verification.");
                    var verifiedDate = EntitlementCalculator.MalaysiaDate(
                        exception.GetAttributeValue<DateTime>("cr2c9_verifiedon"));
                    try { CarryForwardExceptionPolicy.Validate(sourceScope.Start.Year, verifiedDate,
                        exception.GetAttributeValue<int>("cr2c9_approvedlimithours"),
                        exception.GetAttributeValue<string>("cr2c9_documentreference"), superior); }
                    catch (Exception) { throw Guard.Error("The written exception is outside policy limits."); }
                    try { CarryForwardExceptionPolicy.Validate(sourceScope.Start.Year,
                        EntitlementCalculator.MalaysiaDate(exception.GetAttributeValue<DateTime>("createdon")),
                        exception.GetAttributeValue<int>("cr2c9_approvedlimithours"),
                        exception.GetAttributeValue<string>("cr2c9_documentreference"), superior); }
                    catch (Exception) { throw Guard.Error("The written exception was not saved by 15 December."); }
                    special = exception.GetAttributeValue<int>("cr2c9_approvedlimithours");
                }
                var draft = CarryForwardReview.Prepare(annual, sourceScope, nextScope,
                    history, special, special.HasValue, today);
                if (parent.InputParameters.Contains("ReviewedCarryHours")) {
                    if (!(parent.InputParameters["ReviewedCarryHours"] is int requested))
                        throw Guard.Error("The reviewed carry-forward amount is invalid.");
                    var reason = parent.InputParameters.Contains("AdjustmentReason") ?
                        parent.InputParameters["AdjustmentReason"] as string : null;
                    var document = parent.InputParameters.Contains("AdjustmentDocumentReference") ?
                        parent.InputParameters["AdjustmentDocumentReference"] as string : null;
                    if (requested != draft.OriginalCalculation.CarryForwardHours) {
                        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 2000 ||
                            string.IsNullOrWhiteSpace(document) || document.Trim().Length > 200)
                            throw Guard.Error("The HR adjustment evidence is invalid.");
                        try { draft = draft.WithHrAdjustment(requested, reason, document); }
                        catch (Exception e) { throw Guard.Error(e.Message); }
                    } else if (!string.IsNullOrWhiteSpace(reason) || !string.IsNullOrWhiteSpace(document))
                        throw Guard.Error("Adjustment evidence requires a changed reviewed amount.");
                } else if (parent.InputParameters.Contains("AdjustmentReason") ||
                    parent.InputParameters.Contains("AdjustmentDocumentReference"))
                    throw Guard.Error("Adjustment evidence requires a reviewed amount.");
                if (target.GetAttributeValue<OptionSetValue>("cr2c9_reviewstatus")?.Value != 290040002 ||
                    target.GetAttributeValue<int>("cr2c9_sourceunusedhours") != draft.OriginalCalculation.SourceUnusedHours ||
                    target.GetAttributeValue<int>("cr2c9_approvedlimithours") != draft.OriginalCalculation.LimitHours ||
                    target.GetAttributeValue<int>("cr2c9_originalcarryhours") != draft.OriginalCalculation.CarryForwardHours ||
                    target.GetAttributeValue<int>("cr2c9_originalunusedhours") != draft.OriginalCalculation.SeparatelyRecordedUnusedHours ||
                    target.GetAttributeValue<int>("cr2c9_reviewedcarryhours") != draft.ReviewedCarryForwardHours ||
                    target.GetAttributeValue<int>("cr2c9_reviewedunusedhours") != draft.SeparatelyRecordedUnusedHours ||
                    target.GetAttributeValue<string>("cr2c9_operationkey") != draft.OperationKey ||
                    target.GetAttributeValue<string>("cr2c9_sourceledgerhash") != draft.SourceLedgerHash ||
                    target.GetAttributeValue<string>("cr2c9_confirmedby") != entra.ToString("D") ||
                    target.GetAttributeValue<string>("cr2c9_adjustmentreason") != draft.AdjustmentReason ||
                    target.GetAttributeValue<string>("cr2c9_adjustmentdocumentref") != draft.ApprovalDocumentReference ||
                    (special.HasValue && (target.GetAttributeValue<int>("cr2c9_speciallimithours") != special.Value ||
                        target.GetAttributeValue<string>("cr2c9_approvaldocumentref") != exception.GetAttributeValue<string>("cr2c9_documentreference") ||
                        target.GetAttributeValue<string>("cr2c9_specialverifiedby") != exception.GetAttributeValue<string>("cr2c9_verifiedby"))) ||
                    (!special.HasValue && (target.Contains("cr2c9_speciallimithours") ||
                        target.Contains("cr2c9_approvaldocumentref"))))
                    throw Guard.Error("The HR review differs from the verified standard carry-forward calculation.");
            } else {
                var reviewRef = target.GetAttributeValue<EntityReference>("cr2c9_review");
                if (reviewRef == null || reviewRef.LogicalName != "cr2c9_carryforwardreview" ||
                    reviewRef.Id == Guid.Empty) throw Guard.Error("A confirmed HR review is required.");
                var review = service.Retrieve("cr2c9_carryforwardreview", reviewRef.Id,
                    new ColumnSet("cr2c9_reviewstatus", "cr2c9_employee", "cr2c9_sourceperiod",
                        "cr2c9_targetperiod", "cr2c9_reviewedcarryhours", "cr2c9_operationkey"));
                if (review.GetAttributeValue<OptionSetValue>("cr2c9_reviewstatus")?.Value != 290040002 ||
                    review.GetAttributeValue<EntityReference>("cr2c9_employee")?.Id != employee.Id ||
                    review.GetAttributeValue<EntityReference>("cr2c9_sourceperiod")?.Id != source.Id ||
                    review.GetAttributeValue<EntityReference>("cr2c9_targetperiod")?.Id != next.Id ||
                    target.GetAttributeValue<int>("cr2c9_hours") <= 0 ||
                    target.GetAttributeValue<int>("cr2c9_hours") != review.GetAttributeValue<int>("cr2c9_reviewedcarryhours") ||
                    target.GetAttributeValue<string>("cr2c9_operationkey") != review.GetAttributeValue<string>("cr2c9_operationkey") ||
                    target.GetAttributeValue<string>("cr2c9_name") != review.GetAttributeValue<string>("cr2c9_operationkey") ||
                    target.GetAttributeValue<DateTime?>("cr2c9_useby") != new DateTime(today.Year,3,31) ||
                    target.GetAttributeValue<string>("cr2c9_confirmedby") != entra.ToString("D"))
                    throw Guard.Error("The carry-forward credit differs from the confirmed HR review.");
            }
        }

        static IPluginExecutionContext AccountingParent(IPluginExecutionContext child)
        {
            for (var parent = child.ParentContext; parent != null; parent = parent.ParentContext) {
                var name = parent.OwningExtension?.Name;
                var approval = name == "Leave annual approval accounting" &&
                    parent.PrimaryEntityName == Guard.Request;
                var cancellation = name == "Leave future cancellation accounting" &&
                    parent.PrimaryEntityName == "cr2c9_leavecancellation";
                if ((approval || cancellation) && parent.OwningExtension?.LogicalName == "sdkmessageprocessingstep" &&
                    parent.Stage == 40 && parent.Mode == 0 && parent.IsInTransaction &&
                    parent.MessageName == "Update" && parent.PrimaryEntityId != Guid.Empty &&
                    parent.InitiatingUserId == child.InitiatingUserId &&
                    parent.InputParameters.Contains("Target") &&
                    parent.InputParameters["Target"] is Entity target &&
                    target.Id == parent.PrimaryEntityId &&
                    target.GetAttributeValue<OptionSetValue>("cr2c9_status")?.Value == 1)
                    return parent;
            }
            return null;
        }
    }
}
