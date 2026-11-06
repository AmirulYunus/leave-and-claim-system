using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

namespace Leave.Plugins
{
    // Internal transaction adapter, deliberately not an IPlugin or public API.
    // Before invocation, the registered caller must authorize the operation,
    // validate annual category/calendar/policy, and lock the employee identity
    // row. Every period creator/writer must follow that same lock order, with
    // immutable ledger guards installed. These integration gates are not active.
    // No ExecuteTransaction/ExecuteMultiple: nested SDK writes participate in the
    // synchronous caller's existing Dataverse transaction and errors propagate.
    internal sealed class BalancePersistence
    {
        internal const string PeriodTable = "cr2c9_leavebalanceperiod";
        internal const string EntryTable = "cr2c9_leavebalanceentry";
        readonly IOrganizationService service;
        internal BalancePersistence(IOrganizationService service, IPluginExecutionContext context)
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            if (context == null || !context.IsInTransaction || context.Mode != 0 ||
                (context.Stage != 20 && context.Stage != 30 && context.Stage != 40))
                throw Guard.Error("Balance writes require a synchronous server transaction.");
            this.service = service;
        }

        sealed class StoredPeriod
        {
            internal BalancePeriod Scope;
            internal Entity Row;
            internal EntityReference Owner;
        }
        static EntityReference Reference(Entity row, string field, string table = null)
        {
            var value = row.GetAttributeValue<EntityReference>(field);
            if (value == null || value.Id == Guid.Empty || (table != null && value.LogicalName != table))
                throw Guard.Error("Balance data has an invalid " + field + " reference.");
            return value;
        }
        StoredPeriod ReadPeriod(BalancePeriod expected)
        {
            if (expected == null) throw new ArgumentNullException(nameof(expected));
            var row = service.Retrieve(PeriodTable, expected.Id, new ColumnSet(
                "cr2c9_employee", "cr2c9_leavetype", "cr2c9_periodstart", "cr2c9_periodend", "ownerid"));
            if (row.LogicalName != PeriodTable || row.Id != expected.Id ||
                !(row.GetAttributeValue<object>("cr2c9_periodstart") is DateTime start) ||
                !(row.GetAttributeValue<object>("cr2c9_periodend") is DateTime end))
                throw Guard.Error("Balance period dates and identity are required.");
            var scope = new BalancePeriod(row.Id, Reference(row,"cr2c9_employee",Guard.Employee).Id,
                Reference(row,"cr2c9_leavetype","cr2c9_leavetype").Id, start, end);
            if (scope.EmployeeId != expected.EmployeeId || scope.LeaveTypeId != expected.LeaveTypeId ||
                scope.Start != expected.Start || scope.End != expected.End)
                throw Guard.Error("The balance period changed or does not match the requested scope.");
            var owner = Reference(row,"ownerid");
            if (owner.LogicalName != "systemuser" && owner.LogicalName != "team")
                throw Guard.Error("Balance ownership must be a user or team.");
            return new StoredPeriod { Scope = scope, Row = row, Owner = owner };
        }
        void Lock(StoredPeriod period)
        {
            if (string.IsNullOrWhiteSpace(period.Row.RowVersion))
                throw Guard.Error("A balance row version is required; refresh before retrying.");
            var update = new Entity(PeriodTable, period.Scope.Id) { RowVersion = period.Row.RowVersion };
            update[Guard.LockField] = Guid.NewGuid().ToString("D");
            service.Execute(new UpdateRequest { Target = update, ConcurrencyBehavior = ConcurrencyBehavior.IfRowVersionMatches });
            // Never retry a fault within the transaction or continue with stale history.
        }
        BalanceEntry[] History(StoredPeriod period)
        {
            var query = new QueryExpression(EntryTable) {
                ColumnSet = new ColumnSet("cr2c9_balanceperiod", "cr2c9_operationkey", "cr2c9_entrykind",
                    "cr2c9_hours", "cr2c9_leaverequest", "cr2c9_reverses"),
                PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 }
            };
            query.Criteria.AddCondition("cr2c9_balanceperiod", ConditionOperator.Equal, period.Scope.Id);
            query.AddOrder(EntryTable + "id", OrderType.Ascending);
            var rows = new List<Entity>();
            while (true) {
                var page = service.RetrieveMultiple(query);
                rows.AddRange(page.Entities);
                if (rows.Count > 20000) throw Guard.Error("Balance history exceeds the synchronous processing limit; review required.");
                if (!page.MoreRecords) break;
                if (string.IsNullOrEmpty(page.PagingCookie) || page.Entities.Count == 0 || query.PageInfo.PageNumber >= 4)
                    throw Guard.Error("Complete balance history could not be read; no balance changes are allowed.");
                query.PageInfo.PagingCookie = page.PagingCookie;
                query.PageInfo.PageNumber++;
            }
            if (rows.Any(r => r.LogicalName != EntryTable || r.Id == Guid.Empty) ||
                rows.GroupBy(r => r.Id).Any(g => g.Count() != 1))
                throw Guard.Error("Balance history contains invalid or duplicate identities.");
            var byId = rows.ToDictionary(r => r.Id);
            var entries = new List<BalanceEntry>();
            foreach (var row in rows) {
                if (Reference(row,"cr2c9_balanceperiod",PeriodTable).Id != period.Scope.Id ||
                    !(row.GetAttributeValue<object>("cr2c9_hours") is int hours))
                    throw Guard.Error("Balance history has missing hours or a foreign period.");
                var kind = row.GetAttributeValue<OptionSetValue>("cr2c9_entrykind");
                if (kind == null || kind.Value < 290040000 || kind.Value > 290040002)
                    throw Guard.Error("Balance history has an unsupported entry kind.");
                var request = row.Contains("cr2c9_leaverequest") && row["cr2c9_leaverequest"] != null
                    ? Reference(row,"cr2c9_leaverequest",Guard.Request).Id : Guid.Empty;
                string reverses = null;
                if (row.Contains("cr2c9_reverses") && row["cr2c9_reverses"] != null) {
                    var id = Reference(row,"cr2c9_reverses",EntryTable).Id;
                    if (!byId.TryGetValue(id, out var original)) throw Guard.Error("A reversal's original entry is missing from this period.");
                    reverses = original.GetAttributeValue<string>("cr2c9_operationkey");
                }
                entries.Add(new BalanceEntry(period.Scope.Id, row.GetAttributeValue<string>("cr2c9_operationkey"),
                    (BalanceEntryKind)(kind.Value - 290040000), hours, request, reverses));
            }
            return entries.ToArray();
        }
        // A review preview never writes. Compare the period version around the
        // ledger read because all authorized balance writers lock that row first.
        // Confirmation will repeat this read under its own transaction lock.
        internal BalanceEntry[] ReadVerifiedHistory(BalancePeriod expected)
        {
            var before = ReadPeriod(expected);
            if (string.IsNullOrWhiteSpace(before.Row.RowVersion))
                throw Guard.Error("A saved balance row version is required for year-end review.");
            var entries = History(before);
            var after = ReadPeriod(expected);
            if (before.Row.RowVersion != after.Row.RowVersion)
                throw Guard.Error("The annual balance changed while the HR preview was prepared; refresh and retry.");
            BalanceLedger.AvailableHours(before.Scope, entries);
            return entries;
        }
        // Year-end confirmation takes the same period lock used by grants and
        // decisions before reading the complete ledger. The caller locks the
        // Employee first, then source and target periods in calendar order.
        internal BalanceEntry[] LockAndReadHistory(BalancePeriod expected)
        {
            var period = ReadPeriod(expected);
            Lock(period);
            var entries = History(period);
            BalanceLedger.AvailableHours(period.Scope, entries);
            return entries;
        }
        void Write(StoredPeriod period, BalanceEntry entry)
        {
            if (entry.PeriodId != period.Scope.Id || entry.Kind == BalanceEntryKind.Reversal ||
                entry.Hours < -100000 || entry.Hours > 100000 || entry.OperationKey.Length > 120)
                throw Guard.Error("Unsupported balance entry for this operation.");
            var row = new Entity(EntryTable);
            row["cr2c9_name"] = entry.OperationKey;
            row["cr2c9_operationkey"] = entry.OperationKey;
            row["cr2c9_balanceperiod"] = new EntityReference(PeriodTable,period.Scope.Id);
            row["cr2c9_entrykind"] = new OptionSetValue(290040000 + (int)entry.Kind);
            row["cr2c9_hours"] = entry.Hours;
            row["ownerid"] = period.Owner; // Do not make an approver own the employee's ledger.
            if (entry.RequestId != Guid.Empty) row["cr2c9_leaverequest"] = new EntityReference(Guard.Request,entry.RequestId);
            service.Create(row);
        }
        void WriteReversal(StoredPeriod period, BalanceEntry entry)
        {
            if (entry.PeriodId != period.Scope.Id || entry.Kind != BalanceEntryKind.Reversal ||
                entry.Hours <= 0 || entry.Hours > 100000 || entry.OperationKey.Length > 120)
                throw Guard.Error("Invalid annual cancellation reversal.");
            var q = new QueryExpression(EntryTable) { ColumnSet = new ColumnSet("cr2c9_balanceperiod", "cr2c9_leaverequest",
                "cr2c9_entrykind", "cr2c9_hours", "cr2c9_operationkey"), TopCount = 2 };
            q.Criteria.AddCondition("cr2c9_operationkey", ConditionOperator.Equal, entry.Reverses);
            var originals = service.RetrieveMultiple(q).Entities;
            if (originals.Count != 1 || Reference(originals[0],"cr2c9_balanceperiod",PeriodTable).Id != period.Scope.Id ||
                Reference(originals[0],"cr2c9_leaverequest",Guard.Request).Id != entry.RequestId ||
                originals[0].GetAttributeValue<OptionSetValue>("cr2c9_entrykind")?.Value != 290040001 ||
                originals[0].GetAttributeValue<int>("cr2c9_hours") != -entry.Hours)
                throw Guard.Error("The exact approved debit to reverse is missing or changed.");
            var row = new Entity(EntryTable);
            row["cr2c9_name"] = entry.OperationKey;
            row["cr2c9_operationkey"] = entry.OperationKey;
            row["cr2c9_balanceperiod"] = new EntityReference(PeriodTable,period.Scope.Id);
            row["cr2c9_entrykind"] = new OptionSetValue(290040002);
            row["cr2c9_hours"] = entry.Hours;
            row["ownerid"] = period.Owner;
            row["cr2c9_leaverequest"] = new EntityReference(Guard.Request,entry.RequestId);
            row["cr2c9_reverses"] = originals[0].ToEntityReference();
            service.Create(row);
        }
        internal BalanceGrantPlan ApplyGrant(BalancePeriod period, EntitlementRate approvedFullPeriodRate,
            string category = "annual")
        {
            var stored = ReadPeriod(period);
            Lock(stored);
            var plan = BalanceLedger.PlanGrant(stored.Scope, History(stored), approvedFullPeriodRate, category);
            if (plan.Entry != null) Write(stored,plan.Entry);
            return plan;
        }
        internal BalanceGrantPlan ApplyEarnedGrant(BalancePeriod period, int targetEarnedHours)
        {
            var stored = ReadPeriod(period);
            Lock(stored);
            var plan = BalanceLedger.PlanEarnedGrant(stored.Scope, History(stored), targetEarnedHours);
            if (plan.Entry != null) Write(stored,plan.Entry);
            return plan;
        }
        internal BalanceEntry[] ApplyApprovedAnnualLeave(Guid employee, Guid type, Guid request,
            IEnumerable<AnnualLeaveDate> validatedDates, IEnumerable<BalancePeriod> verifiedPeriods,
            IReadOnlyDictionary<Guid, int> earnedCaps = null)
        {
            if (employee == Guid.Empty || type == Guid.Empty || request == Guid.Empty ||
                validatedDates == null || verifiedPeriods == null) throw new ArgumentException("Approved leave scope is required.");
            var dates = validatedDates.ToArray();
            var periods = verifiedPeriods.ToArray();
            if (periods.Any(p => p == null || p.EmployeeId != employee || p.LeaveTypeId != type) ||
                periods.GroupBy(p => p.Id).Any(g => g.Count() != 1)) throw Guard.Error("Approval periods do not match the employee and leave type.");
            var stored = periods.OrderBy(p => p.Id).Select(ReadPeriod).ToArray();
            // A stable lock order prevents two cross-period operations acquiring
            // the same balance rows in opposite orders. Caller locks employee first.
            foreach (var p in stored) Lock(p);
            var entries = stored.SelectMany(History).ToArray();
            var plan = earnedCaps == null
                ? BalanceLedger.PlanApprovedAnnualLeave(employee,type,request,dates,
                    stored.Select(p => p.Scope),entries)
                : BalanceLedger.PlanApprovedAnnualLeaveCapped(employee,type,request,dates,
                    stored.Select(p => p.Scope),entries,earnedCaps);
            // Validate every period before the first write. A later SDK fault is
            // propagated, rolling back all writes in the caller's transaction.
            foreach (var entry in plan) Write(stored.Single(p => p.Scope.Id == entry.PeriodId),entry);
            return plan;
        }
        internal BalanceEntry[] ApplyApprovedAnnualCancellation(Guid employee, Guid type, Guid request, Guid cancellation,
            IEnumerable<AnnualLeaveDate> validatedDates, IEnumerable<BalancePeriod> verifiedPeriods)
        {
            if (employee == Guid.Empty || type == Guid.Empty || request == Guid.Empty || cancellation == Guid.Empty ||
                validatedDates == null || verifiedPeriods == null) throw new ArgumentException("Annual cancellation scope is required.");
            var dates = validatedDates.ToArray();
            var periods = verifiedPeriods.ToArray();
            if (periods.Any(p => p == null || p.EmployeeId != employee || p.LeaveTypeId != type) ||
                periods.GroupBy(p => p.Id).Any(g => g.Count() != 1)) throw Guard.Error("Cancellation periods do not match the saved request.");
            var stored = periods.OrderBy(p => p.Id).Select(ReadPeriod).ToArray();
            foreach (var p in stored) Lock(p);
            var entries = stored.SelectMany(History).ToArray();
            var plan = BalanceLedger.PlanExactAnnualReversal(employee,type,request,cancellation,dates,
                stored.Select(p => p.Scope),entries);
            foreach (var entry in plan) WriteReversal(stored.Single(p => p.Scope.Id == entry.PeriodId),entry);
            return plan;
        }

        // The live approval and cancellation guards use the carry-forward
        // methods below for Annual Leave decisions.
        sealed class StoredCarryCredit
        {
            internal Entity Row;
            internal CarryForwardCredit Credit;
            internal EntityReference Owner;
            internal DateTime UseBy;
        }

        StoredCarryCredit ReadCarryCredit(BalancePeriod period)
        {
            var query = new QueryExpression("cr2c9_carryforwardcredit") {
                ColumnSet = new ColumnSet("cr2c9_employee", "cr2c9_sourceperiod", "cr2c9_targetperiod",
                    "cr2c9_review", "cr2c9_hours", "cr2c9_operationkey", "cr2c9_useby", "ownerid"),
                TopCount = 2
            };
            query.Criteria.AddCondition("cr2c9_targetperiod", ConditionOperator.Equal, period.Id);
            var found = service.RetrieveMultiple(query).Entities;
            if (found.Count > 1) throw Guard.Error("Duplicate carry-forward credits exist for this target year.");
            if (found.Count == 0) return null;
            var row = found.Single();
            if (row.Id == Guid.Empty || Reference(row,"cr2c9_employee",Guard.Employee).Id != period.EmployeeId ||
                Reference(row,"cr2c9_targetperiod",PeriodTable).Id != period.Id)
                throw Guard.Error("The carry-forward credit belongs to a different employee or year.");
            var source = service.Retrieve(PeriodTable, Reference(row,"cr2c9_sourceperiod",PeriodTable).Id,
                new ColumnSet("cr2c9_employee", "cr2c9_leavetype", "cr2c9_periodstart", "cr2c9_periodend"));
            if (Reference(source,"cr2c9_employee",Guard.Employee).Id != period.EmployeeId ||
                Reference(source,"cr2c9_leavetype","cr2c9_leavetype").Id != period.LeaveTypeId ||
                source.GetAttributeValue<DateTime?>("cr2c9_periodstart") != period.Start.AddYears(-1) ||
                source.GetAttributeValue<DateTime?>("cr2c9_periodend") != period.Start.AddDays(-1))
                throw Guard.Error("The carry-forward source is not the preceding calendar year.");
            var review = service.Retrieve("cr2c9_carryforwardreview",
                Reference(row,"cr2c9_review","cr2c9_carryforwardreview").Id,
                new ColumnSet("cr2c9_reviewstatus", "cr2c9_sourceperiod", "cr2c9_targetperiod",
                    "cr2c9_reviewedcarryhours", "cr2c9_operationkey"));
            var hours = row.GetAttributeValue<int>("cr2c9_hours");
            var key = row.GetAttributeValue<string>("cr2c9_operationkey");
            var useBy = row.GetAttributeValue<DateTime?>("cr2c9_useby");
            if (review.GetAttributeValue<OptionSetValue>("cr2c9_reviewstatus")?.Value != 290040002 ||
                Reference(review,"cr2c9_sourceperiod",PeriodTable).Id != source.Id ||
                Reference(review,"cr2c9_targetperiod",PeriodTable).Id != period.Id ||
                review.GetAttributeValue<int>("cr2c9_reviewedcarryhours") != hours ||
                review.GetAttributeValue<string>("cr2c9_operationkey") != key ||
                !useBy.HasValue || useBy.Value != new DateTime(period.Start.Year,3,31))
                throw Guard.Error("The carry-forward credit is not a matching confirmed HR review.");
            var owner = Reference(row,"ownerid");
            if (owner.LogicalName != "systemuser" && owner.LogicalName != "team")
                throw Guard.Error("The carry-forward credit has invalid ownership.");
            return new StoredCarryCredit {
                Row = row, Credit = new CarryForwardCredit(source.Id, period.Id, key, hours),
                Owner = owner, UseBy = useBy.Value
            };
        }

        // January–March approvals must not consume new-year earnings while
        // unused prior-year leave is still awaiting HR's carry-forward decision.
        // Once charged to the wrong bucket, the confirmation action deliberately
        // refuses a late credit because it cannot rewrite an approved request.
        void RequireCarryForwardDecision(BalancePeriod period,
            IEnumerable<AnnualLeaveDate> dates, StoredCarryCredit credit)
        {
            if (credit != null || !dates.Any(d => d.Date <= new DateTime(period.Start.Year, 3, 31)))
                return;
            var priorStart = period.Start.AddYears(-1);
            var q = new QueryExpression(PeriodTable) {
                ColumnSet = new ColumnSet("cr2c9_employee", "cr2c9_leavetype",
                    "cr2c9_periodstart", "cr2c9_periodend"), TopCount = 2
            };
            q.Criteria.AddCondition("cr2c9_employee", ConditionOperator.Equal, period.EmployeeId);
            q.Criteria.AddCondition("cr2c9_leavetype", ConditionOperator.Equal, period.LeaveTypeId);
            q.Criteria.AddCondition("cr2c9_periodstart", ConditionOperator.Equal, priorStart);
            q.Criteria.AddCondition("cr2c9_periodend", ConditionOperator.Equal, period.Start.AddDays(-1));
            var priorRows = service.RetrieveMultiple(q).Entities;
            if (priorRows.Count > 1)
                throw Guard.Error("Duplicate prior-year Annual Leave balances require HR review.");
            if (priorRows.Count == 0) return;
            var prior = new BalancePeriod(priorRows[0].Id, period.EmployeeId,
                period.LeaveTypeId, priorStart, period.Start.AddDays(-1));
            var history = ReadVerifiedHistory(prior);
            if (BalanceLedger.AvailableHours(prior, history) > 0)
                throw Guard.Error("HR must confirm the prior year's unused Annual Leave carry-forward before approving January–March leave.");
        }

        void LockCarryCredit(StoredCarryCredit credit)
        {
            if (string.IsNullOrWhiteSpace(credit.Row.RowVersion))
                throw Guard.Error("A carry-forward credit row version is required; refresh before retrying.");
            var update = new Entity("cr2c9_carryforwardcredit", credit.Row.Id) {
                RowVersion = credit.Row.RowVersion
            };
            update["cr2c9_locktoken"] = Guid.NewGuid().ToString("D");
            service.Execute(new UpdateRequest { Target = update,
                ConcurrencyBehavior = ConcurrencyBehavior.IfRowVersionMatches });
        }

        CarryForwardUsageEntry[] CarryHistory(StoredCarryCredit credit)
        {
            if (credit == null) return Array.Empty<CarryForwardUsageEntry>();
            var q = new QueryExpression("cr2c9_carryforwardusage") {
                ColumnSet = new ColumnSet("cr2c9_credit", "cr2c9_leaverequest", "cr2c9_cancellation",
                    "cr2c9_reverses", "cr2c9_entrykind", "cr2c9_hours", "cr2c9_operationkey"),
                PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 }
            };
            q.Criteria.AddCondition("cr2c9_credit", ConditionOperator.Equal, credit.Row.Id);
            q.AddOrder("cr2c9_carryforwardusageid", OrderType.Ascending);
            var rows = new List<Entity>();
            while (true) {
                var page = service.RetrieveMultiple(q);
                rows.AddRange(page.Entities);
                if (rows.Count > 20000) throw Guard.Error("Carry-forward usage exceeds the synchronous processing limit.");
                if (!page.MoreRecords) break;
                if (string.IsNullOrEmpty(page.PagingCookie) || page.Entities.Count == 0 || q.PageInfo.PageNumber >= 4)
                    throw Guard.Error("Complete carry-forward usage could not be read.");
                q.PageInfo.PagingCookie = page.PagingCookie;
                q.PageInfo.PageNumber++;
            }
            if (rows.Any(r => r.Id == Guid.Empty) || rows.GroupBy(r => r.Id).Any(g => g.Count() != 1))
                throw Guard.Error("Carry-forward usage has invalid or duplicate identities.");
            var byId = rows.ToDictionary(r => r.Id);
            var entries = rows.Select(row => {
                if (Reference(row,"cr2c9_credit","cr2c9_carryforwardcredit").Id != credit.Row.Id ||
                    !(row.GetAttributeValue<object>("cr2c9_hours") is int hours))
                    throw Guard.Error("Carry-forward usage has a foreign credit or missing hours.");
                var kind = row.GetAttributeValue<OptionSetValue>("cr2c9_entrykind")?.Value;
                if (kind != 290040000 && kind != 290040001)
                    throw Guard.Error("Carry-forward usage has an unknown kind.");
                var request = Reference(row,"cr2c9_leaverequest",Guard.Request).Id;
                string reverses = null;
                if (kind == 290040001) {
                    Reference(row,"cr2c9_cancellation","cr2c9_leavecancellation");
                    var original = Reference(row,"cr2c9_reverses","cr2c9_carryforwardusage");
                    if (!byId.TryGetValue(original.Id, out var debit))
                        throw Guard.Error("A carry-forward reversal's debit is missing.");
                    reverses = debit.GetAttributeValue<string>("cr2c9_operationkey");
                } else if (row.Contains("cr2c9_cancellation") || row.Contains("cr2c9_reverses"))
                    throw Guard.Error("A carry-forward debit cannot reference a cancellation.");
                return new CarryForwardUsageEntry(credit.Row.Id, request,
                    row.GetAttributeValue<string>("cr2c9_operationkey"),
                    (CarryForwardUsageKind)(kind.Value - 290040000), hours, reverses);
            }).ToArray();
            CarryForwardUsageLedger.AvailableHours(credit.Row.Id, credit.Credit.Hours, entries);
            return entries;
        }

        void WriteCarry(StoredCarryCredit credit, CarryForwardUsageEntry entry,
            Guid cancellationId = default(Guid))
        {
            if (credit == null || entry == null || entry.CreditId != credit.Row.Id ||
                entry.OperationKey.Length > 120 ||
                (entry.Kind == CarryForwardUsageKind.CancellationReversal) != (cancellationId != Guid.Empty))
                throw Guard.Error("Invalid carry-forward write plan.");
            var row = new Entity("cr2c9_carryforwardusage") {
                ["cr2c9_name"] = entry.OperationKey,
                ["cr2c9_operationkey"] = entry.OperationKey,
                ["cr2c9_credit"] = credit.Row.ToEntityReference(),
                ["cr2c9_leaverequest"] = new EntityReference(Guard.Request,entry.RequestId),
                ["cr2c9_entrykind"] = new OptionSetValue(290040000 + (int)entry.Kind),
                ["cr2c9_hours"] = entry.Hours,
                ["ownerid"] = credit.Owner
            };
            if (cancellationId != Guid.Empty) {
                var q = new QueryExpression("cr2c9_carryforwardusage") {
                    ColumnSet = new ColumnSet("cr2c9_credit", "cr2c9_leaverequest",
                        "cr2c9_entrykind", "cr2c9_hours"), TopCount = 2
                };
                q.Criteria.AddCondition("cr2c9_operationkey", ConditionOperator.Equal, entry.Reverses);
                var originals = service.RetrieveMultiple(q).Entities;
                if (originals.Count != 1 ||
                    Reference(originals[0],"cr2c9_credit","cr2c9_carryforwardcredit").Id != credit.Row.Id ||
                    Reference(originals[0],"cr2c9_leaverequest",Guard.Request).Id != entry.RequestId ||
                    originals[0].GetAttributeValue<OptionSetValue>("cr2c9_entrykind")?.Value != 290040000 ||
                    originals[0].GetAttributeValue<int>("cr2c9_hours") != entry.Hours)
                    throw Guard.Error("The exact original carried debit is missing.");
                row["cr2c9_cancellation"] = new EntityReference("cr2c9_leavecancellation",cancellationId);
                row["cr2c9_reverses"] = originals[0].ToEntityReference();
            }
            service.Create(row);
        }

        internal CarryForwardPeriodPlan[] ApplyApprovedAnnualLeaveWithCarry(Guid employee, Guid type,
            Guid request, IEnumerable<AnnualLeaveDate> validatedDates,
            IEnumerable<BalancePeriod> verifiedPeriods, IReadOnlyDictionary<Guid,int> earnedCaps)
        {
            if (employee == Guid.Empty || type == Guid.Empty || request == Guid.Empty ||
                validatedDates == null || verifiedPeriods == null || earnedCaps == null)
                throw new ArgumentException("Approved Annual Leave needs complete server-verified scope.");
            var dates = validatedDates.ToArray();
            var periods = verifiedPeriods.ToArray();
            if (dates.Length == 0 || dates.Any(d => d == null) ||
                dates.GroupBy(d => d.Date).Any(g => g.Count() != 1) ||
                periods.Length == 0 || periods.Any(p => p == null || p.EmployeeId != employee ||
                    p.LeaveTypeId != type || !earnedCaps.ContainsKey(p.Id)) ||
                periods.GroupBy(p => p.Id).Any(g => g.Count() != 1) ||
                dates.Any(d => periods.Count(p => p.Start <= d.Date && d.Date <= p.End) != 1) ||
                periods.Any(p => !dates.Any(d => p.Start <= d.Date && d.Date <= p.End)))
                throw Guard.Error("Approved dates and balance periods must match exactly.");
            var stored = periods.OrderBy(p => p.Id).Select(ReadPeriod).ToArray();
            var credits = stored.Select(p => ReadCarryCredit(p.Scope)).ToArray();
            for (var i = 0; i < stored.Length; i++)
                RequireCarryForwardDecision(stored[i].Scope,
                    dates.Where(d => stored[i].Scope.Start <= d.Date && d.Date <= stored[i].Scope.End),
                    credits[i]);
            // Employee was already locked by the parent decision guard. Both
            // approval and cancellation lock all periods, then all credits,
            // in the same stable order before reading usage or writing entries.
            foreach (var p in stored) Lock(p);
            foreach (var credit in credits.Where(c => c != null).OrderBy(c => c.Row.Id))
                LockCarryCredit(credit);
            var plans = new CarryForwardPeriodPlan[stored.Length];
            for (var i = 0; i < stored.Length; i++) {
                var p = stored[i]; var credit = credits[i];
                plans[i] = CarryForwardPeriodPlan.Approval(p.Scope, History(p), earnedCaps[p.Scope.Id],
                    credit?.Row.Id ?? Guid.Empty, credit?.Credit, credit?.UseBy ?? default(DateTime),
                    CarryHistory(credit), request,
                    dates.Where(d => p.Scope.Start <= d.Date && d.Date <= p.Scope.End));
            }
            // All periods were checked before the first write. Any SDK fault
            // aborts the parent decision transaction, including history.
            for (var i = 0; i < stored.Length; i++) {
                if (plans[i].CarryEntry != null) WriteCarry(credits[i], plans[i].CarryEntry);
                if (plans[i].AnnualEntry != null) Write(stored[i], plans[i].AnnualEntry);
            }
            return plans;
        }

        internal CarryForwardPeriodPlan[] ApplyApprovedAnnualCancellationWithCarry(Guid employee,
            Guid type, Guid request, Guid cancellation,
            IEnumerable<AnnualLeaveDate> validatedDates, IEnumerable<BalancePeriod> verifiedPeriods)
        {
            if (employee == Guid.Empty || type == Guid.Empty || request == Guid.Empty ||
                cancellation == Guid.Empty || validatedDates == null || verifiedPeriods == null)
                throw new ArgumentException("Approved cancellation needs complete server-verified scope.");
            var dates = validatedDates.ToArray();
            var periods = verifiedPeriods.ToArray();
            if (dates.Length == 0 || dates.Any(d => d == null) ||
                dates.GroupBy(d => d.Date).Any(g => g.Count() != 1) ||
                periods.Length == 0 || periods.Any(p => p == null || p.EmployeeId != employee ||
                    p.LeaveTypeId != type) ||
                periods.GroupBy(p => p.Id).Any(g => g.Count() != 1) ||
                dates.Any(d => periods.Count(p => p.Start <= d.Date && d.Date <= p.End) != 1) ||
                periods.Any(p => !dates.Any(d => p.Start <= d.Date && d.Date <= p.End)))
                throw Guard.Error("Cancellation dates and balance periods must match exactly.");
            var stored = periods.OrderBy(p => p.Id).Select(ReadPeriod).ToArray();
            var credits = stored.Select(p => ReadCarryCredit(p.Scope)).ToArray();
            foreach (var p in stored) Lock(p);
            foreach (var credit in credits.Where(c => c != null).OrderBy(c => c.Row.Id))
                LockCarryCredit(credit);
            var plans = new CarryForwardPeriodPlan[stored.Length];
            for (var i = 0; i < stored.Length; i++) {
                var p = stored[i]; var credit = credits[i];
                plans[i] = CarryForwardPeriodPlan.Cancellation(p.Scope, History(p),
                    credit?.Row.Id ?? Guid.Empty, credit?.Credit, CarryHistory(credit),
                    request, cancellation,
                    dates.Where(d => p.Scope.Start <= d.Date && d.Date <= p.Scope.End));
            }
            for (var i = 0; i < stored.Length; i++) {
                if (plans[i].CarryEntry != null) WriteCarry(credits[i], plans[i].CarryEntry,cancellation);
                if (plans[i].AnnualEntry != null) WriteReversal(stored[i], plans[i].AnnualEntry);
            }
            return plans;
        }
    }
}
