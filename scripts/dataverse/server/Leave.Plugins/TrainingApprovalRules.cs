using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Leave.Plugins
{
    internal static class TrainingApprovalRules
    {
        internal static void RequireSavedDates(IOrganizationService service, Entity request, string typeName = TrainingPolicy.TypeName)
        {
            if (typeName == TrainingPolicy.TypeName) TrainingPolicy.ChargesAnnual(request);
            else if (typeName != UnpaidPolicy.TypeName) throw Guard.Error("Unsupported weekday leave category.");
            var start = request.GetAttributeValue<DateTime?>("cr2c9_startdate");
            var end = request.GetAttributeValue<DateTime?>("cr2c9_enddate");
            if (!start.HasValue || !end.HasValue || request.Id == Guid.Empty)
                throw Guard.Error(typeName + " needs a complete saved date range.");
            var expected = LeaveRangeSchedule.Dates(typeName, start.Value, end.Value);
            var hours = LeaveRangeSchedule.Hours(typeName, request);
            var query = new QueryExpression(Guard.Date) {
                ColumnSet = new ColumnSet("cr2c9_leaverequest", "cr2c9_leavedate", "cr2c9_duration", "statecode"),
                PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 }
            };
            query.Criteria.AddCondition("cr2c9_leaverequest", ConditionOperator.Equal, request.Id);
            var page = service.RetrieveMultiple(query);
            if (page.MoreRecords || page.Entities.Count != expected.Length)
                throw Guard.Error(typeName + " must have one saved entry for every weekday.");
            var dates = page.Entities.Select(row => {
                var owner = row.GetAttributeValue<EntityReference>("cr2c9_leaverequest");
                var day = row.GetAttributeValue<DateTime?>("cr2c9_leavedate");
                var duration = row.GetAttributeValue<OptionSetValue>("cr2c9_duration")?.Value;
                if (owner?.Id != request.Id || !day.HasValue || day.Value.TimeOfDay != TimeSpan.Zero ||
                    row.GetAttributeValue<OptionSetValue>("statecode")?.Value != 0 ||
                    duration != (hours == 4 ? 290040000 : 290040001))
                    throw Guard.Error(typeName + " has an invalid saved date or duration.");
                return day.Value;
            }).OrderBy(day => day).ToArray();
            if (!dates.SequenceEqual(expected))
                throw Guard.Error(typeName + " has a missing or duplicate saved weekday.");
        }
    }
}
