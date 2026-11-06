using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Leave.Plugins
{
    internal static class UniformAnnualSchedule
    {
        internal static readonly Guid AnnualType = new Guid("959c93de-1197-4de4-a660-2e2c3266f0fe");
        internal const string Step = "Leave uniform annual dates";
        internal static DateTime[] Dates(DateTime start, DateTime end)
        {
            if (start.TimeOfDay != TimeSpan.Zero || end.TimeOfDay != TimeSpan.Zero || end < start ||
                (end - start).Days > 366)
                throw Guard.Error("Choose a valid Annual Leave date range of at most 367 calendar days.");
            var dates = new List<DateTime>();
            for (var date = start; date <= end; date = date.AddDays(1))
                if (date.DayOfWeek != DayOfWeek.Saturday && date.DayOfWeek != DayOfWeek.Sunday)
                    dates.Add(date);
            if (dates.Count == 0) throw Guard.Error("The Annual Leave range must include a Monday–Friday date.");
            return dates.ToArray();
        }
        internal static int Hours(Entity request)
        {
            var hours = NumericHours(request);
            if (hours != 4 && hours != 8)
                throw Guard.Error("Annual Leave must be 4 or 8 hours for every selected weekday. Create another request for a different duration.");
            return (int)hours.Value;
        }
        internal static decimal? NumericHours(Entity request)
        {
            if (!request.Contains("cr2c9_hoursperday") || request["cr2c9_hoursperday"] == null) return null;
            var value = request["cr2c9_hoursperday"];
            if (value is decimal || value is double || value is int) return Convert.ToDecimal(value);
            throw Guard.Error("Annual Leave hours have an unsupported value type.");
        }
        internal static bool IsAnnual(Entity request) =>
            request.GetAttributeValue<EntityReference>("cr2c9_leavetypename")?.Id == AnnualType;
        internal static bool TrustedParent(IOrganizationService service, IPluginExecutionContext context, Guid requestId)
        {
            for (var p = context.ParentContext; p != null; p = p.ParentContext)
                if (p.Stage == 40 && p.Mode == 0 && p.IsInTransaction &&
                    p.PrimaryEntityName == Guard.Request && p.PrimaryEntityId == requestId &&
                    (p.MessageName == "Create" || p.MessageName == "Update") &&
                    Guard.VerifiedStep(service, p, Step + ": " + p.MessageName))
                    return true;
            return false;
        }
        internal static bool TrustedDateRegeneration(IOrganizationService service, IPluginExecutionContext context)
        {
            // In live Dataverse, date writes made by the registered post-operation
            // request step can arrive without that step in ParentContext. The
            // server-owned ObjectModel Delete/Create is nested under an Upsert
            // envelope and runs as SYSTEM. Direct employee date writes do not
            // have this envelope; only allow this exact internal shape.
            var platform = context.ParentContext;
            var envelope = platform?.ParentContext;
            if (context.Depth != 2 || context.Stage != 20 || !context.IsInTransaction ||
                context.PrimaryEntityName != Guard.Date ||
                (context.MessageName != "Create" && context.MessageName != "Delete") ||
                platform?.Stage != 30 || !platform.IsInTransaction ||
                platform.PrimaryEntityName != Guard.Date || platform.MessageName != context.MessageName ||
                platform.OwningExtension?.Name != "ObjectModel Implementation" ||
                envelope?.Stage != 30 || !envelope.IsInTransaction ||
                envelope.MessageName != "Upsert" || envelope.OwningExtension?.Name != "ObjectModel Implementation" ||
                context.InitiatingUserId == Guid.Empty || context.InitiatingUserId != context.UserId)
                return false;
            var user = service.Retrieve("systemuser", context.InitiatingUserId,
                new ColumnSet("azureactivedirectoryobjectid", "isdisabled"));
            return user.GetAttributeValue<bool>("isdisabled") &&
                Guard.Entra(user.GetAttributeValue<object>("azureactivedirectoryobjectid")) == Guid.Empty;
        }
    }

    // Registered synchronously after a request is saved. It retains the
    // existing step identity because date-write authorization verifies that
    // registered step. The pre-operation guard validates the entire form.
    public sealed class UniformAnnualDates : IPlugin
    {
        public void Execute(IServiceProvider provider)
        {
            var context = (IPluginExecutionContext)provider.GetService(typeof(IPluginExecutionContext));
            if (context.Stage != 40 || context.Mode != 0 || !context.IsInTransaction ||
                context.PrimaryEntityName != Guard.Request || context.PrimaryEntityId == Guid.Empty ||
                (context.MessageName != "Create" && context.MessageName != "Update") ||
                context.OwningExtension?.LogicalName != "sdkmessageprocessingstep" ||
                context.OwningExtension.Name != UniformAnnualSchedule.Step + ": " + context.MessageName)
                throw Guard.Error("Uniform annual dates require the registered synchronous request transaction.");
            if (!(context.InputParameters["Target"] is Entity target) ||
                (target.Id != Guid.Empty && target.Id != context.PrimaryEntityId))
                throw Guard.Error("The saved Annual Leave request is missing.");
            if (context.MessageName == "Update" &&
                !target.Contains("cr2c9_startdate") && !target.Contains("cr2c9_enddate") &&
                !target.Contains("cr2c9_hoursperday") && !target.Contains("cr2c9_leavetypename")) return;
            var factory = (IOrganizationServiceFactory)provider.GetService(typeof(IOrganizationServiceFactory));
            var service = factory.CreateOrganizationService(null);
            var request = service.Retrieve(Guard.Request, context.PrimaryEntityId,
                new ColumnSet("cr2c9_leavetypename", "cr2c9_startdate", "cr2c9_enddate", "cr2c9_hoursperday", "cr2c9_status"));
            if (request.GetAttributeValue<OptionSetValue>("cr2c9_status")?.Value != 0)
                throw Guard.Error("Only pending leave dates can be regenerated.");
            var name = LeaveRangeSchedule.TypeName(service, request);
            var dates = LeaveRangeSchedule.Dates(name,
                request.GetAttributeValue<DateTime>("cr2c9_startdate"),
                request.GetAttributeValue<DateTime>("cr2c9_enddate"));
            var hours = LeaveRangeSchedule.Hours(name, request);
            var query = new QueryExpression(Guard.Date) {
                ColumnSet = new ColumnSet(false),
                PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 }
            };
            query.Criteria.AddCondition("cr2c9_leaverequest", ConditionOperator.Equal, request.Id);
            var existing = service.RetrieveMultiple(query);
            if (existing.MoreRecords) throw Guard.Error("Too many saved leave dates to edit safely.");
            foreach (var row in existing.Entities) service.Delete(Guard.Date, row.Id);
            foreach (var date in dates) {
                var row = new Entity(Guard.Date);
                row["cr2c9_entryname"] = "Leave date " + date.ToString("yyyy-MM-dd");
                row["cr2c9_leaverequest"] = request.ToEntityReference();
                row["cr2c9_leavedate"] = date;
                row["cr2c9_duration"] = new OptionSetValue(hours == 4 ? 290040000 : 290040001);
                service.Create(row);
            }
        }
    }
}
