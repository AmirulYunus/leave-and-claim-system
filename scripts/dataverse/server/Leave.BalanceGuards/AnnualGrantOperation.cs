using System;
using System.Linq;
using System.Globalization;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Crm.Sdk.Messages;
using Leave.Plugins;

// The linked accounting code stays identical to its tested source. This assembly
// is deployed independently of request approvals and undeployed submission code.
#if !USE_EXISTING_GUARD
namespace Leave.Plugins {
    internal static class Guard {
        internal const string Employee = "cr2c9_employee", Request = "cr2c9_leaverequest", LockField = "cr2c9_reviewlocktoken";
        internal static InvalidPluginExecutionException Error(string message) => new InvalidPluginExecutionException(message);
    }
}
#endif
namespace Leave.BalanceGuards
{
    internal static class GrantContract
    {
        internal const string Api = "cr2c9_GrantCalendarAnnualLeave";
        internal const string Privilege = "prvCreatecr2c9_leavebalanceperiod";
        // Development binding is explicit. Production deployment must bind the
        // migrated Annual Leave reference; never identify a category by name alone.
        internal static readonly Guid AnnualType = new Guid("959c93de-1197-4de4-a660-2e2c3266f0fe");
        internal static IOrganizationService System(IServiceProvider p) =>
            ((IOrganizationServiceFactory)p.GetService(typeof(IOrganizationServiceFactory))).CreateOrganizationService(null);
        internal static EntityReference EmployeeTarget(IPluginExecutionContext c) {
            if (!c.InputParameters.Contains("Target") || !(c.InputParameters["Target"] is EntityReference e) ||
                e.LogicalName != Guard.Employee || e.Id == Guid.Empty) throw Guard.Error("A saved Employee is required.");
            return e;
        }
        internal static void GlobalPrivilege(IOrganizationService service, Guid user, string table, PrivilegeType type) {
            if (user == Guid.Empty) throw Guard.Error("An authorized administrative caller is required.");
            var metadata = ((RetrieveEntityResponse)service.Execute(new RetrieveEntityRequest {
                LogicalName = table, EntityFilters = EntityFilters.Privileges
            })).EntityMetadata;
            var privilege = metadata.Privileges.Single(x => x.PrivilegeType == type);
            var privileges = ((RetrieveUserPrivilegesResponse)service.Execute(new RetrieveUserPrivilegesRequest { UserId = user })).RolePrivileges;
            if (!privileges.Any(x => x.PrivilegeId == privilege.PrivilegeId && x.Depth == PrivilegeDepth.Global))
                throw Guard.Error("Organization-wide administrative privilege is required for this operation.");
        }
        internal static bool SystemColumn(string k) => k == "modifiedon" || k == "modifiedby" || k == "modifiedonbehalfby" || k == "timezoneruleversionnumber" || k == "utcconversiontimezonecode";
        internal static IPluginExecutionContext GrantParent(IPluginExecutionContext c) {
            for (var parent = c.ParentContext; parent != null; parent = parent.ParentContext)
                if (parent.MessageName == Api && parent.Stage == 30 && parent.Mode == 0 && parent.IsInTransaction &&
                    parent.InitiatingUserId == c.InitiatingUserId) return parent;
            return null;
        }
        internal static Entity[] Periods(IOrganizationService s, Guid employee, DateTime start, DateTime end,
            Guid? leaveType = null) {
            var type = leaveType ?? AnnualType;
            var q = new QueryExpression(BalancePersistence.PeriodTable) { ColumnSet = new ColumnSet("cr2c9_employee", "cr2c9_leavetype", "cr2c9_periodstart", "cr2c9_periodend", "ownerid"), TopCount = 3 };
            q.Criteria.AddCondition("cr2c9_employee", ConditionOperator.Equal, employee);
            q.Criteria.AddCondition("cr2c9_leavetype", ConditionOperator.Equal, type);
            q.Criteria.AddCondition("cr2c9_periodstart", ConditionOperator.LessEqual, end);
            q.Criteria.AddCondition("cr2c9_periodend", ConditionOperator.GreaterEqual, start);
            var rows = s.RetrieveMultiple(q).Entities.ToArray();
            if (rows.Length > 1 || rows.Any(x => x.GetAttributeValue<DateTime?>("cr2c9_periodstart") != start || x.GetAttributeValue<DateTime?>("cr2c9_periodend") != end))
                throw Guard.Error("Leave balance periods overlap or have different boundaries; review required.");
            return rows;
        }
        internal static void Lock(IOrganizationService s, Entity row) {
            if (string.IsNullOrWhiteSpace(row.RowVersion)) throw Guard.Error("Employee row version is required; refresh and retry.");
            var e = new Entity(row.LogicalName, row.Id) { RowVersion = row.RowVersion };
            e[Guard.LockField] = Guid.NewGuid().ToString("D");
            s.Execute(new UpdateRequest { Target = e, ConcurrencyBehavior = ConcurrencyBehavior.IfRowVersionMatches });
        }
    }

