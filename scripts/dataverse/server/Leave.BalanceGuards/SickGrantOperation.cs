using System;
using System.Globalization;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Leave.Plugins;

namespace Leave.BalanceGuards
{
    internal static class SickContract
    {
        internal const string Api = "cr2c9_GrantCalendarSickLeave";
        // Fixed development reference. A Production import must bind the
        // migrated Sick Leave record explicitly before activating this action.
        internal static readonly Guid SickType = new Guid("261e2bd3-770e-4880-bcef-e4f5be6571e6");
        internal static bool IsMedicalName(string name) => name == "Medical Leave" || name == "Sick Leave";
        internal static IPluginExecutionContext GrantParent(IPluginExecutionContext context) {
            for (var parent = context.ParentContext; parent != null; parent = parent.ParentContext)
                if (parent.MessageName == Api && parent.Stage == 30 && parent.Mode == 0 && parent.IsInTransaction &&
                    parent.InitiatingUserId == context.InitiatingUserId)
                    return parent;
            return null;
        }
    }

    // One calendar-year outpatient sick balance. It posts the full statutory
    // service-band rate, never annual-leave accrual or hospitalisation credit.
    // Repeating the action only adds a verified increase after an anniversary.
    public sealed class CalendarSickGrantOperation : IPlugin
    {
        public void Execute(IServiceProvider provider) {
            var c = (IPluginExecutionContext)provider.GetService(typeof(IPluginExecutionContext));
            if (c.MessageName != SickContract.Api || c.Stage != 30 || c.Mode != 0 || !c.IsInTransaction)
                throw Guard.Error("Medical Leave grants require the registered synchronous action transaction.");
            var target = GrantContract.EmployeeTarget(c);
            if (c.InputParameters.Keys.Any(k => k != "Target" && k != "x-ms-app-name"))
                throw Guard.Error("Unsupported Medical Leave grant input.");
            var s = GrantContract.System(provider);
            GrantContract.GlobalPrivilege(s,c.InitiatingUserId,BalancePersistence.PeriodTable,
                Microsoft.Xrm.Sdk.Metadata.PrivilegeType.Create);
            var caller = ((IOrganizationServiceFactory)provider.GetService(typeof(IOrganizationServiceFactory)))
                .CreateOrganizationService(c.InitiatingUserId);
            caller.Retrieve(Guard.Employee,target.Id,new ColumnSet(false));
            var actor = s.Retrieve("systemuser",c.InitiatingUserId,new ColumnSet("isdisabled","azureactivedirectoryobjectid"));
            if (actor.GetAttributeValue<bool>("isdisabled") ||
                ApprovalContract.Identity(actor.GetAttributeValue<object>("azureactivedirectoryobjectid")) == Guid.Empty)
                throw Guard.Error("An active verified HR or administrative caller is required.");
            var columns = new ColumnSet("cr2c9_datejoined","cr2c9_employmenttype","cr2c9_isactive",
                "cr2c9_leavingdate","ownerid","statecode");
            var employee = s.Retrieve(Guard.Employee,target.Id,columns);
            GrantContract.Lock(s,employee);
            employee = s.Retrieve(Guard.Employee,target.Id,columns);
            var today = EntitlementCalculator.MalaysiaDate(DateTime.UtcNow);
            var start = new DateTime(today.Year,1,1);
            var end = new DateTime(today.Year,12,31);
            var joined = employee.GetAttributeValue<DateTime?>("cr2c9_datejoined");
            var employment = employee.GetAttributeValue<OptionSetValue>("cr2c9_employmenttype")?.Value;
            var rate = EntitlementCalculator.Calculate(joined,null,employment,today).Sick;
            var leaving = employee.GetAttributeValue<DateTime?>("cr2c9_leavingdate");
            if (employee.GetAttributeValue<OptionSetValue>("statecode")?.Value != 0 ||
                !employee.GetAttributeValue<bool>("cr2c9_isactive") ||
                leaving.HasValue && leaving.Value < today)
                rate = new EntitlementRate { Status="Inactive", Reason="No new Medical Leave grant for an inactive or departed employee." };
            if (leaving.HasValue && (leaving.Value.TimeOfDay != TimeSpan.Zero ||
                joined.HasValue && leaving.Value < joined.Value))
                rate = new EntitlementRate { Status="InvalidDetails", Reason="HR must correct the Leaving Date." };
            var periods = GrantContract.Periods(s,target.Id,start,end,SickContract.SickType);
            if (periods.Length == 0 && rate.Status != "Calculated") {
                Result(c,rate.Status,rate.Reason,start,end,0,null);
                return;
            }
            if (rate.Status == "Calculated") {
                var type = s.Retrieve("cr2c9_leavetype",SickContract.SickType,
                    new ColumnSet("cr2c9_leavetypename","statecode"));
                if (type.GetAttributeValue<OptionSetValue>("statecode")?.Value != 0 ||
                    !SickContract.IsMedicalName(type.GetAttributeValue<string>("cr2c9_leavetypename")))
                    throw Guard.Error("The configured Medical Leave category is inactive or changed.");
            }
            Entity period;
            if (periods.Length != 0) period = periods.Single();
            else {
                var owner = employee.GetAttributeValue<EntityReference>("ownerid");
                if (owner == null || owner.Id == Guid.Empty ||
                    (owner.LogicalName != "systemuser" && owner.LogicalName != "team"))
                    throw Guard.Error("Employee ownership is required for a Medical Leave balance.");
                period = new Entity(BalancePersistence.PeriodTable) {
                    ["cr2c9_name"] = "Medical Leave " + today.Year.ToString(CultureInfo.InvariantCulture),
                    ["cr2c9_employee"] = target,
                    ["cr2c9_leavetype"] = new EntityReference("cr2c9_leavetype",SickContract.SickType),
                    ["cr2c9_periodstart"] = start,["cr2c9_periodend"] = end,["ownerid"] = owner
                };
                period.Id = s.Create(period);
            }
            var scope = new BalancePeriod(period.Id,target.Id,SickContract.SickType,start,end);
            var plan = new BalancePersistence(s,c).ApplyGrant(scope,rate,"sick");
            Result(c,plan.Entry == null ? plan.Status : "Granted",
                plan.Reason ?? "Current calendar-year outpatient Medical Leave allowance.",start,end,
                plan.Entry?.Hours ?? 0,plan.ExistingAvailableHours + (plan.Entry?.Hours ?? 0));
        }
        static void Result(IPluginExecutionContext c,string status,string reason,DateTime start,
            DateTime end,int credited,int? available) {
            c.OutputParameters["Status"] = status;
            c.OutputParameters["Reason"] = reason ?? "";
            c.OutputParameters["PeriodStart"] = start.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
            c.OutputParameters["PeriodEnd"] = end.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
            c.OutputParameters["CreditedHours"] = credited;
            c.OutputParameters["AvailableHours"] = available ?? -1;
        }
    }
}
