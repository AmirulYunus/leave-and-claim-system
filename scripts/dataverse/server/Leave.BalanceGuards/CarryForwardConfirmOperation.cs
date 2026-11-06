using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Leave.Plugins;

namespace Leave.BalanceGuards
{
    internal static class CarryForwardConfirmContract
    {
        internal const string Api = "cr2c9_ConfirmAnnualCarryForward";

        internal static IPluginExecutionContext Parent(IPluginExecutionContext child)
        {
            for (var p = child.ParentContext; p != null; p = p.ParentContext)
                if (p.MessageName == Api && p.Stage == 30 && p.Mode == 0 &&
                    p.IsInTransaction && p.InitiatingUserId == child.InitiatingUserId &&
                    p.InputParameters.Contains("Target") &&
                    p.InputParameters["Target"] is EntityReference target &&
                    target.LogicalName == Guard.Employee && target.Id != Guid.Empty)
                    return p;
            return null;
        }
    }

    // A preview followed by this explicit HR action is the standard-limit
    // year-end review. The complete ledger, review and separate credit commit
    // in the same synchronous Dataverse transaction. No forfeiture is posted.
    public sealed class CarryForwardConfirmOperation : IPlugin
    {
        public void Execute(IServiceProvider provider) => ExecuteAt(provider,
            EntitlementCalculator.MalaysiaDate(DateTime.UtcNow));

