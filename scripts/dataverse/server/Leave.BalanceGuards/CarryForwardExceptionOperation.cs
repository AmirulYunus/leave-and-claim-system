using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Leave.Plugins;

namespace Leave.BalanceGuards
{
    internal static class CarryForwardExceptionStore
    {
        internal const string Table = "cr2c9_carryforwardexception";
        internal const string Api = "cr2c9_RecordAnnualCarryForwardException";

        internal static Entity Read(IOrganizationService service, Guid sourceId)
        {
            var q = new QueryExpression(Table) {
                TopCount = 2,
                ColumnSet = new ColumnSet("cr2c9_employee", "cr2c9_sourceperiod",
                    "cr2c9_approvedlimithours", "cr2c9_documentreference",
                    "cr2c9_superiorentraid", "cr2c9_verifiedby", "cr2c9_verifiedon", "createdon")
            };
            q.Criteria.AddCondition("cr2c9_sourceperiod", ConditionOperator.Equal, sourceId);
            var rows = service.RetrieveMultiple(q).Entities;
            if (rows.Count > 1) throw Guard.Error("Duplicate written carry-forward exceptions require investigation.");
            return rows.SingleOrDefault();
        }

        internal static int? VerifiedLimit(Entity row, Guid employeeId, int sourceYear)
        {
            if (row == null) return null;
            var date = EntitlementCalculator.MalaysiaDate(
                row.GetAttributeValue<DateTime>("cr2c9_verifiedon"));
            if (row.GetAttributeValue<EntityReference>("cr2c9_employee")?.Id != employeeId ||
                row.GetAttributeValue<EntityReference>("cr2c9_sourceperiod") == null ||
                string.IsNullOrWhiteSpace(row.GetAttributeValue<string>("cr2c9_verifiedby")))
                throw Guard.Error("The saved written exception has invalid ownership or HR verification.");
            if (!Guid.TryParse(row.GetAttributeValue<string>("cr2c9_superiorentraid"), out var superior))
                throw Guard.Error("The written exception has no verified Superior identity.");
            var hours = row.GetAttributeValue<int>("cr2c9_approvedlimithours");
            try {
                CarryForwardExceptionPolicy.Validate(sourceYear, date, hours,
                    row.GetAttributeValue<string>("cr2c9_documentreference"), superior);
                CarryForwardExceptionPolicy.Validate(sourceYear, EntitlementCalculator.MalaysiaDate(
                    row.GetAttributeValue<DateTime>("createdon")), hours,
                    row.GetAttributeValue<string>("cr2c9_documentreference"), superior);
            } catch (Exception) { throw Guard.Error("The saved written exception is outside the policy limits."); }
            return hours;
        }
    }

    // HR attests to a written immediate-Superior approval before the 15 Dec
    // deadline. This immutable source-year record is rechecked at year-end.
    public sealed class CarryForwardExceptionOperation : IPlugin
    {
        public void Execute(IServiceProvider provider) => ExecuteAt(provider,
            EntitlementCalculator.MalaysiaDate(DateTime.UtcNow));

