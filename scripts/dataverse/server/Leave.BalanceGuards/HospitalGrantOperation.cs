using System;
using System.Globalization;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Leave.Plugins;

namespace Leave.BalanceGuards
{
    internal static class HospitalContract
    {
        internal const string Api = "cr2c9_GrantCalendarHospitalisationLeave";
        // Fixed Developer reference; rebind the leave type after a Production import.
        internal static readonly Guid HospitalType = new Guid("91f9367f-5bd7-4a06-a750-124c36d5729f");
        internal static IPluginExecutionContext GrantParent(IPluginExecutionContext context) {
            for (var parent = context.ParentContext; parent != null; parent = parent.ParentContext)
                if (parent.MessageName == Api && parent.Stage == 30 && parent.Mode == 0 && parent.IsInTransaction &&
                    parent.InitiatingUserId == context.InitiatingUserId) return parent;
            return null;
        }
    }

    // The separate 60-day period protects the statutory hospitalisation floor.
    // The combined company figure is calculated from this and Medical usage.
    public sealed class CalendarHospitalGrantOperation : IPlugin
    {
        public void Execute(IServiceProvider provider) {
            var c = (IPluginExecutionContext)provider.GetService(typeof(IPluginExecutionContext));
            if (c.MessageName != HospitalContract.Api || c.Stage != 30 || c.Mode != 0 || !c.IsInTransaction)
                throw Guard.Error("Hospitalisation grants require the registered synchronous action transaction.");
            var target = GrantContract.EmployeeTarget(c);
            if (c.InputParameters.Keys.Any(k => k != "Target" && k != "x-ms-app-name"))
                throw Guard.Error("Unsupported Hospitalisation grant input.");
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
            var rate = EntitlementCalculator.Calculate(joined,null,employment,today).Hospitalisation;
            var leaving = employee.GetAttributeValue<DateTime?>("cr2c9_leavingdate");
            if (employee.GetAttributeValue<OptionSetValue>("statecode")?.Value != 0 ||
                !employee.GetAttributeValue<bool>("cr2c9_isactive") || leaving.HasValue && leaving.Value < today)
                rate = new EntitlementRate { Status="Inactive", Reason="No new Hospitalisation grant for an inactive or departed employee." };
            if (leaving.HasValue && (leaving.Value.TimeOfDay != TimeSpan.Zero ||
                joined.HasValue && leaving.Value < joined.Value))
                rate = new EntitlementRate { Status="InvalidDetails", Reason="HR must correct the Leaving Date." };
            var periods = GrantContract.Periods(s,target.Id,start,end,HospitalContract.HospitalType);
            if (periods.Length == 0 && rate.Status != "Calculated") {
                Result(c,rate.Status,rate.Reason,start,end,0,null); return;
            }
            if (rate.Status == "Calculated") {
                var type = s.Retrieve("cr2c9_leavetype",HospitalContract.HospitalType,
                    new ColumnSet("cr2c9_leavetypename","statecode"));
                if (type.GetAttributeValue<OptionSetValue>("statecode")?.Value != 0 ||
                    type.GetAttributeValue<string>("cr2c9_leavetypename") != "Hospitalisation Leave")
                    throw Guard.Error("The configured Hospitalisation category is inactive or changed.");
            }
            Entity period;
            if (periods.Length != 0) period = periods.Single();
            else {
                var owner = employee.GetAttributeValue<EntityReference>("ownerid");
                if (owner == null || owner.Id == Guid.Empty ||
                    (owner.LogicalName != "systemuser" && owner.LogicalName != "team"))
                    throw Guard.Error("Employee ownership is required for a Hospitalisation balance.");
                period = new Entity(BalancePersistence.PeriodTable) {
                    ["cr2c9_name"] = "Hospitalisation Leave " + today.Year.ToString(CultureInfo.InvariantCulture),
                    ["cr2c9_employee"] = target,
                    ["cr2c9_leavetype"] = new EntityReference("cr2c9_leavetype",HospitalContract.HospitalType),
                    ["cr2c9_periodstart"] = start,["cr2c9_periodend"] = end,["ownerid"] = owner
                };
                period.Id = s.Create(period);
            }
            var scope = new BalancePeriod(period.Id,target.Id,HospitalContract.HospitalType,start,end);
            var plan = new BalancePersistence(s,c).ApplyGrant(scope,rate,"hospitalisation");
            Result(c,plan.Entry == null ? plan.Status : "Granted",
                plan.Reason ?? "Current calendar-year Hospitalisation Leave allowance.",start,end,
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
