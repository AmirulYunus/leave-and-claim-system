using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Leave.Plugins;

namespace Leave.BalanceGuards
{
    // Retain the registered type identity and all existing enabled steps.
    // External writes remain denied, including administrators and bulk calls.
    public sealed class BalanceStagingGuard : IPlugin
    {
        public void Execute(IServiceProvider provider) {
            var c = (IPluginExecutionContext)provider.GetService(typeof(IPluginExecutionContext));
            var parent = GrantContract.GrantParent(c);
            var sickParent = SickContract.GrantParent(c);
            var hospitalParent = HospitalContract.GrantParent(c);
            if ((parent != null ? 1 : 0) + (sickParent != null ? 1 : 0) + (hospitalParent != null ? 1 : 0) > 1)
                throw Guard.Error("Ambiguous balance grant parent.");
            var grantParent = parent ?? sickParent ?? hospitalParent;
            var grantType = parent != null ? GrantContract.AnnualType : sickParent != null ? SickContract.SickType : HospitalContract.HospitalType;
            var grantKey = parent != null ? ":annual-grant:" : sickParent != null ? ":sick-grant:" : ":hospitalisation-grant:";
            // Standard-table Create/Update also traverse the merged multiple
            // pipeline. The action issues one row per SDK call, never batches.
            var message = c.MessageName.EndsWith("Multiple", StringComparison.Ordinal)
                ? c.MessageName.Substring(0, c.MessageName.Length - "Multiple".Length) : c.MessageName;
            var approval = ApprovalContract.Parent(c);
            if (approval != null) { ApprovalWrite(provider, c, approval, message); return; }
            var cancellation = CancellationContract.Parent(c);
            if (cancellation != null) { CancellationWrite(provider, c, cancellation, message); return; }
            var carryConfirmation = CarryForwardConfirmContract.Parent(c);
            if (carryConfirmation != null) { CarryForwardLock(provider, c, carryConfirmation, message); return; }
            if (c.Stage != 20 || c.Mode != 0 || !c.IsInTransaction || grantParent == null ||
                (message != "Create" && message != "Update"))
                throw Guard.Error("Balances can be changed only by an authorized leave grant action; ledger entries are immutable.");
            var employee = GrantContract.EmployeeTarget(grantParent);
            Entity target;
            if (c.InputParameters.Contains("Targets")) {
                var collection = c.InputParameters["Targets"] as EntityCollection;
                if (collection == null || collection.Entities.Count != 1) throw Guard.Error("The leave grant action accepts only individual server writes.");
                target = collection.Entities.Single();
            } else target = (Entity)c.InputParameters["Target"];
            var s = GrantContract.System(provider);
            if (c.PrimaryEntityName == BalancePersistence.PeriodTable) {
                if (message == "Update") {
                    if (!target.Contains(Guard.LockField) || !Guid.TryParse(target.GetAttributeValue<string>(Guard.LockField), out _) ||
                        target.Attributes.Keys.Any(k => k != Guard.LockField && k != BalancePersistence.PeriodTable + "id" && !GrantContract.SystemColumn(k)))
                        throw Guard.Error("Leave balance periods are immutable; only server locks may update.");
                    ValidatePeriod(s.Retrieve(BalancePersistence.PeriodTable, target.Id,
                        new ColumnSet("cr2c9_employee", "cr2c9_leavetype", "cr2c9_periodstart", "cr2c9_periodend")), employee.Id, grantType);
                } else ValidatePeriod(target, employee.Id, grantType);
                return;
            }
            if (c.PrimaryEntityName != BalancePersistence.EntryTable || message != "Create")
                throw Guard.Error("Balance ledger entries cannot be modified.");
            if (target.GetAttributeValue<OptionSetValue>("cr2c9_entrykind")?.Value != 290040000 ||
                !(target.GetAttributeValue<object>("cr2c9_hours") is int hours) || hours <= 0 || hours > 100000 ||
                target.Contains("cr2c9_leaverequest") || target.Contains("cr2c9_reverses"))
                throw Guard.Error("Only positive leave grant credits are supported by this action.");
            var period = target.GetAttributeValue<EntityReference>("cr2c9_balanceperiod");
            if (period == null || period.LogicalName != BalancePersistence.PeriodTable || period.Id == Guid.Empty)
                throw Guard.Error("A saved balance period is required.");
            var saved = s.Retrieve(period.LogicalName, period.Id,
                new ColumnSet("cr2c9_employee", "cr2c9_leavetype", "cr2c9_periodstart", "cr2c9_periodend", "ownerid"));
            ValidatePeriod(saved, employee.Id, grantType);
            var owner = saved.GetAttributeValue<EntityReference>("ownerid");
            var targetOwner = target.GetAttributeValue<EntityReference>("ownerid");
            if (owner == null || targetOwner == null || owner.Id != targetOwner.Id || owner.LogicalName != targetOwner.LogicalName)
                throw Guard.Error("Ledger ownership must match its period.");
            var operation = target.GetAttributeValue<string>("cr2c9_operationkey");
            if (operation == null || !(operation.StartsWith(period.Id.ToString("D") + grantKey, StringComparison.Ordinal) ||
                parent != null && operation.StartsWith(period.Id.ToString("D") + ":earned-grant:", StringComparison.Ordinal)) ||
                operation != target.GetAttributeValue<string>("cr2c9_name")) throw Guard.Error("Invalid leave grant operation key.");
        }
        static void CarryForwardLock(IServiceProvider provider, IPluginExecutionContext c,
            IPluginExecutionContext parent, string message) {
            if (c.Stage != 20 || c.Mode != 0 || !c.IsInTransaction ||
                c.PrimaryEntityName != BalancePersistence.PeriodTable || message != "Update")
                throw Guard.Error("Carry-forward confirmation may lock saved Annual Leave periods only.");
            var target = c.InputParameters.Contains("Target") ? c.InputParameters["Target"] as Entity : null;
            if (target == null || target.Id == Guid.Empty ||
                !Guid.TryParse(target.GetAttributeValue<string>(Guard.LockField), out _) ||
                target.Attributes.Keys.Any(k => k != Guard.LockField &&
                    k != BalancePersistence.PeriodTable + "id" && !GrantContract.SystemColumn(k)))
                throw Guard.Error("Carry-forward confirmation may update only a period lock.");
            var employee = GrantContract.EmployeeTarget(parent);
            var saved = GrantContract.System(provider).Retrieve(BalancePersistence.PeriodTable,target.Id,
                new ColumnSet("cr2c9_employee","cr2c9_leavetype","cr2c9_periodstart","cr2c9_periodend"));
            var today = EntitlementCalculator.MalaysiaDate(DateTime.UtcNow);
            var start = saved.GetAttributeValue<DateTime?>("cr2c9_periodstart");
            var end = saved.GetAttributeValue<DateTime?>("cr2c9_periodend");
            if (saved.GetAttributeValue<EntityReference>("cr2c9_employee")?.Id != employee.Id ||
                saved.GetAttributeValue<EntityReference>("cr2c9_leavetype")?.Id != GrantContract.AnnualType ||
                !start.HasValue || !end.HasValue ||
                !(start.Value == new DateTime(today.Year-1,1,1) && end.Value == new DateTime(today.Year-1,12,31) ||
                  start.Value == new DateTime(today.Year,1,1) && end.Value == new DateTime(today.Year,12,31)))
                throw Guard.Error("Carry-forward period lock is outside the closing and current year.");
        }
        static void ApprovalWrite(IServiceProvider provider, IPluginExecutionContext c, IPluginExecutionContext parent, string message) {
            if (c.Stage != 20 || c.Mode != 0 || !c.IsInTransaction || (message != "Create" && message != "Update"))
                throw Guard.Error("Only scoped server approval deductions and balance locks are allowed.");
            Entity target;
            if (c.InputParameters.Contains("Targets")) {
                var rows = c.InputParameters["Targets"] as EntityCollection;
                if (rows == null || rows.Entities.Count != 1) throw Guard.Error("Approval accounting writes individual rows only.");
                target = rows.Entities.Single();
            } else target = c.InputParameters["Target"] as Entity;
            if (target == null || target.LogicalName != c.PrimaryEntityName) throw Guard.Error("Invalid approval accounting target.");
            var s = GrantContract.System(provider);
            var request = ApprovalContract.ApprovedRequest(s,parent,true);
            var leaveType = ApprovalContract.Ref(request,"cr2c9_leavetypename","cr2c9_leavetype").Id;
            var emergency = leaveType != GrantContract.AnnualType &&
                EmergencyApprovalRules.IsEmergency(s,request,true);
            var trainingExternal = leaveType != GrantContract.AnnualType && !emergency &&
                TrainingPolicy.IsTraining(s,request) && TrainingPolicy.ChargesAnnual(request);
            var annual = leaveType == GrantContract.AnnualType || emergency || trainingExternal;
            var balanceType = annual ? GrantContract.AnnualType : leaveType;
            if (!annual && leaveType != SickContract.SickType && leaveType != HospitalContract.HospitalType)
                throw Guard.Error("Only Annual, charged Training, Emergency, Sick or Hospitalisation Leave may use balance approval accounting.");
            var employee = ApprovalContract.Ref(request,"cr2c9_employeename",Guard.Employee).Id;
            var dates = annual && !emergency && !trainingExternal ? ApprovalContract.ReadDates(s,request) : ApprovalContract.SickDates(s,request);
            if (target.LogicalName == BalancePersistence.PeriodTable && message == "Update") {
                if (!target.Contains(Guard.LockField) || !Guid.TryParse(target.GetAttributeValue<string>(Guard.LockField),out _) ||
                    target.Attributes.Keys.Any(k => k != Guard.LockField && k != BalancePersistence.PeriodTable + "id" && !GrantContract.SystemColumn(k)))
                    throw Guard.Error("Approval may lock an existing period only.");
                var scope = ApprovalContract.Period(s.Retrieve(target.LogicalName,target.Id,new ColumnSet("cr2c9_employee","cr2c9_leavetype","cr2c9_periodstart","cr2c9_periodend")),employee,balanceType);
                if (annual) RejectPostedClosingYear(s,scope);
                if (!dates.Any(d => d.Date >= scope.Start && d.Date <= scope.End))
                    throw Guard.Error("The balance period does not cover this request.");
                return;
            }
            if (target.LogicalName != BalancePersistence.EntryTable || message != "Create" ||
                target.GetAttributeValue<OptionSetValue>("cr2c9_entrykind")?.Value != 290040001 ||
                !(target.GetAttributeValue<object>("cr2c9_hours") is int hours) || hours >= 0 || hours < -100000 || target.Contains("cr2c9_reverses") ||
                ApprovalContract.Ref(target,"cr2c9_leaverequest",Guard.Request).Id != request.Id)
                throw Guard.Error("Only immutable negative deductions for this approved request are allowed.");
            var periodRef = ApprovalContract.Ref(target,"cr2c9_balanceperiod",BalancePersistence.PeriodTable);
            var period = s.Retrieve(periodRef.LogicalName,periodRef.Id,new ColumnSet("cr2c9_employee","cr2c9_leavetype","cr2c9_periodstart","cr2c9_periodend","ownerid"));
            var periodScope = ApprovalContract.Period(period,employee,balanceType);
            if (annual) RejectPostedClosingYear(s,periodScope);
            var amount = dates.Where(d => d.Date >= periodScope.Start && d.Date <= periodScope.End).Sum(d => d.Hours)
                - (annual ? CarryDebitHours(s,request.Id,periodScope) : 0);
            var owner = period.GetAttributeValue<EntityReference>("ownerid");
            var entryOwner = target.GetAttributeValue<EntityReference>("ownerid");
            var key = request.Id.ToString("D") + ":approved:" + period.Id.ToString("D");
            if (hours != -amount || amount <= 0 || owner == null || entryOwner == null || owner.Id != entryOwner.Id || owner.LogicalName != entryOwner.LogicalName ||
                target.GetAttributeValue<string>("cr2c9_operationkey") != key || target.GetAttributeValue<string>("cr2c9_name") != key)
                throw Guard.Error("Deduction amount, ownership and operation key must match the saved request dates and period.");
        }
        static void CancellationWrite(IServiceProvider provider, IPluginExecutionContext c, IPluginExecutionContext parent, string message) {
            if (c.Stage != 20 || c.Mode != 0 || !c.IsInTransaction || (message != "Create" && message != "Update"))
                throw Guard.Error("Only scoped server cancellation reversals and balance locks are allowed.");
            Entity target;
            if (c.InputParameters.Contains("Targets")) {
                var rows = c.InputParameters["Targets"] as EntityCollection;
                if (rows == null || rows.Entities.Count != 1) throw Guard.Error("Cancellation accounting writes individual rows only.");
                target = rows.Entities.Single();
            } else target = c.InputParameters["Target"] as Entity;
            if (target == null || target.LogicalName != c.PrimaryEntityName) throw Guard.Error("Invalid cancellation accounting target.");
            var s = GrantContract.System(provider);
            var cancellation = CancellationContract.Decided(s,parent,true);
            var request = CancellationContract.Request(s,cancellation);
            var employee = ApprovalContract.Ref(request,"cr2c9_employeename",Guard.Employee).Id;
            var leaveType = ApprovalContract.Ref(request,"cr2c9_leavetypename","cr2c9_leavetype").Id;
            var emergency = leaveType != GrantContract.AnnualType &&
                EmergencyApprovalRules.IsEmergency(s,request,true,false);
            var trainingExternal = leaveType != GrantContract.AnnualType && !emergency &&
                TrainingPolicy.IsTraining(s,request,false) && TrainingPolicy.ChargesAnnual(request);
            var annual = leaveType == GrantContract.AnnualType || emergency || trainingExternal;
            var balanceType = annual ? GrantContract.AnnualType : leaveType;
            if (!annual && leaveType != SickContract.SickType && leaveType != HospitalContract.HospitalType)
                throw Guard.Error("Only Annual, charged Training, Emergency, Sick or Hospitalisation Leave may use balance cancellation accounting.");
            var dates = annual && !emergency && !trainingExternal ? ApprovalContract.ReadDates(s,request) : ApprovalContract.SickDates(s,request);
            if (target.LogicalName == BalancePersistence.PeriodTable && message == "Update") {
                if (!target.Contains(Guard.LockField) || !Guid.TryParse(target.GetAttributeValue<string>(Guard.LockField),out _) ||
                    target.Attributes.Keys.Any(k => k != Guard.LockField && k != BalancePersistence.PeriodTable + "id" && !GrantContract.SystemColumn(k)))
                    throw Guard.Error("Cancellation may lock an existing balance period only.");
                var scope = ApprovalContract.Period(s.Retrieve(target.LogicalName,target.Id,
                    new ColumnSet("cr2c9_employee","cr2c9_leavetype","cr2c9_periodstart","cr2c9_periodend")),employee,balanceType);
                if (annual) RejectPostedClosingYear(s,scope);
                if (!dates.Any(d => d.Date >= scope.Start && d.Date <= scope.End))
                    throw Guard.Error("The locked balance period does not cover this cancellation.");
                return;
            }
            if (target.LogicalName != BalancePersistence.EntryTable || message != "Create" ||
                target.GetAttributeValue<OptionSetValue>("cr2c9_entrykind")?.Value != 290040002 ||
                !(target.GetAttributeValue<object>("cr2c9_hours") is int hours) || hours <= 0 || hours > 100000 ||
                ApprovalContract.Ref(target,"cr2c9_leaverequest",Guard.Request).Id != request.Id)
                throw Guard.Error("Only an exact positive reversal of this approved request is allowed.");
            var periodRef=ApprovalContract.Ref(target,"cr2c9_balanceperiod",BalancePersistence.PeriodTable);
            var period=s.Retrieve(periodRef.LogicalName,periodRef.Id,
                new ColumnSet("cr2c9_employee","cr2c9_leavetype","cr2c9_periodstart","cr2c9_periodend","ownerid"));
            var scopePeriod=ApprovalContract.Period(period,employee,balanceType);
            if (annual) RejectPostedClosingYear(s,scopePeriod);
            var amount=dates.Where(d=>d.Date>=scopePeriod.Start && d.Date<=scopePeriod.End).Sum(d=>d.Hours)
                - (annual ? CarryDebitHours(s,request.Id,scopePeriod) : 0);
            var original=ApprovalContract.Ref(target,"cr2c9_reverses",BalancePersistence.EntryTable);
            var debit=s.Retrieve(BalancePersistence.EntryTable,original.Id,
                new ColumnSet("cr2c9_entrykind","cr2c9_hours","cr2c9_balanceperiod","cr2c9_leaverequest","cr2c9_operationkey"));
            var debitKey=request.Id.ToString("D")+":approved:"+period.Id.ToString("D");
            var reversalKey=cancellation.Id.ToString("D")+":reversed:"+period.Id.ToString("D");
            var owner=period.GetAttributeValue<EntityReference>("ownerid");
            var entryOwner=target.GetAttributeValue<EntityReference>("ownerid");
            if (amount<=0 || hours!=amount || debit.GetAttributeValue<OptionSetValue>("cr2c9_entrykind")?.Value!=290040001 ||
                debit.GetAttributeValue<int>("cr2c9_hours")!=-amount ||
                ApprovalContract.Ref(debit,"cr2c9_balanceperiod",BalancePersistence.PeriodTable).Id!=period.Id ||
                ApprovalContract.Ref(debit,"cr2c9_leaverequest",Guard.Request).Id!=request.Id ||
                debit.GetAttributeValue<string>("cr2c9_operationkey")!=debitKey ||
                owner==null || entryOwner==null || owner.Id!=entryOwner.Id || owner.LogicalName!=entryOwner.LogicalName ||
                target.GetAttributeValue<string>("cr2c9_operationkey")!=reversalKey ||
                target.GetAttributeValue<string>("cr2c9_name")!=reversalKey)
                throw Guard.Error("Reversal amount, original debit, ownership and operation key must match the saved cancellation.");
        }
        static void ValidatePeriod(Entity row, Guid employee, Guid leaveType) {
            var e = row.GetAttributeValue<EntityReference>("cr2c9_employee");
            var t = row.GetAttributeValue<EntityReference>("cr2c9_leavetype");
            var year = EntitlementCalculator.MalaysiaDate(DateTime.UtcNow).Year;
            if (e == null || e.LogicalName != Guard.Employee || e.Id != employee || t == null ||
                t.LogicalName != "cr2c9_leavetype" || t.Id != leaveType ||
                row.GetAttributeValue<DateTime?>("cr2c9_periodstart") != new DateTime(year,1,1) ||
                row.GetAttributeValue<DateTime?>("cr2c9_periodend") != new DateTime(year,12,31))
                throw Guard.Error("Leave grant scope must match the action's employee, category and current calendar year.");
        }