    public sealed class CalendarAnnualGrantOperation : IPlugin
    {
        public void Execute(IServiceProvider provider) {
            var c = (IPluginExecutionContext)provider.GetService(typeof(IPluginExecutionContext));
            if (c.MessageName != GrantContract.Api || c.Stage != 30 || c.Mode != 0 || !c.IsInTransaction)
                throw Guard.Error("Annual grants require the registered synchronous action transaction.");
            var target = GrantContract.EmployeeTarget(c);
            // No caller-supplied year, date, leave type, allowance or ownership.
            // Model-driven Web API supplies this telemetry header; it is ignored and never authorizes a grant.
            if (c.InputParameters.Keys.Any(k => k != "Target" && k != "x-ms-app-name")) throw Guard.Error("Unsupported annual grant input.");
            var s = GrantContract.System(provider);
            GrantContract.GlobalPrivilege(s, c.InitiatingUserId, BalancePersistence.PeriodTable, PrivilegeType.Create);
            var caller = ((IOrganizationServiceFactory)provider.GetService(typeof(IOrganizationServiceFactory))).CreateOrganizationService(c.InitiatingUserId);
            caller.Retrieve(Guard.Employee, target.Id, new ColumnSet(false)); // Respect caller record read access.
            var user = s.Retrieve("systemuser", c.InitiatingUserId, new ColumnSet("isdisabled"));
            if (user.GetAttributeValue<bool>("isdisabled")) throw Guard.Error("The caller is disabled.");
            var columns = new ColumnSet("cr2c9_datejoined", "cr2c9_employeelevel", "cr2c9_employmenttype", "cr2c9_isactive", "cr2c9_leavingdate", "ownerid", "statecode");
            var employee = s.Retrieve(Guard.Employee, target.Id, columns);
            GrantContract.Lock(s, employee); // All period creation locks employee first.
            employee = s.Retrieve(Guard.Employee, target.Id, columns);
            var date = EntitlementCalculator.MalaysiaDate(DateTime.UtcNow);
            var assessment = CalendarAnnualGrant.Assess(employee.GetAttributeValue<DateTime?>("cr2c9_datejoined"),
                employee.GetAttributeValue<OptionSetValue>("cr2c9_employeelevel")?.Value,
                employee.GetAttributeValue<OptionSetValue>("cr2c9_employmenttype")?.Value,
                employee.GetAttributeValue<bool?>("cr2c9_isactive"), employee.GetAttributeValue<DateTime?>("cr2c9_leavingdate"), date);
            var state = employee.GetAttributeValue<OptionSetValue>("statecode")?.Value;
            if (!state.HasValue) throw Guard.Error("Employee record state is required.");
            if (state.Value != 0) assessment.Annual = new EntitlementRate { Status = "Inactive", Reason = "No new grant for a deactivated Employee record; preserve history." };
            var joined = employee.GetAttributeValue<DateTime?>("cr2c9_datejoined");
            // A first-year result is held only by the superseded proration rule
            // when all rate and employment inputs passed validation. Other
            // PendingPolicy outcomes (including departure and long service)
            // stay held. Credit only cumulative leave earned through today.
            var firstYearReady = joined.HasValue && joined.Value >= assessment.PeriodStart &&
                assessment.Annual.Status == "PendingPolicy" &&
                assessment.Annual.Reason == "First-year grants and proration are on hold.";
            var canGrant = assessment.Annual.Status == "Calculated" || firstYearReady;
            int? earnedHours = null;
            if (canGrant) earnedHours = CalendarAnnualGrant.ApprovalEarnedCap(joined,
                employee.GetAttributeValue<OptionSetValue>("cr2c9_employeelevel")?.Value,
                employee.GetAttributeValue<OptionSetValue>("cr2c9_employmenttype")?.Value,
                employee.GetAttributeValue<bool?>("cr2c9_isactive"),
                employee.GetAttributeValue<DateTime?>("cr2c9_leavingdate"),assessment.PeriodStart,date);
            var periods = GrantContract.Periods(s, target.Id, assessment.PeriodStart, assessment.PeriodEnd);
            if (periods.Length == 0 && !canGrant) {
                Result(c, assessment.Annual.Status, assessment.Annual.Reason, assessment.PeriodStart, assessment.PeriodEnd, 0, null);
                return; // No empty period or zero entitlement for missing/held inputs.
            }
            if (canGrant) {
                var type = s.Retrieve("cr2c9_leavetype", GrantContract.AnnualType, new ColumnSet("statecode"));
                if (type.GetAttributeValue<OptionSetValue>("statecode")?.Value != 0) throw Guard.Error("The configured Annual Leave category is inactive.");
            }
            Entity period;
            if (periods.Length != 0) period = periods.Single();
            else {
                var owner = employee.GetAttributeValue<EntityReference>("ownerid");
                if (owner == null || (owner.LogicalName != "systemuser" && owner.LogicalName != "team")) throw Guard.Error("Employee ownership is required.");
                period = new Entity(BalancePersistence.PeriodTable) {
                    ["cr2c9_name"] = "Annual Leave " + date.Year.ToString(CultureInfo.InvariantCulture),
                    ["cr2c9_employee"] = target, ["cr2c9_leavetype"] = new EntityReference("cr2c9_leavetype", GrantContract.AnnualType),
                    ["cr2c9_periodstart"] = assessment.PeriodStart, ["cr2c9_periodend"] = assessment.PeriodEnd, ["ownerid"] = owner
                };
                period.Id = s.Create(period);
            }
            var scope = new BalancePeriod(period.Id, target.Id, GrantContract.AnnualType, assessment.PeriodStart, assessment.PeriodEnd);
            var plan = canGrant
                ? new BalancePersistence(s, c).ApplyEarnedGrant(scope, earnedHours.Value)
                : new BalancePersistence(s, c).ApplyGrant(scope, assessment.Annual);
            Result(c, plan.Entry == null ? plan.Status : "Granted", plan.Reason ?? (canGrant ? "Cumulative annual leave earned through today." : assessment.Annual.Reason),
                assessment.PeriodStart, assessment.PeriodEnd, plan.Entry?.Hours ?? 0,
                plan.ExistingAvailableHours + (plan.Entry?.Hours ?? 0));
        }
        static void Result(IPluginExecutionContext c, string status, string reason, DateTime start, DateTime end, int credited, int? available) {
            c.OutputParameters["Status"] = status; c.OutputParameters["Reason"] = reason ?? "";
            c.OutputParameters["PeriodStart"] = start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            c.OutputParameters["PeriodEnd"] = end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            c.OutputParameters["CreditedHours"] = credited;
            // -1 means no calculated period, never a zero entitlement.
            c.OutputParameters["AvailableHours"] = available ?? -1;
        }
    }

