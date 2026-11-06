using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using System.Linq;

namespace Leave.Plugins
{
    // Validation of new submissions only. Existing decisions/history are not
    // rewritten. Eligibility, documents, holidays and balances are separate.
    internal static class SubmissionValidation
    {
        internal static string Validate(Entity target, IOrganizationService callerService, IOrganizationService systemService)
        {
            var type = target.GetAttributeValue<EntityReference>("cr2c9_leavetypename");
            if (type == null || type.LogicalName != "cr2c9_leavetype" || type.Id == Guid.Empty)
                throw Guard.Error("Select a valid Leave Type before submitting.");
            var start = target.GetAttributeValue<DateTime?>("cr2c9_startdate");
            var end = target.GetAttributeValue<DateTime?>("cr2c9_enddate");
            if (!start.HasValue) throw Guard.Error("Enter a Start Date before submitting.");
            if (!end.HasValue) throw Guard.Error("Enter an End Date before submitting.");
            if (start.Value.TimeOfDay != TimeSpan.Zero || end.Value.TimeOfDay != TimeSpan.Zero)
                throw Guard.Error("Start Date and End Date must be calendar dates without a time.");
            if (end.Value < start.Value) throw Guard.Error("End Date cannot be earlier than Start Date.");

            // Use the caller's read permissions, and return no private record
            // contents. A deleted/unreadable selection also fails the submission.
            var metadata = ((RetrieveEntityResponse)systemService.Execute(new RetrieveEntityRequest {
                LogicalName = type.LogicalName, EntityFilters = EntityFilters.Entity
            })).EntityMetadata;
            if (string.IsNullOrWhiteSpace(metadata.PrimaryNameAttribute))
                throw Guard.Error("The selected Leave Type cannot be identified.");
            var selected = callerService.Retrieve(type.LogicalName, type.Id,
                new ColumnSet("statecode", metadata.PrimaryNameAttribute));
            if (selected.GetAttributeValue<OptionSetValue>("statecode")?.Value != 0)
                throw Guard.Error("The selected Leave Type is inactive or unavailable. Select an active Leave Type.");
            var name = selected.GetAttributeValue<string>(metadata.PrimaryNameAttribute);
            if (string.IsNullOrWhiteSpace(name))
                throw Guard.Error("The selected Leave Type has no name. Ask HR to correct it before submitting.");
            LeaveRangeSchedule.Dates(name, start.Value, end.Value);
            LeaveRangeSchedule.Hours(name, target);
            return name;
        }
    }

    internal static class ConsecutiveCalendarLeave
    {
        internal static void Validate(string leaveTypeName, DateTime start, DateTime end)
        {
            var days = leaveTypeName == "Maternity Leave" ? 98 :
                leaveTypeName == "Paternity Leave" ? 7 :
                leaveTypeName == "Marriage Leave" || leaveTypeName == "Compassionate Leave" ? 3 : 0;
            if (days == 0) return;
            if (start > DateTime.MaxValue.AddDays(-(days - 1)) || end != start.AddDays(days - 1))
                throw Guard.Error(leaveTypeName + " must end on " + (days - 1) +
                    " calendar days after its Start Date.");
        }

        internal static void RequireSavedDates(IOrganizationService service, Entity request, string leaveTypeName)
        {
            var start = request.GetAttributeValue<DateTime?>("cr2c9_startdate");
            var end = request.GetAttributeValue<DateTime?>("cr2c9_enddate");
            if (!start.HasValue || !end.HasValue || start.Value.TimeOfDay != TimeSpan.Zero ||
                end.Value.TimeOfDay != TimeSpan.Zero || request.Id == Guid.Empty)
                throw Guard.Error(leaveTypeName + " needs a complete saved date range before approval.");
            Validate(leaveTypeName, start.Value, end.Value);
            var count = (end.Value - start.Value).Days + 1;
            var query = new QueryExpression(Guard.Date) {
                ColumnSet = new ColumnSet("cr2c9_leaverequest", "cr2c9_leavedate", "cr2c9_duration", "statecode"),
                PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 }
            };
            query.Criteria.AddCondition("cr2c9_leaverequest", ConditionOperator.Equal, request.Id);
            var page = service.RetrieveMultiple(query);
            if (page.MoreRecords || page.Entities.Count != count)
                throw Guard.Error(leaveTypeName + " must have one saved full-day entry for every calendar date.");
            var saved = page.Entities.Select(row => {
                var owner = row.GetAttributeValue<EntityReference>("cr2c9_leaverequest");
                var date = row.GetAttributeValue<DateTime?>("cr2c9_leavedate");
                if (owner?.Id != request.Id || !date.HasValue || date.Value.TimeOfDay != TimeSpan.Zero ||
                    row.GetAttributeValue<OptionSetValue>("statecode")?.Value != 0 ||
                    row.GetAttributeValue<OptionSetValue>("cr2c9_duration")?.Value != 290040001)
                    throw Guard.Error(leaveTypeName + " has an invalid saved full-day entry.");
                return date.Value;
            }).OrderBy(date => date).ToArray();
            if (saved.Where((date, index) => date != start.Value.AddDays(index)).Any())
                throw Guard.Error(leaveTypeName + " has a missing or duplicate saved calendar date.");
        }
    }
}
