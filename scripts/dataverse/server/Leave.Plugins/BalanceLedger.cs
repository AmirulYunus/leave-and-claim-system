using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Leave.Plugins
{
    // Policy-independent accounting core. No IPlugin registration; the internal
    // persistence adapter still needs validated calendars, authority and guards,
    // lock the scoped balance, and persist all planned entries in one transaction.
    // Periods are explicit; this never assumes a January or service-year reset.
    public sealed class BalancePeriod
    {
        public Guid Id { get; private set; }
        public Guid EmployeeId { get; private set; }
        public Guid LeaveTypeId { get; private set; }
        public DateTime Start { get; private set; }
        public DateTime End { get; private set; }
        public BalancePeriod(Guid id, Guid employee, Guid leaveType, DateTime start, DateTime end)
        {
            if (id == Guid.Empty || employee == Guid.Empty || leaveType == Guid.Empty)
                throw new ArgumentException("Balance scope identities are required.");
            BalanceLedger.CalendarDate(start); BalanceLedger.CalendarDate(end);
            if (end < start) throw new ArgumentException("Balance period is reversed.");
            Id = id; EmployeeId = employee; LeaveTypeId = leaveType; Start = start; End = end;
        }
    }

    public enum BalanceEntryKind { Grant, ApprovedLeave, Reversal }
    public sealed class BalanceEntry
    {
        public Guid PeriodId { get; private set; }
        public string OperationKey { get; private set; }
        public BalanceEntryKind Kind { get; private set; }
        public int Hours { get; private set; }
        public Guid RequestId { get; private set; }
        public string Reverses { get; private set; }
        public BalanceEntry(Guid period, string operation, BalanceEntryKind kind, int hours,
            Guid request = default(Guid), string reverses = null)
        {
            if (period == Guid.Empty || string.IsNullOrWhiteSpace(operation)) throw new ArgumentException("Ledger identity is required.");
            if ((kind == BalanceEntryKind.Grant && hours <= 0) ||
                (kind == BalanceEntryKind.ApprovedLeave && (hours >= 0 || request == Guid.Empty)) ||
                (kind == BalanceEntryKind.Reversal && (hours <= 0 || request == Guid.Empty || string.IsNullOrWhiteSpace(reverses))) ||
                !Enum.IsDefined(typeof(BalanceEntryKind), kind)) throw new ArgumentException("Invalid ledger entry.");
            if (kind == BalanceEntryKind.Grant && request != Guid.Empty || kind != BalanceEntryKind.Reversal && reverses != null)
                throw new ArgumentException("Ledger links do not match the entry kind.");
            PeriodId = period; OperationKey = operation; Kind = kind; Hours = hours; RequestId = request; Reverses = reverses;
        }
    }
    public sealed class AnnualLeaveDate
    {
        public DateTime Date { get; private set; }
        public int Hours { get; private set; }
        public AnnualLeaveDate(DateTime date, int hours)
        {
            BalanceLedger.CalendarDate(date);
            if (hours != 4 && hours != 8) throw new ArgumentException("Annual leave requires four or eight hours per date.");
            Date = date; Hours = hours;
        }
    }
    public sealed class BalanceGrantPlan
    {
        public string Status { get; internal set; }
        public string Reason { get; internal set; }
        public int ExistingAvailableHours { get; internal set; }
        public BalanceEntry Entry { get; internal set; }
    }

    public static class BalanceLedger
    {
        internal static void CalendarDate(DateTime date)
        {
            if (date.TimeOfDay != TimeSpan.Zero) throw new ArgumentException("A calendar date without a time is required.");
        }
        static BalanceEntry[] ValidEntries(BalancePeriod period, IEnumerable<BalanceEntry> history)
        {
            if (period == null || history == null) throw new ArgumentNullException();
            var entries = history.ToArray();
            if (entries.Any(e => e == null || e.PeriodId != period.Id)) throw new ArgumentException("Ledger history must belong to this period.");
            if (entries.GroupBy(e => e.OperationKey, StringComparer.Ordinal).Any(g => g.Count() != 1))
                throw new InvalidOperationException("Duplicate operation in ledger history.");
            if (entries.Where(e => e.Kind == BalanceEntryKind.ApprovedLeave).GroupBy(e => e.RequestId).Any(g => g.Count() != 1))
                throw new InvalidOperationException("A request has more than one deduction in this period.");
            foreach (var reversal in entries.Where(e => e.Kind == BalanceEntryKind.Reversal)) {
                var original = entries.SingleOrDefault(e => e.OperationKey == reversal.Reverses);
                if (original == null || original.Kind != BalanceEntryKind.ApprovedLeave ||
                    reversal.RequestId != original.RequestId || (long)reversal.Hours != -(long)original.Hours ||
                    entries.Count(e => e.Kind == BalanceEntryKind.Reversal && e.Reverses == original.OperationKey) != 1)
                    throw new InvalidOperationException("A reversal must restore its original deduction exactly once.");
            }
            if (entries.Sum(e => (long)e.Hours) < 0) throw new InvalidOperationException("Ledger history is overdrawn.");
            return entries;
        }
        public static int AvailableHours(BalancePeriod period, IEnumerable<BalanceEntry> history)
            => checked((int)ValidEntries(period, history).Sum(e => (long)e.Hours));

        // A full-year grant already recorded in the ledger cannot make future
        // months usable early. The server must calculate earnedCapHours from
        // saved employment inputs and the current Malaysia date, never from a
        // client-supplied amount. Source-only until the approval path is wired.
        public static int EarnedAvailableHours(BalancePeriod period,
            IEnumerable<BalanceEntry> history, int earnedCapHours)
        {
            if (earnedCapHours < 0 || earnedCapHours % 4 != 0)
                throw new ArgumentException("Earned leave must be in half-day increments.");
            var entries = ValidEntries(period, history);
            var grants = entries.Where(e => e.Kind == BalanceEntryKind.Grant)
                .Sum(e => (long)e.Hours);
            var used = entries.Where(e => e.Kind == BalanceEntryKind.ApprovedLeave)
                .Sum(e => -(long)e.Hours) - entries.Where(e => e.Kind == BalanceEntryKind.Reversal)
                .Sum(e => (long)e.Hours);
            if (used < 0 || used > earnedCapHours)
                throw new InvalidOperationException("Recorded leave usage exceeds earned leave; review required.");
            return checked((int)(Math.Min(grants, earnedCapHours) - used));
        }

        public static BalanceEntry[] PlanApprovedAnnualLeaveCapped(Guid employee,
            Guid type, Guid request, IEnumerable<AnnualLeaveDate> dates,
            IEnumerable<BalancePeriod> periods, IEnumerable<BalanceEntry> history,
            IReadOnlyDictionary<Guid, int> earnedCaps)
        {
            if (earnedCaps == null) throw new ArgumentNullException("earnedCaps");
            var scopes = periods.ToArray();
            var entries = history.ToArray();
            var plan = PlanApprovedAnnualLeave(employee, type, request, dates, scopes, entries);
            foreach (var debit in plan) {
                if (!earnedCaps.TryGetValue(debit.PeriodId, out var cap) ||
                    EarnedAvailableHours(scopes.Single(p => p.Id == debit.PeriodId),
                        entries.Where(e => e.PeriodId == debit.PeriodId), cap) < -debit.Hours)
                    throw new InvalidOperationException("Insufficient earned annual leave balance.");
            }
            return plan;
        }

        // Caller supplies a policy-approved full-period rate. This does not turn
        // first/last-year rates into grants or invent any proration/expiry rule.
        public static BalanceGrantPlan PlanGrant(BalancePeriod period, IEnumerable<BalanceEntry> history, EntitlementRate rate,
            string category = "annual")
        {
            if (category != "annual" && category != "sick" && category != "hospitalisation")
                throw new ArgumentException("Unsupported balance grant category.");
            var entries = ValidEntries(period, history);
            var result = new BalanceGrantPlan { ExistingAvailableHours = checked((int)entries.Sum(e => (long)e.Hours)) };
            if (rate == null || rate.Status != "Calculated") {
                result.Status = rate == null ? "PendingHrDetails" : rate.Status;
                result.Reason = rate == null ? "Entitlement has not been calculated." : rate.Reason;
                return result; // No zero entitlement and no balance replacement.
            }
            if (!rate.Hours.HasValue || rate.Hours.Value <= 0 || !rate.Days.HasValue || (long)rate.Days.Value * 8 != rate.Hours.Value)
                throw new ArgumentException("A valid annual entitlement in eight-hour days is required.");
            var granted = checked((int)entries.Where(e => e.Kind == BalanceEntryKind.Grant).Sum(e => (long)e.Hours));
            if (rate.Hours.Value < granted) {
                result.Status = "PendingAdjustment";
                result.Reason = "The new entitlement is lower than existing grants; preserve the balance for review.";
                return result;
            }
            var delta = rate.Hours.Value - granted;
            result.Status = delta == 0 ? "AlreadyGranted" : "Ready";
            if (delta != 0) result.Entry = new BalanceEntry(period.Id,
                period.Id.ToString("D") + ":" + category + "-grant:" + rate.Hours.Value.ToString(CultureInfo.InvariantCulture),
                BalanceEntryKind.Grant, delta);
            return result;
        }

        // Incremental earned credit. Never remove an older grant silently:
        // already-posted full-year credits need separate audited reconciliation.
        public static BalanceGrantPlan PlanEarnedGrant(BalancePeriod period,
            IEnumerable<BalanceEntry> history, int targetEarnedHours)
        {
            var entries = ValidEntries(period, history);
            if (targetEarnedHours < 0 || targetEarnedHours % 4 != 0)
                throw new ArgumentException("Earned credit must be a nonnegative half-day amount.");
            var result = new BalanceGrantPlan {
                ExistingAvailableHours = checked((int)entries.Sum(e => (long)e.Hours))
            };
            var granted = checked((int)entries.Where(e => e.Kind == BalanceEntryKind.Grant)
                .Sum(e => (long)e.Hours));
            if (granted > targetEarnedHours) {
                result.Status = "PendingAdjustment";
                result.Reason = "Recorded grants exceed earned leave; preserve history for review.";
                return result;
            }
            var delta = targetEarnedHours - granted;
            result.Status = delta == 0 ? "AlreadyGranted" : "Ready";
            if (delta != 0) result.Entry = new BalanceEntry(period.Id,
                period.Id.ToString("D") + ":earned-grant:" + targetEarnedHours.ToString(CultureInfo.InvariantCulture),
                BalanceEntryKind.Grant, delta);
            return result;
        }

        // Invoked only after server approval authorization and work-calendar
        // validation. Child dates, not inclusive range or legacy HoursPerDay,
        // supply the amount. A cross-period request returns one entry per period.
        public static BalanceEntry[] PlanApprovedAnnualLeave(Guid employee, Guid type, Guid request,
            IEnumerable<AnnualLeaveDate> dates, IEnumerable<BalancePeriod> periods,
            IEnumerable<BalanceEntry> history)
        {
            if (employee == Guid.Empty || type == Guid.Empty || request == Guid.Empty) throw new ArgumentException("Request scope is required.");
            if (dates == null || periods == null || history == null) throw new ArgumentNullException();
            var days = dates.ToArray(); var suppliedPeriods = periods.ToArray(); var entries = history.ToArray();
            if (suppliedPeriods.Any(p => p == null)) throw new ArgumentException("A balance period is missing.");
            var buckets = suppliedPeriods.OrderBy(p => p.Start).ThenBy(p => p.Id).ToArray();
            if (days.Length == 0 || days.Any(d => d == null)) throw new ArgumentException("Approved annual leave requires per-date durations.");
            if (days.GroupBy(d => d.Date).Any(g => g.Count() != 1)) throw new ArgumentException("Duplicate leave date.");
            if (buckets.Any(p => p.EmployeeId != employee || p.LeaveTypeId != type) || buckets.GroupBy(p => p.Id).Any(g => g.Count() != 1))
                throw new ArgumentException("All periods must belong to the request's employee and leave type.");
            for (var i = 1; i < buckets.Length; i++) if (buckets[i].Start <= buckets[i - 1].End) throw new ArgumentException("Balance periods overlap.");
            if (entries.Any(e => e == null || !buckets.Any(p => p.Id == e.PeriodId))) throw new ArgumentException("History contains a foreign balance period.");
            var amounts = new Dictionary<Guid, int>();
            foreach (var day in days) {
                var period = buckets.SingleOrDefault(p => p.Start <= day.Date && day.Date <= p.End);
                if (period == null) throw new InvalidOperationException("No granted balance period covers a requested date.");
                amounts[period.Id] = checked((amounts.ContainsKey(period.Id) ? amounts[period.Id] : 0) + day.Hours);
            }
            var result = new List<BalanceEntry>();
            foreach (var period in buckets) {
                var scoped = ValidEntries(period, entries.Where(e => e.PeriodId == period.Id));
                var existing = scoped.SingleOrDefault(e => e.Kind == BalanceEntryKind.ApprovedLeave && e.RequestId == request);
                int amount; var included = amounts.TryGetValue(period.Id, out amount);
                if (existing != null) {
                    if (!included || existing.Hours != -amount) throw new InvalidOperationException("Approved leave already has a different deduction.");
                    if (scoped.Any(e => e.Kind == BalanceEntryKind.Reversal && e.Reverses == existing.OperationKey))
                        throw new InvalidOperationException("Cancelled leave cannot be deducted again.");
                    continue; // Exact retry; do not deduct again.
                }
                if (!included) continue;
                if (scoped.Sum(e => (long)e.Hours) < amount) throw new InvalidOperationException("Insufficient annual leave balance.");
                result.Add(new BalanceEntry(period.Id, request.ToString("D") + ":approved:" + period.Id.ToString("D"),
                    BalanceEntryKind.ApprovedLeave, -amount, request));
            }
            return result.ToArray();
        }

        // A cancellation restores the exact committed debit in each period.
        // The caller must first verify a future approved request and an
        // authorized Superior decision; no pending cancellation changes use.
        public static BalanceEntry[] PlanExactAnnualReversal(Guid employee, Guid type, Guid request,
            Guid cancellation, IEnumerable<AnnualLeaveDate> dates,
            IEnumerable<BalancePeriod> periods, IEnumerable<BalanceEntry> history)
        {
            if (cancellation == Guid.Empty || history == null) throw new ArgumentException("Cancellation and saved ledger history are required.");
            var scopes = periods?.ToArray() ?? throw new ArgumentNullException(nameof(periods));
            var entries = history.ToArray();
            var pendingDebits = PlanApprovedAnnualLeave(employee,type,request,dates,scopes,entries);
            if (pendingDebits.Length != 0) throw new InvalidOperationException("Every selected date must already have its exact approved debit.");
            var debits = entries.Where(e => e.Kind == BalanceEntryKind.ApprovedLeave && e.RequestId == request).ToArray();
            if (debits.Length == 0 || debits.Length != scopes.Count(p => debits.Any(d => d.PeriodId == p.Id)))
                throw new InvalidOperationException("Complete approved deductions are required before reversal.");
            return debits.OrderBy(d => d.PeriodId).Select(d => new BalanceEntry(d.PeriodId,
                cancellation.ToString("D") + ":reversed:" + d.PeriodId.ToString("D"),
                BalanceEntryKind.Reversal, checked(-d.Hours), request, d.OperationKey)).ToArray();
        }
    }
}