    // Temporary administrative maintenance boundary. This does not map HR by
    // email or assign HR roles. Existing organization-wide Employee writers can
    // maintain inputs; narrower employee privileges cannot change policy fields.
    public sealed class EmploymentInputGuard : IPlugin {
        public void Execute(IServiceProvider provider) {
            var c = (IPluginExecutionContext)provider.GetService(typeof(IPluginExecutionContext));
            if (c.Stage != 20 || c.Mode != 0 || !c.IsInTransaction || c.PrimaryEntityName != Guard.Employee) throw Guard.Error("Invalid employment safeguard registration.");
            var targets = c.InputParameters.Contains("Targets") ? ((EntityCollection)c.InputParameters["Targets"]).Entities.ToArray() :
                new[] { (Entity)c.InputParameters["Target"] };
            var protectedFields = new[] { "cr2c9_datejoined", "cr2c9_employeelevel", "cr2c9_employmenttype", "cr2c9_isactive", "cr2c9_leavingdate", "cr2c9_position", "cr2c9_probationcompletedon", "cr2c9_entrauserid", "cr2c9_superior" };
            if (targets.Any(t => t.Attributes.Keys.Any(protectedFields.Contains)))
                GrantContract.GlobalPrivilege(GrantContract.System(provider), c.InitiatingUserId, Guard.Employee,
                    c.MessageName.StartsWith("Create", StringComparison.Ordinal) ? PrivilegeType.Create : PrivilegeType.Write);
            // HR may maintain employment policy inputs but must not rebind a
            // profile to another Entra account or replace its synced Superior.
            // The trusted IT/sync administrator keeps the stronger create scope.
            if (targets.Any(t => t.Contains("cr2c9_entrauserid") || t.Contains("cr2c9_superior")))
                GrantContract.GlobalPrivilege(GrantContract.System(provider), c.InitiatingUserId,
                    Guard.Employee, PrivilegeType.Create);
        }
    }
}