        internal void ExecuteAt(IServiceProvider provider, DateTime today)
        {
            var c = (IPluginExecutionContext)provider.GetService(typeof(IPluginExecutionContext));
            if (c == null || c.MessageName != CarryForwardExceptionStore.Api || c.Stage != 30 ||
                c.Mode != 0 || !c.IsInTransaction)
                throw Guard.Error("Written carry-forward approval requires the registered synchronous HR action.");
            var target = GrantContract.EmployeeTarget(c);
            if (c.InputParameters.Keys.Any(k => k != "Target" && k != "ApprovedLimitHours" &&
                k != "DocumentReference" && k != "x-ms-app-name"))
                throw Guard.Error("Unexpected written carry-forward approval input.");
            if (!(c.InputParameters["ApprovedLimitHours"] is int hours) ||
                !(c.InputParameters["DocumentReference"] is string reference))
                throw Guard.Error("An approved limit and written document reference are required.");
            var s = GrantContract.System(provider);
            var actor = s.Retrieve("systemuser", c.InitiatingUserId,
                new ColumnSet("isdisabled", "azureactivedirectoryobjectid"));
            var hrEntra = actor.GetAttributeValue<Guid>("azureactivedirectoryobjectid");
            if (actor.GetAttributeValue<bool>("isdisabled") || hrEntra == Guid.Empty ||
                !DecisionAuthority.IsHr(s, c.InitiatingUserId))
                throw Guard.Error("A directly assigned active Leave HR reviewer is required.");
            var caller = ((IOrganizationServiceFactory)provider.GetService(typeof(IOrganizationServiceFactory)))
                .CreateOrganizationService(c.InitiatingUserId);
            caller.Retrieve(Guard.Employee, target.Id, new ColumnSet(false));
            var employee = s.Retrieve(Guard.Employee, target.Id,
                new ColumnSet("cr2c9_superior", "cr2c9_entrauserid", "ownerid"));
            var superiorRef = employee.GetAttributeValue<EntityReference>("cr2c9_superior");
            if (superiorRef == null || superiorRef.LogicalName != Guard.Employee ||
                superiorRef.Id == target.Id)
                throw Guard.Error("A different current immediate Superior is required.");
            var superiorRow = s.Retrieve(Guard.Employee, superiorRef.Id,
                new ColumnSet("cr2c9_entrauserid", "cr2c9_isactive"));
            if (!Guid.TryParse(superiorRow.GetAttributeValue<string>("cr2c9_entrauserid"), out var superior) ||
                !superiorRow.GetAttributeValue<bool>("cr2c9_isactive"))
                throw Guard.Error("The current immediate Superior has no active verified Entra identity.");
            try { CarryForwardExceptionPolicy.Validate(today.Year, today, hours, reference, superior); }
            catch (Exception e) { throw Guard.Error(e.Message); }
            var sourceRows = GrantContract.Periods(s, target.Id,
                new DateTime(today.Year, 1, 1), new DateTime(today.Year, 12, 31));
            if (sourceRows.Length != 1)
                throw Guard.Error("The current-year Annual Leave period is required before recording a written exception.");
            var source = sourceRows[0];
            GrantContract.Lock(s, employee);
            var saved = CarryForwardExceptionStore.Read(s, source.Id);
            if (saved != null) {
                var existing = CarryForwardExceptionStore.VerifiedLimit(saved, target.Id, today.Year);
                if (existing != hours ||
                    saved.GetAttributeValue<string>("cr2c9_documentreference") != reference.Trim() ||
                    saved.GetAttributeValue<string>("cr2c9_superiorentraid") != superior.ToString("D"))
                    throw Guard.Error("A different written carry-forward exception is already recorded; HR must investigate.");
                c.OutputParameters["Status"] = "AlreadyRecorded";
                c.OutputParameters["ExceptionId"] = saved.Id.ToString("D");
                return;
            }
            var owner = source.GetAttributeValue<EntityReference>("ownerid");
            if (owner == null) throw Guard.Error("The Annual Leave period has no owner.");
            var id = s.Create(new Entity(CarryForwardExceptionStore.Table) {
                ["cr2c9_name"] = "Written carry-forward exception " + today.Year,
                ["cr2c9_employee"] = target,
                ["cr2c9_sourceperiod"] = source.ToEntityReference(),
                ["cr2c9_approvedlimithours"] = hours,
                ["cr2c9_documentreference"] = reference.Trim(),
                ["cr2c9_superiorentraid"] = superior.ToString("D"),
                ["cr2c9_verifiedby"] = hrEntra.ToString("D"),
                ["cr2c9_verifiedon"] = DateTime.UtcNow,
                ["ownerid"] = owner
            });
            c.OutputParameters["Status"] = "Recorded";
            c.OutputParameters["ExceptionId"] = id.ToString("D");
        }
    }
}
