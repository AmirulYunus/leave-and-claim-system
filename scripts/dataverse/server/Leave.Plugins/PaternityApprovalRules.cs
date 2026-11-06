using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Leave.Plugins
{
    internal static class PaternityApprovalRules
    {
        internal const string ExpectedConfinement = "cr2c9_paternityexpecteddate";
        internal const string ActualConfinement = "cr2c9_paternitybirthdate";
        internal const string Notice = "cr2c9_paternitynoticedate";
        internal const string HrVerified = "cr2c9_paternityhrverifiedon";
        internal static readonly Guid TypeId = new Guid("8e025448-ff94-4204-90e2-df4d3b3ca8d8");

        internal static bool IsPaternity(IOrganizationService service, Entity request)
        {
            var reference = request.GetAttributeValue<EntityReference>("cr2c9_leavetypename");
            if (reference == null || reference.LogicalName != "cr2c9_leavetype" || reference.Id != TypeId)
                return false;
            var type = service.Retrieve(reference.LogicalName, reference.Id,
                new ColumnSet("cr2c9_leavetypename", "statecode"));
            return type.GetAttributeValue<OptionSetValue>("statecode")?.Value == 0 &&
                type.GetAttributeValue<string>("cr2c9_leavetypename") == "Paternity Leave";
        }

        internal static void RequireSubmittedDates(Entity request)
        {
            var expected = request.GetAttributeValue<DateTime?>(ExpectedConfinement);
            var actual = request.GetAttributeValue<DateTime?>(ActualConfinement);
            var notice = request.GetAttributeValue<DateTime?>(Notice);
            if (!notice.HasValue || (!expected.HasValue && !actual.HasValue))
                throw Guard.Error("Paternity Leave needs a notice date and an expected or actual birth date.");
            if (new[] { expected, actual, notice }.Any(d => d.HasValue && d.Value.TimeOfDay != TimeSpan.Zero))
                throw Guard.Error("Paternity birth and notice dates must be calendar dates.");
        }

        internal static void RequireReview(IOrganizationService service, Entity request, Entity employee)
        {
            RequireSubmittedDates(request);
            var actual = request.GetAttributeValue<DateTime?>(ActualConfinement);
            var start = request.GetAttributeValue<DateTime?>("cr2c9_startdate");
            if (!actual.HasValue || !start.HasValue || start.Value != actual.Value)
                throw Guard.Error("Paternity Leave must start on the verified birth date.");
            var end = request.GetAttributeValue<DateTime?>("cr2c9_enddate");
            if (!end.HasValue) throw Guard.Error("Paternity Leave needs an End Date.");
            ConsecutiveCalendarLeave.Validate("Paternity Leave", start.Value, end.Value);
            LeaveRangeSchedule.Hours("Paternity Leave", request);
            ConsecutiveCalendarLeave.RequireSavedDates(service, request, "Paternity Leave");
            var joined = employee.GetAttributeValue<DateTime?>("cr2c9_datejoined");
            if (!joined.HasValue || joined.Value.TimeOfDay != TimeSpan.Zero ||
                joined.Value > start.Value.AddMonths(-12))
                throw Guard.Error("Paternity Leave requires at least 12 months of continuous service before it starts; check Date Joined.");

            // Count previous approved confinements, including requests later
            // cancelled. A cancellation does not undo a birth. Treat legacy
            // requests without a saved birth date as separate confinements.
            var employeeReference = request.GetAttributeValue<EntityReference>("cr2c9_employeename");
            var typeReference = request.GetAttributeValue<EntityReference>("cr2c9_leavetypename");
            if (employeeReference == null || typeReference == null)
                throw Guard.Error("Paternity request identity is incomplete.");
            var previous = new System.Collections.Generic.List<Entity>();
            foreach (var status in new[] { 1, 5 })
            {
                var query = new QueryExpression(Guard.Request) {
                    ColumnSet = new ColumnSet(ActualConfinement, "cr2c9_startdate"),
                    PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 }
                };
                query.Criteria.AddCondition("cr2c9_employeename", ConditionOperator.Equal, employeeReference.Id);
                query.Criteria.AddCondition("cr2c9_leavetypename", ConditionOperator.Equal, typeReference.Id);
                query.Criteria.AddCondition("cr2c9_status", ConditionOperator.Equal, status);
                while (true)
                {
                    var page = service.RetrieveMultiple(query);
                    previous.AddRange(page.Entities.Where(row => row.Id != request.Id));
                    if (!page.MoreRecords) break;
                    query.PageInfo.PageNumber++;
                    query.PageInfo.PagingCookie = page.PagingCookie;
                }
            }
            var knownBirths = previous.Where(row => row.GetAttributeValue<DateTime?>(ActualConfinement).HasValue)
                .Select(row => row.GetAttributeValue<DateTime>(ActualConfinement).Date).Distinct().ToArray();
            var legacyCount = previous.Count(row => !row.GetAttributeValue<DateTime?>(ActualConfinement).HasValue);
            if (knownBirths.Contains(actual.Value.Date))
                throw Guard.Error("Paternity Leave has already been approved for this confinement.");
            if (knownBirths.Length + legacyCount >= 5)
                throw Guard.Error("Five prior Paternity Leave confinements are already recorded. Review the history.");
        }
    }
}
