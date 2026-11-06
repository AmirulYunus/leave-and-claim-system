using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Leave.Plugins
{
    internal static class CompassionateApprovalRules
    {
        internal static readonly Guid TypeId = new Guid("e85a19e9-c65c-48a3-8352-63b301521efd");
        internal const string Relationship = "cr2c9_compassionaterelationship";
        internal static readonly string[] AllowedRelationships = {
            "Spouse", "Child", "Natural parent", "Sibling", "Grandparent", "Parent-in-law", "Legal guardian"
        };

        internal static bool IsCompassionate(Entity request) =>
            request.GetAttributeValue<EntityReference>("cr2c9_leavetypename")?.Id == TypeId;

        internal static void RequireSubmittedDetails(Entity request)
        {
            var relationship = request.GetAttributeValue<string>(Relationship)?.Trim();
            if (!AllowedRelationships.Any(value => string.Equals(value, relationship, StringComparison.OrdinalIgnoreCase)))
                throw Guard.Error("Choose an eligible relationship for Compassionate Leave: spouse, child, natural parent, sibling, grandparent, parent-in-law or legal guardian.");
            var start = request.GetAttributeValue<DateTime?>("cr2c9_startdate");
            var end = request.GetAttributeValue<DateTime?>("cr2c9_enddate");
            if (!start.HasValue || !end.HasValue)
                throw Guard.Error("Compassionate Leave needs a Start Date and End Date.");
            ConsecutiveCalendarLeave.Validate("Compassionate Leave", start.Value, end.Value);
            LeaveRangeSchedule.Hours("Compassionate Leave", request);
        }

        internal static void RequireApproval(IOrganizationService service, Entity request)
        {
            RequireSubmittedDetails(request);
            ConsecutiveCalendarLeave.RequireSavedDates(service, request, "Compassionate Leave");
            var notes = new QueryExpression("annotation") {
                ColumnSet = new ColumnSet("isdocument", "filesize", "filename"), TopCount = 100
            };
            notes.Criteria.AddCondition("objectid", ConditionOperator.Equal, request.Id);
            var evidence = service.RetrieveMultiple(notes);
            if (evidence.MoreRecords || !evidence.Entities.Any(note =>
                note.GetAttributeValue<bool>("isdocument") &&
                note.GetAttributeValue<int>("filesize") > 0 &&
                !string.IsNullOrWhiteSpace(note.GetAttributeValue<string>("filename"))))
                throw Guard.Error("Upload a supporting document before approving Compassionate Leave. The approver must verify it.");
        }
    }
}