        // Internal clock seam for deterministic transaction tests; the
        // registered plug-in always supplies the current Malaysia date.
        internal void ExecuteAt(IServiceProvider provider, DateTime today)
        {
            var c = (IPluginExecutionContext)provider.GetService(typeof(IPluginExecutionContext));
            if (c == null || c.MessageName != CarryForwardConfirmContract.Api || c.Stage != 30 ||
                c.Mode != 0 || !c.IsInTransaction)
                throw Guard.Error("Carry-forward confirmation requires the registered synchronous action.");
            var target = GrantContract.EmployeeTarget(c);
            if (c.InputParameters.Keys.Any(k => k != "Target" && k != "x-ms-app-name" &&
                k != "ReviewedCarryHours" && k != "AdjustmentReason" &&
                k != "AdjustmentDocumentReference"))
                throw Guard.Error("Carry-forward confirmation has unexpected inputs.");
            var s = GrantContract.System(provider);
            var actor = s.Retrieve("systemuser", c.InitiatingUserId,
                new ColumnSet("isdisabled", "azureactivedirectoryobjectid"));
            var entra = actor.GetAttributeValue<Guid>("azureactivedirectoryobjectid");
            if (actor.GetAttributeValue<bool>("isdisabled") || entra == Guid.Empty ||
                !DecisionAuthority.IsHr(s, c.InitiatingUserId))
                throw Guard.Error("A directly assigned active Leave HR reviewer is required.");
            var caller = ((IOrganizationServiceFactory)provider.GetService(typeof(IOrganizationServiceFactory)))
                .CreateOrganizationService(c.InitiatingUserId);
            caller.Retrieve(Guard.Employee, target.Id, new ColumnSet(false));
            BalanceLedger.CalendarDate(today);
            if (today.Month > 3)
                throw Guard.Error("The previous year's carry-forward confirmation closes after 31 March.");
            var sourceStart = new DateTime(today.Year - 1, 1, 1);
            var sourceEnd = new DateTime(today.Year - 1, 12, 31);
            var targetStart = new DateTime(today.Year, 1, 1);
            var targetEnd = new DateTime(today.Year, 12, 31);
            var sourceRows = GrantContract.Periods(s, target.Id, sourceStart, sourceEnd);
            var targetRows = GrantContract.Periods(s, target.Id, targetStart, targetEnd);
            if (sourceRows.Length != 1 || targetRows.Length != 1)
                throw Guard.Error("Both consecutive Annual Leave balance periods are required before HR confirmation.");
            var source = new BalancePeriod(sourceRows[0].Id, target.Id, GrantContract.AnnualType,
                sourceStart, sourceEnd);
            var next = new BalancePeriod(targetRows[0].Id, target.Id, GrantContract.AnnualType,
                targetStart, targetEnd);
            var employee = s.Retrieve(Guard.Employee, target.Id, new ColumnSet("ownerid"));
            GrantContract.Lock(s, employee);
            var persistence = new BalancePersistence(s, c);
            var sourceHistory = persistence.LockAndReadHistory(source);
            var targetHistory = persistence.LockAndReadHistory(next);
            var lockedSourceVersion = s.Retrieve(BalancePersistence.PeriodTable, source.Id,
                new ColumnSet(false)).RowVersion;
            if (string.IsNullOrWhiteSpace(lockedSourceVersion))
                throw Guard.Error("The closing-year balance lock could not be verified.");
            var exception = CarryForwardExceptionStore.Read(s, source.Id);
            var special = CarryForwardExceptionStore.VerifiedLimit(exception, target.Id, sourceStart.Year);
            var review = CarryForwardReview.Prepare(GrantContract.AnnualType, source, next,
                sourceHistory, special, special.HasValue, today);
            if (c.InputParameters.Contains("ReviewedCarryHours")) {
                if (!(c.InputParameters["ReviewedCarryHours"] is int requested))
                    throw Guard.Error("The reviewed carry-forward amount must be whole hours.");
                var reason = c.InputParameters.Contains("AdjustmentReason") ?
                    c.InputParameters["AdjustmentReason"] as string : null;
                var document = c.InputParameters.Contains("AdjustmentDocumentReference") ?
                    c.InputParameters["AdjustmentDocumentReference"] as string : null;
                if (requested == review.OriginalCalculation.CarryForwardHours) {
                    if (!string.IsNullOrWhiteSpace(reason) || !string.IsNullOrWhiteSpace(document))
                        throw Guard.Error("Adjustment evidence requires a changed reviewed amount.");
                } else {
                    if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 2000 ||
                        string.IsNullOrWhiteSpace(document) || document.Trim().Length > 200)
                        throw Guard.Error("A changed amount needs a reason and supporting document reference.");
                    try { review = review.WithHrAdjustment(requested, reason, document); }
                    catch (Exception e) { throw Guard.Error(e.Message); }
                }
            } else if (c.InputParameters.Contains("AdjustmentReason") ||
                c.InputParameters.Contains("AdjustmentDocumentReference"))
                throw Guard.Error("Adjustment evidence requires a reviewed amount.");
            var reviews = FindBySource(s, "cr2c9_carryforwardreview", source.Id,
                "cr2c9_reviewstatus", "cr2c9_employee", "cr2c9_targetperiod",
                "cr2c9_sourceledgerhash", "cr2c9_sourceunusedhours", "cr2c9_approvedlimithours",
                "cr2c9_originalcarryhours", "cr2c9_originalunusedhours", "cr2c9_reviewedcarryhours",
                "cr2c9_reviewedunusedhours", "cr2c9_operationkey", "cr2c9_speciallimithours",
                "cr2c9_approvaldocumentref", "cr2c9_specialverifiedby", "cr2c9_specialverifiedon",
                "cr2c9_adjustmentreason", "cr2c9_adjustmentdocumentref");
            var credits = FindBySource(s, "cr2c9_carryforwardcredit", source.Id,
                "cr2c9_employee", "cr2c9_targetperiod", "cr2c9_review", "cr2c9_hours",
                "cr2c9_operationkey", "cr2c9_useby");
            if (reviews.Length > 1 || credits.Length > 1)
                throw Guard.Error("Duplicate carry-forward records require investigation.");
            var savedReview = reviews.SingleOrDefault();
            var savedCredit = credits.SingleOrDefault();
            if (savedReview != null) {
                if (savedReview.GetAttributeValue<OptionSetValue>("cr2c9_reviewstatus")?.Value != 290040002 ||
                    savedReview.GetAttributeValue<EntityReference>("cr2c9_employee")?.Id != target.Id ||
                    savedReview.GetAttributeValue<EntityReference>("cr2c9_targetperiod")?.Id != next.Id ||
                    savedReview.GetAttributeValue<string>("cr2c9_sourceledgerhash") != review.SourceLedgerHash ||
                    savedReview.GetAttributeValue<int>("cr2c9_sourceunusedhours") != review.OriginalCalculation.SourceUnusedHours ||
                    savedReview.GetAttributeValue<int>("cr2c9_approvedlimithours") != review.OriginalCalculation.LimitHours ||
                    savedReview.GetAttributeValue<int>("cr2c9_originalcarryhours") != review.OriginalCalculation.CarryForwardHours ||
                    savedReview.GetAttributeValue<int>("cr2c9_originalunusedhours") != review.OriginalCalculation.SeparatelyRecordedUnusedHours ||
                    savedReview.GetAttributeValue<int>("cr2c9_reviewedcarryhours") != review.ReviewedCarryForwardHours ||
                    savedReview.GetAttributeValue<int>("cr2c9_reviewedunusedhours") != review.SeparatelyRecordedUnusedHours ||
                    savedReview.GetAttributeValue<string>("cr2c9_operationkey") != review.OperationKey ||
                    savedReview.GetAttributeValue<string>("cr2c9_adjustmentreason") != review.AdjustmentReason ||
                    savedReview.GetAttributeValue<string>("cr2c9_adjustmentdocumentref") != review.ApprovalDocumentReference ||
                    (special.HasValue && (savedReview.GetAttributeValue<int>("cr2c9_speciallimithours") != special.Value ||
                        savedReview.GetAttributeValue<string>("cr2c9_approvaldocumentref") != exception.GetAttributeValue<string>("cr2c9_documentreference") ||
                        savedReview.GetAttributeValue<string>("cr2c9_specialverifiedby") != exception.GetAttributeValue<string>("cr2c9_verifiedby"))) ||
                    (!special.HasValue && savedReview.Contains("cr2c9_speciallimithours")))
                    throw Guard.Error("The saved HR review differs from the verified closing ledger.");
                if ((savedCredit == null) != (review.ReviewedCarryForwardHours == 0) ||
                    savedCredit != null && (savedCredit.GetAttributeValue<EntityReference>("cr2c9_employee")?.Id != target.Id ||
                    savedCredit.GetAttributeValue<EntityReference>("cr2c9_targetperiod")?.Id != next.Id ||
                    savedCredit.GetAttributeValue<EntityReference>("cr2c9_review")?.Id != savedReview.Id ||
                    savedCredit.GetAttributeValue<int>("cr2c9_hours") != review.ReviewedCarryForwardHours ||
                    savedCredit.GetAttributeValue<string>("cr2c9_operationkey") != review.OperationKey ||
                    savedCredit.GetAttributeValue<DateTime?>("cr2c9_useby") != new DateTime(today.Year, 3, 31)))
                    throw Guard.Error("The saved carry-forward credit differs from the confirmed HR review.");
                Result(c, "AlreadyConfirmed", savedReview.Id, savedCredit?.Id ?? Guid.Empty, review);
                return;
            }
            if (savedCredit != null) throw Guard.Error("A carry-forward credit has no confirmed HR review.");
            // Existing current-year deductions were made without a carried
            // bucket. Posting afterward could silently violate carry-first
            // accounting; those cases need an explicit reconciliation path.
            if (targetHistory.Any(e => e.Kind != BalanceEntryKind.Grant))
                throw Guard.Error("Current-year leave was already accounted before carry-forward confirmation; HR reconciliation is required.");
            // No write may occur until the complete source history and intended
            // credit have passed the immutable confirmation plan.
            var plan = CarryForwardConfirmation.Plan(review, sourceHistory,
                Array.Empty<CarryForwardCredit>(), today);
            var owner = sourceRows[0].GetAttributeValue<EntityReference>("ownerid");
            if (owner == null || (owner.LogicalName != "systemuser" && owner.LogicalName != "team"))
                throw Guard.Error("The source Annual Leave period has no valid owner.");
            var now = DateTime.UtcNow;
            var reviewRow = new Entity("cr2c9_carryforwardreview") {
                ["cr2c9_name"] = "Annual Leave carry-forward " + sourceStart.Year,
                ["cr2c9_employee"] = target,
                ["cr2c9_sourceperiod"] = sourceRows[0].ToEntityReference(),
                ["cr2c9_targetperiod"] = targetRows[0].ToEntityReference(),
                ["cr2c9_reviewstatus"] = new OptionSetValue(290040002),
                ["cr2c9_sourceunusedhours"] = review.OriginalCalculation.SourceUnusedHours,
                ["cr2c9_approvedlimithours"] = review.OriginalCalculation.LimitHours,
                ["cr2c9_originalcarryhours"] = review.OriginalCalculation.CarryForwardHours,
                ["cr2c9_originalunusedhours"] = review.OriginalCalculation.SeparatelyRecordedUnusedHours,
                ["cr2c9_reviewedcarryhours"] = review.ReviewedCarryForwardHours,
                ["cr2c9_reviewedunusedhours"] = review.SeparatelyRecordedUnusedHours,
                ["cr2c9_operationkey"] = review.OperationKey,
                ["cr2c9_sourcerowversion"] = lockedSourceVersion,
                ["cr2c9_sourceledgerhash"] = review.SourceLedgerHash,
                ["cr2c9_confirmedby"] = entra.ToString("D"),
                ["cr2c9_confirmedon"] = now,
                ["ownerid"] = owner
            };
            if (special.HasValue) {
                reviewRow["cr2c9_speciallimithours"] = special.Value;
                reviewRow["cr2c9_approvaldocumentref"] = exception.GetAttributeValue<string>("cr2c9_documentreference");
                reviewRow["cr2c9_specialverifiedby"] = exception.GetAttributeValue<string>("cr2c9_verifiedby");
                reviewRow["cr2c9_specialverifiedon"] = exception.GetAttributeValue<DateTime>("cr2c9_verifiedon");
            }
            if (review.AdjustmentReason != null) {
                reviewRow["cr2c9_adjustmentreason"] = review.AdjustmentReason;
                reviewRow["cr2c9_adjustmentdocumentref"] = review.ApprovalDocumentReference;
            }
            var reviewId = s.Create(reviewRow);
            var creditId = Guid.Empty;
            if (plan.CreditToPost != null) {
                var creditRow = new Entity("cr2c9_carryforwardcredit") {
                    ["cr2c9_name"] = review.OperationKey,
                    ["cr2c9_employee"] = target,
                    ["cr2c9_sourceperiod"] = sourceRows[0].ToEntityReference(),
                    ["cr2c9_targetperiod"] = targetRows[0].ToEntityReference(),
                    ["cr2c9_review"] = new EntityReference("cr2c9_carryforwardreview", reviewId),
                    ["cr2c9_hours"] = plan.CreditToPost.Hours,
                    ["cr2c9_operationkey"] = review.OperationKey,
                    ["cr2c9_useby"] = plan.UseBy,
                    ["cr2c9_confirmedby"] = entra.ToString("D"),
                    ["cr2c9_confirmedon"] = now,
                    ["ownerid"] = owner
                };
                creditId = s.Create(creditRow);
            }
            Result(c, "Confirmed", reviewId, creditId, review);
        }

        static Entity[] FindBySource(IOrganizationService s, string table, Guid source, params string[] columns)
        {
            var q = new QueryExpression(table) { ColumnSet = new ColumnSet(columns), TopCount = 2 };
            q.Criteria.AddCondition("cr2c9_sourceperiod", ConditionOperator.Equal, source);
            return s.RetrieveMultiple(q).Entities.ToArray();
        }

        static void Result(IPluginExecutionContext c, string status, Guid reviewId, Guid creditId,
            CarryForwardReview review)
        {
            c.OutputParameters["Status"] = status;
            c.OutputParameters["ReviewId"] = reviewId.ToString("D");
            c.OutputParameters["CreditId"] = creditId == Guid.Empty ? "" : creditId.ToString("D");
            c.OutputParameters["SourceUnusedHours"] = review.OriginalCalculation.SourceUnusedHours;
            c.OutputParameters["CreditedHours"] = review.ReviewedCarryForwardHours;
            c.OutputParameters["SeparateUnusedHours"] = review.SeparatelyRecordedUnusedHours;
        }
    }
}