        static void RejectPostedClosingYear(IOrganizationService service, BalancePeriod period) {
            if (period.Start.Year >= EntitlementCalculator.MalaysiaDate(DateTime.UtcNow).Year) return;
            var q = new QueryExpression("cr2c9_carryforwardreview") {
                ColumnSet = new ColumnSet("cr2c9_reviewstatus"), TopCount = 2
            };
            q.Criteria.AddCondition("cr2c9_sourceperiod",ConditionOperator.Equal,period.Id);
            var reviews = service.RetrieveMultiple(q).Entities;
            if (reviews.Count > 1 || reviews.Any(r =>
                r.GetAttributeValue<OptionSetValue>("cr2c9_reviewstatus")?.Value == 290040002))
                throw Guard.Error("The closing-year Annual Leave balance is locked by HR carry-forward confirmation.");
            var credits = new QueryExpression("cr2c9_carryforwardcredit") {
                ColumnSet = new ColumnSet(false), TopCount = 1
            };
            credits.Criteria.AddCondition("cr2c9_sourceperiod",ConditionOperator.Equal,period.Id);
            if (service.RetrieveMultiple(credits).Entities.Count != 0)
                throw Guard.Error("The closing-year Annual Leave balance is locked by a posted carry-forward credit.");
        }

        static int CarryDebitHours(IOrganizationService s, Guid requestId, BalancePeriod period) {
            var credits=new QueryExpression("cr2c9_carryforwardcredit") {
                ColumnSet=new ColumnSet("cr2c9_hours","cr2c9_useby"),TopCount=2
            };
            credits.Criteria.AddCondition("cr2c9_targetperiod",ConditionOperator.Equal,period.Id);
            var found=s.RetrieveMultiple(credits).Entities;
            if(found.Count>1)throw Guard.Error("Duplicate carry-forward credits prevent annual accounting.");
            if(found.Count==0)return 0;
            var credit=found.Single();
            var useBy=credit.GetAttributeValue<DateTime?>("cr2c9_useby");
            if(!useBy.HasValue || useBy.Value!=new DateTime(period.Start.Year,3,31))
                throw Guard.Error("The carried credit has an invalid use-by date.");
            var q=new QueryExpression("cr2c9_carryforwardusage") {
                ColumnSet=new ColumnSet("cr2c9_hours","cr2c9_entrykind","cr2c9_operationkey"),TopCount=2
            };
            q.Criteria.AddCondition("cr2c9_credit",ConditionOperator.Equal,credit.Id);
            q.Criteria.AddCondition("cr2c9_leaverequest",ConditionOperator.Equal,requestId);
            q.Criteria.AddCondition("cr2c9_entrykind",ConditionOperator.Equal,290040000);
            var debits=s.RetrieveMultiple(q).Entities;
            if(debits.Count>1)throw Guard.Error("Duplicate carried debits prevent annual accounting.");
            if(debits.Count==0)return 0;
            var debit=debits.Single();
            var hours=debit.GetAttributeValue<int>("cr2c9_hours");
            if(hours<=0 || hours%4!=0 || hours>credit.GetAttributeValue<int>("cr2c9_hours") ||
                debit.GetAttributeValue<OptionSetValue>("cr2c9_entrykind")?.Value!=290040000 ||
                debit.GetAttributeValue<string>("cr2c9_operationkey")!=
                    requestId.ToString("D")+":carry-approved:"+credit.Id.ToString("D"))
                throw Guard.Error("The carried debit is invalid.");
            var request=s.Retrieve(Guard.Request,requestId,new ColumnSet("cr2c9_startdate","cr2c9_enddate"));
            var eligible=ApprovalContract.ReadDates(s,request)
                .Where(d=>d.Date>=period.Start && d.Date<=useBy.Value).Sum(d=>d.Hours);
            if(hours>eligible)throw Guard.Error("Carried leave can be used only for dates through 31 March.");
            return hours;
        }
    }
}
