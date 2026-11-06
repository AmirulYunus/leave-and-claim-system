using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Leave.Plugins
{
    // The request form owns the complete range. Every saved request has its
    // per-date rows created in the same transaction, so there is no second
    // employee submission step or partially populated pending request.
    internal static class LeaveRangeSchedule
    {
        internal static string TypeName(IOrganizationService service, Entity request)
        {
            var type = request.GetAttributeValue<EntityReference>("cr2c9_leavetypename");
            if (type == null || type.LogicalName != "cr2c9_leavetype" || type.Id == Guid.Empty)
                throw Guard.Error("Choose a valid Leave Type.");
            var record = service.Retrieve(type.LogicalName, type.Id, new ColumnSet("cr2c9_leavetypename"));
            var name = record.GetAttributeValue<string>("cr2c9_leavetypename");
            if (string.IsNullOrWhiteSpace(name)) throw Guard.Error("The Leave Type has no name.");
            return name;
        }

        internal static bool IsCalendar(string name) =>
            name == "Maternity Leave" || name == "Paternity Leave" ||
            name == "Marriage Leave" || name == "Compassionate Leave";

        internal static bool IsWeekday(string name) =>
            name == "Annual Leave" || name == "Medical Leave" || name == "Sick Leave" ||
            name == "Hospitalisation Leave" || name == "Hospitalization Leave" ||
            name == "Emergency Leave" || name == "Training Leave" ||
            name == "Unpaid Leave" || name == "Replacement Leave" ||
            name == "Off In Lieu" || name == "Off-in-lieu";

        internal static DateTime[] Dates(string name, DateTime start, DateTime end)
        {
            if (!IsCalendar(name) && !IsWeekday(name))
                throw Guard.Error("This Leave Type needs a configured date rule before it can be submitted.");
            if (start.TimeOfDay != TimeSpan.Zero || end.TimeOfDay != TimeSpan.Zero || end < start ||
                (end - start).Days > 366)
                throw Guard.Error("Choose a valid leave date range of at most 367 calendar days.");
            ConsecutiveCalendarLeave.Validate(name, start, end);
            var dates = new List<DateTime>();
            for (var date = start; date <= end; date = date.AddDays(1))
                if (IsCalendar(name) || (date.DayOfWeek != DayOfWeek.Saturday && date.DayOfWeek != DayOfWeek.Sunday))
                    dates.Add(date);
            if (dates.Count == 0) throw Guard.Error("The leave range must include a Monday–Friday date.");
            return dates.ToArray();
        }

        internal static int Hours(string name, Entity request)
        {
            var hours = UniformAnnualSchedule.NumericHours(request);
            if (IsCalendar(name)) {
                if (hours != 8) throw Guard.Error(name + " is consecutive full calendar days; choose 8 hours per day.");
                return 8;
            }
            if (hours != 4 && hours != 8)
                throw Guard.Error("Choose 4 or 8 hours for every included leave date. Use a separate request for a different duration.");
            return (int)hours.Value;
        }
    }
}
