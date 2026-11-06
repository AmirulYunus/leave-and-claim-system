using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Leave.Plugins
{
    internal static class SickLeaveRules
    {
        internal static readonly Guid Type = new Guid("261e2bd3-770e-4880-bcef-e4f5be6571e6");

        internal static bool IsSick(Entity request) =>
            request.GetAttributeValue<EntityReference>("cr2c9_leavetypename")?.Id == Type;

        // Date-only start is midnight Malaysia time. Dataverse Created On is UTC.
        internal static bool WasSubmittedInTime(DateTime start, DateTime submittedUtc)
        {
            if (start.TimeOfDay != TimeSpan.Zero) return false;
            var utc = submittedUtc.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(submittedUtc, DateTimeKind.Utc)
                : submittedUtc.ToUniversalTime();
            var deadlineUtc = DateTime.SpecifyKind(start.AddDays(2).AddHours(-8), DateTimeKind.Utc);
            return utc <= deadlineUtc;
        }

        internal static void RequireTimelySubmission(Entity request, DateTime submittedUtc)
        {
            var start = request.GetAttributeValue<DateTime?>("cr2c9_startdate");
            if (!start.HasValue || !WasSubmittedInTime(start.Value, submittedUtc))
                throw Guard.Error("Submit Medical Leave in the app within 48 hours of the Start Date, measured from 12:00 AM Malaysia time on that date.");
        }

        internal static void RequireCertificate(IOrganizationService service, Guid requestId,
            string category = "Medical Leave")
        {
            var q = new QueryExpression("annotation") {
                ColumnSet = new ColumnSet("isdocument", "filesize", "filename"),
                TopCount = 100
            };
            q.Criteria.AddCondition("objectid", ConditionOperator.Equal, requestId);
            var notes = service.RetrieveMultiple(q);
            if (notes.MoreRecords) throw Guard.Error("Too many attachments to verify " + category + " evidence safely.");
            foreach (var note in notes.Entities)
                if (note.GetAttributeValue<bool>("isdocument") &&
                    note.GetAttributeValue<int>("filesize") > 0 &&
                    !string.IsNullOrWhiteSpace(note.GetAttributeValue<string>("filename")))
                    return;
            throw Guard.Error("Upload a medical certificate to this " + category + " request before approval. The approver must verify the certificate.");
        }
    }
}
