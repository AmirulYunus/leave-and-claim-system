using System;
using Microsoft.Xrm.Sdk;

namespace Leave.Plugins
{
    internal static class MaternityApprovalRules
    {
        internal const string ExpectedConfinement = "cr2c9_expectedconfinementdate";
        internal const string FormalNotice = "cr2c9_maternityformalnoticedate";
        internal const string HrVerified = "cr2c9_maternityhrverifiedon";
        internal static readonly Guid TypeId = new Guid("7b3543af-6be9-49f2-a222-724297ef4f09");

        internal static bool IsMaternity(Entity request) =>
            request.GetAttributeValue<EntityReference>("cr2c9_leavetypename")?.Id == TypeId;

        internal static void RequireDates(Entity request)
        {
            var expected = request.GetAttributeValue<DateTime?>(ExpectedConfinement);
            var notice = request.GetAttributeValue<DateTime?>(FormalNotice);
            if (!expected.HasValue || !notice.HasValue)
                throw Guard.Error("Maternity approval needs the expected confinement date and formal notice date on the request.");
            if (expected.Value.TimeOfDay != TimeSpan.Zero || notice.Value.TimeOfDay != TimeSpan.Zero)
                throw Guard.Error("Maternity confinement and formal notice must be calendar dates.");
            // Section 40(2) refers to notice within the 60 days preceding
            // expected confinement. Sections 40(5)-(6) also protect claims
            // despite notice defects or a reasonable late-notice cause.
            // Record both dates and leave the assessment to the approver;
            // do not turn the notice window into an automatic leave denial.
        }

        internal static void RequireReview(IOrganizationService service, Entity request)
        {
            RequireDates(request);
            LeaveRangeSchedule.Hours("Maternity Leave", request);
            ConsecutiveCalendarLeave.RequireSavedDates(service, request, "Maternity Leave");
        }
    }
}
