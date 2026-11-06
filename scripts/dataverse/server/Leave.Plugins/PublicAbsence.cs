using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Leave.Plugins
{
    // An organization-readable projection, separate from the private request.
    // Its ID equals the request ID so approval retries cannot publish two rows.
    internal static class PublicAbsence
    {
        internal const string Table = "cr2c9_publicabsence";

        internal static void Publish(IOrganizationService service, Entity request, Entity employee)
        {
            var name = employee.GetAttributeValue<string>("cr2c9_employeename");
            var start = request.GetAttributeValue<DateTime?>("cr2c9_startdate");
            var end = request.GetAttributeValue<DateTime?>("cr2c9_enddate");
            var hours = UniformAnnualSchedule.NumericHours(request);
            if (string.IsNullOrWhiteSpace(name) || !start.HasValue || !end.HasValue ||
                start.Value.TimeOfDay != TimeSpan.Zero || end.Value.TimeOfDay != TimeSpan.Zero ||
                end.Value < start.Value || (hours != 4 && hours != 8))
                throw Guard.Error("The approved leave has incomplete public calendar details.");

            var row = new Entity(Table, request.Id);
            row["cr2c9_name"] = name.Trim();
            row["cr2c9_startdate"] = start.Value;
            row["cr2c9_enddate"] = end.Value;
            row["cr2c9_hoursperday"] = (int)hours.Value;
            foreach (var field in new[] { "cr2c9_department", "cr2c9_costcentre" })
                if (request.GetAttributeValue<EntityReference>(field) is EntityReference choice)
                    row[field] = choice;
            service.Create(row);
        }

        internal static void Remove(IOrganizationService service, Guid requestId)
        {
            // Older approvals may predate this projection. A missing row must
            // not prevent an otherwise valid future-leave cancellation.
            var rows = new QueryExpression(Table) {
                ColumnSet = new ColumnSet(false), TopCount = 1
            };
            rows.Criteria.AddCondition(Table + "id", ConditionOperator.Equal, requestId);
            if (service.RetrieveMultiple(rows).Entities.Count != 0)
                service.Delete(Table, requestId);
        }
    }
}
