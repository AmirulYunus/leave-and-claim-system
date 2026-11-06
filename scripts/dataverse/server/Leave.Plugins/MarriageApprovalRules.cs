using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Leave.Plugins
{
    internal static class MarriageApprovalRules
    {
        internal static readonly Guid TypeId = new Guid("75aa666a-2b4e-49e5-8766-0dc59e6fc25f");
        internal const string RegisteredOn = "cr2c9_marriageregisteredon";
        internal const string FirstMarriage = "cr2c9_firstlegalmarriage";

        internal static bool IsMarriage(Entity request) =>
            request.GetAttributeValue<EntityReference>("cr2c9_leavetypename")?.Id == TypeId;

        internal static void RequireSubmittedDetails(Entity request)
        {
            var registered = request.GetAttributeValue<DateTime?>(RegisteredOn);
            if (!registered.HasValue || registered.Value.TimeOfDay != TimeSpan.Zero)
                throw Guard.Error("Enter the official marriage registration date.");
            if (!request.GetAttributeValue<bool>(FirstMarriage))
                throw Guard.Error("Marriage Leave is available only for the employee's first legal marriage. Confirm the declaration before submitting.");
            var start = request.GetAttributeValue<DateTime?>("cr2c9_startdate");
            var end = request.GetAttributeValue<DateTime?>("cr2c9_enddate");
            if (!start.HasValue || !end.HasValue || start.Value < registered.Value ||
                registered.Value > DateTime.MaxValue.AddMonths(-12) ||
                end.Value >= registered.Value.AddMonths(12))
                throw Guard.Error("Take all three Marriage Leave days after registration and within twelve months of the official registration date.");
        }

        internal static void RequireApproval(IOrganizationService service, Entity request, Entity employee)
        {
            RequireSubmittedDetails(request);
            var start = request.GetAttributeValue<DateTime>("cr2c9_startdate");
            var end = request.GetAttributeValue<DateTime>("cr2c9_enddate");
            ConsecutiveCalendarLeave.Validate("Marriage Leave", start, end);
            LeaveRangeSchedule.Hours("Marriage Leave", request);
            ConsecutiveCalendarLeave.RequireSavedDates(service, request, "Marriage Leave");
            var probation = employee.GetAttributeValue<DateTime?>("cr2c9_probationcompletedon");
            if (!probation.HasValue || probation.Value.TimeOfDay != TimeSpan.Zero || probation.Value > start)
                throw Guard.Error("HR must record a probation completion date on or before Marriage Leave starts.");

            var employeeRef = request.GetAttributeValue<EntityReference>("cr2c9_employeename");
            if (employeeRef == null || employeeRef.Id == Guid.Empty)
                throw Guard.Error("The Marriage Leave employee is missing.");
            var q = new QueryExpression(Guard.Request) {
                ColumnSet = new ColumnSet(false), TopCount = 1
            };
            q.Criteria.AddCondition("cr2c9_employeename", ConditionOperator.Equal, employeeRef.Id);
            q.Criteria.AddCondition("cr2c9_leavetypename", ConditionOperator.Equal, TypeId);
            q.Criteria.AddCondition(Guard.Request + "id", ConditionOperator.NotEqual, request.Id);
            q.Criteria.AddCondition("cr2c9_status", ConditionOperator.Equal, 1);
            if (service.RetrieveMultiple(q).Entities.Count > 0)
                throw Guard.Error("Marriage Leave was already approved for this employee's first legal marriage.");

            var notes = new QueryExpression("annotation") {
                ColumnSet = new ColumnSet("isdocument", "filesize", "filename"), TopCount = 100
            };
            notes.Criteria.AddCondition("objectid", ConditionOperator.Equal, request.Id);
            var evidence = service.RetrieveMultiple(notes);
            if (evidence.MoreRecords || !System.Linq.Enumerable.Any(evidence.Entities, note =>
                note.GetAttributeValue<bool>("isdocument") &&
                note.GetAttributeValue<int>("filesize") > 0 &&
                !string.IsNullOrWhiteSpace(note.GetAttributeValue<string>("filename"))))
                throw Guard.Error("Upload the marriage certificate before approval. The approver must verify the file and first-marriage declaration.");
        }
    }
}
