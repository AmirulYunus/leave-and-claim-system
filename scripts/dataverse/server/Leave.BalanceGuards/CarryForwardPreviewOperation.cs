using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Leave.Plugins;

namespace Leave.BalanceGuards
{
    // HR-only read of the most recently closed Annual Leave year. It does not
    // create a review or credit; confirmation must independently lock and
    // revalidate the complete ledger before posting anything.
    public sealed class CarryForwardPreviewOperation : IPlugin
    {
        internal const string Api = "cr2c9_PreviewAnnualCarryForward";

        public void Execute(IServiceProvider provider)
        {
            var c = (IPluginExecutionContext)provider.GetService(typeof(IPluginExecutionContext));
            if (c == null || c.MessageName != Api || c.Stage != 30 || c.Mode != 0 || !c.IsInTransaction)
                throw Guard.Error("Carry-forward preview requires the registered synchronous action.");
            var target = GrantContract.EmployeeTarget(c);
            if (c.InputParameters.Keys.Any(k => k != "Target" && k != "x-ms-app-name"))
                throw Guard.Error("Carry-forward preview does not accept caller-supplied balance or policy values.");
            var s = GrantContract.System(provider);
            var user = s.Retrieve("systemuser", c.InitiatingUserId,
                new ColumnSet("isdisabled", "azureactivedirectoryobjectid"));
            if (user.GetAttributeValue<bool>("isdisabled") ||
                user.GetAttributeValue<Guid>("azureactivedirectoryobjectid") == Guid.Empty ||
                !DecisionAuthority.IsHr(s, c.InitiatingUserId))
                throw Guard.Error("A directly assigned active Leave HR reviewer is required.");
            var caller = ((IOrganizationServiceFactory)provider.GetService(typeof(IOrganizationServiceFactory)))
                .CreateOrganizationService(c.InitiatingUserId);
            caller.Retrieve(Guard.Employee, target.Id, new ColumnSet(false));

            var today = EntitlementCalculator.MalaysiaDate(DateTime.UtcNow);
            var sourceYear = today.Year - 1;
            var sourceStart = new DateTime(sourceYear, 1, 1);
            var sourceEnd = new DateTime(sourceYear, 12, 31);
            var targetStart = new DateTime(today.Year, 1, 1);
            var targetEnd = new DateTime(today.Year, 12, 31);
            c.OutputParameters["SourceYear"] = sourceYear;
            var sourceRows = GrantContract.Periods(s, target.Id, sourceStart, sourceEnd);
            if (sourceRows.Length == 0) { Result(c, "NoClosedYearBalance", Guid.Empty, 0, 0, 0, 0); return; }
            var targetRows = GrantContract.Periods(s, target.Id, targetStart, targetEnd);
            if (targetRows.Length == 0) { Result(c, "NoCurrentYearBalance", Guid.Empty, 0, 0, 0, 0); return; }
            var source = new BalancePeriod(sourceRows.Single().Id, target.Id, GrantContract.AnnualType,
                sourceStart, sourceEnd);
            var next = new BalancePeriod(targetRows.Single().Id, target.Id, GrantContract.AnnualType,
                targetStart, targetEnd);
            var history = new BalancePersistence(s, c).ReadVerifiedHistory(source);
            var exception = CarryForwardExceptionStore.Read(s, source.Id);
            var special = CarryForwardExceptionStore.VerifiedLimit(exception, target.Id, sourceYear);
            var draft = CarryForwardReview.Prepare(GrantContract.AnnualType,
                source, next, history, special, special.HasValue, today);
            var reviews = new QueryExpression("cr2c9_carryforwardreview") {
                ColumnSet = new ColumnSet("cr2c9_reviewstatus"), TopCount = 2
            };
            reviews.Criteria.AddCondition("cr2c9_sourceperiod", ConditionOperator.Equal, source.Id);
            var existing = s.RetrieveMultiple(reviews).Entities;
            if (existing.Count > 1)
                throw Guard.Error("Duplicate carry-forward reviews require HR investigation.");
            var review = existing.SingleOrDefault();
            var status = review == null ? "ReadyForHrReview" :
                review.GetAttributeValue<OptionSetValue>("cr2c9_reviewstatus")?.Value == 290040002
                    ? "AlreadyConfirmed" : "ExistingReview";
            Result(c, status, review?.Id ?? Guid.Empty, draft.OriginalCalculation.SourceUnusedHours,
                draft.OriginalCalculation.LimitHours, draft.OriginalCalculation.CarryForwardHours,
                draft.OriginalCalculation.SeparatelyRecordedUnusedHours);
        }

        static void Result(IPluginExecutionContext c, string status, Guid reviewId,
            int unused, int limit, int carry, int separate)
        {
            c.OutputParameters["Status"] = status;
            c.OutputParameters["ReviewId"] = reviewId == Guid.Empty ? "" : reviewId.ToString("D");
            c.OutputParameters["SourceUnusedHours"] = unused;
            c.OutputParameters["StandardLimitHours"] = limit;
            c.OutputParameters["CandidateCarryHours"] = carry;
            c.OutputParameters["SeparateUnusedHours"] = separate;
        }
    }
}
