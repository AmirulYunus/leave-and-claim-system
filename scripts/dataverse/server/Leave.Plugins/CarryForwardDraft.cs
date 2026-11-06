using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Leave.Plugins
{
    // A calculation for HR's year-end preview, not permission to post a credit
    // or expire the remainder. All amounts are whole hours in the annual ledger.
    public sealed class CarryForwardDraft
    {
        public int SourceUnusedHours { get; private set; }
        public int CarryForwardHours { get; private set; }
        public int SeparatelyRecordedUnusedHours { get; private set; }
        public int LimitHours { get; private set; }
        public bool UsesWrittenException { get; private set; }

        public static CarryForwardDraft Calculate(int unusedHours, int? approvedSpecialLimitHours,
            bool writtenSuperiorApprovalVerifiedByHr)
        {
            if (unusedHours < 0 || unusedHours % 4 != 0)
                throw new ArgumentException("Unused Annual Leave must be a nonnegative half-day amount.");
            if (approvedSpecialLimitHours.HasValue &&
                (approvedSpecialLimitHours.Value <= 40 || approvedSpecialLimitHours.Value % 4 != 0))
                throw new ArgumentException("A special carry-forward limit must exceed five days in half-day increments.");
            if (writtenSuperiorApprovalVerifiedByHr != approvedSpecialLimitHours.HasValue)
                throw new ArgumentException("A special limit requires written Superior approval verified by HR.");
            var limit = approvedSpecialLimitHours ?? 40;
            var carry = Math.Min(unusedHours, limit);
            return new CarryForwardDraft {
                SourceUnusedHours = unusedHours,
                CarryForwardHours = carry,
                SeparatelyRecordedUnusedHours = unusedHours - carry,
                LimitHours = limit,
                UsesWrittenException = approvedSpecialLimitHours.HasValue
            };
        }
    }

    // A year-end HR preview. Nothing here writes a balance entry or expires
    // leave; posting must verify the saved source ledger again under a lock.
    public sealed class CarryForwardReview
    {
        public BalancePeriod SourcePeriod { get; private set; }
        public BalancePeriod TargetPeriod { get; private set; }
        public CarryForwardDraft OriginalCalculation { get; private set; }
        public int ReviewedCarryForwardHours { get; private set; }
        public int SeparatelyRecordedUnusedHours { get; private set; }
        public string AdjustmentReason { get; private set; }
        public string ApprovalDocumentReference { get; private set; }
        public string OperationKey { get; private set; }
        public string SourceLedgerHash { get; private set; }

        public static string HashLedger(BalancePeriod period, IEnumerable<BalanceEntry> entries)
        {
            if (period == null || entries == null) throw new ArgumentNullException("Source balance history is required.");
            var history = entries.ToArray();
            BalanceLedger.AvailableHours(period, history); // Validate identity, duplicates and reversals first.
            var lines = history.OrderBy(e => e.OperationKey, StringComparer.Ordinal).Select(e =>
                e.OperationKey + "|" + ((int)e.Kind).ToString(CultureInfo.InvariantCulture) + "|" +
                e.Hours.ToString(CultureInfo.InvariantCulture) + "|" + e.RequestId.ToString("D") + "|" +
                (e.Reverses ?? ""));
            var payload = period.Id.ToString("D") + "\n" + string.Join("\n", lines);
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(payload)))
                    .Replace("-", "").ToLowerInvariant();
        }

        public static CarryForwardReview Prepare(Guid annualLeaveTypeId, BalancePeriod source, BalancePeriod target,
            IEnumerable<BalanceEntry> sourceEntries,
            int? specialLimitHours, bool writtenSuperiorApprovalVerifiedByHr,
            DateTime asOfMalaysia)
        {
            if (source == null || target == null || sourceEntries == null)
                throw new ArgumentNullException("Year-end balance periods and source ledger are required.");
            BalanceLedger.CalendarDate(asOfMalaysia);
            if (asOfMalaysia < target.Start)
                throw new ArgumentException("Final carry-forward review must wait until the source year has ended.");
            if (annualLeaveTypeId == Guid.Empty || source.LeaveTypeId != annualLeaveTypeId ||
                source.Start != new DateTime(source.Start.Year, 1, 1) ||
                source.End != new DateTime(source.Start.Year, 12, 31) ||
                target.Start != source.End.AddDays(1) ||
                target.End != new DateTime(target.Start.Year, 12, 31) ||
                source.EmployeeId != target.EmployeeId ||
                source.LeaveTypeId != target.LeaveTypeId ||
                source.Id == target.Id)
                throw new ArgumentException("Carry-forward requires consecutive calendar-year balances for the same employee and leave type.");

            var history = sourceEntries.ToArray();
            var unused = BalanceLedger.AvailableHours(source, history);
            var original = CarryForwardDraft.Calculate(unused, specialLimitHours,
                writtenSuperiorApprovalVerifiedByHr);
            return new CarryForwardReview {
                SourcePeriod = source,
                TargetPeriod = target,
                OriginalCalculation = original,
                ReviewedCarryForwardHours = original.CarryForwardHours,
                SeparatelyRecordedUnusedHours = original.SeparatelyRecordedUnusedHours,
                OperationKey = source.Id.ToString("D") + ":carry-forward:" + target.Id.ToString("D"),
                SourceLedgerHash = HashLedger(source, history)
            };
        }

        public CarryForwardReview WithHrAdjustment(int carryHours, string reason,
            string approvalDocumentReference)
        {
            if (carryHours < 0 || carryHours % 4 != 0 ||
                carryHours > OriginalCalculation.SourceUnusedHours ||
                carryHours > OriginalCalculation.LimitHours)
                throw new ArgumentException("The adjusted carry-forward must fit the verified unused leave and approved limit in half-day increments.");
            if (carryHours != OriginalCalculation.CarryForwardHours &&
                (string.IsNullOrWhiteSpace(reason) || string.IsNullOrWhiteSpace(approvalDocumentReference)))
                throw new ArgumentException("An HR adjustment needs a reason and supporting approval document.");
            return new CarryForwardReview {
                SourcePeriod = SourcePeriod,
                TargetPeriod = TargetPeriod,
                OriginalCalculation = OriginalCalculation,
                ReviewedCarryForwardHours = carryHours,
                SeparatelyRecordedUnusedHours = OriginalCalculation.SourceUnusedHours - carryHours,
                AdjustmentReason = carryHours == OriginalCalculation.CarryForwardHours ? null : reason.Trim(),
                ApprovalDocumentReference = carryHours == OriginalCalculation.CarryForwardHours ? null : approvalDocumentReference.Trim(),
                OperationKey = OperationKey,
                SourceLedgerHash = SourceLedgerHash
            };
        }
    }

    public sealed class CarryForwardCredit
    {
        public Guid SourcePeriodId { get; private set; }
        public Guid TargetPeriodId { get; private set; }
        public string OperationKey { get; private set; }
        public int Hours { get; private set; }
        public CarryForwardCredit(Guid sourcePeriodId, Guid targetPeriodId, string operationKey, int hours)
        {
            if (sourcePeriodId == Guid.Empty || targetPeriodId == Guid.Empty || sourcePeriodId == targetPeriodId ||
                string.IsNullOrWhiteSpace(operationKey) || hours <= 0 || hours % 4 != 0)
                throw new ArgumentException("A carry-forward credit needs distinct years, an operation key and positive half-day hours.");
            SourcePeriodId = sourcePeriodId;
            TargetPeriodId = targetPeriodId;
            OperationKey = operationKey;
            Hours = hours;
        }
    }

    // Pure confirmation plan. The registered server action must authorize HR,
    // lock the source/target/review, reread all rows and commit status + credit
    // in its synchronous Dataverse transaction. This class performs no writes.
    public sealed class CarryForwardConfirmation
    {
        public bool AlreadyPosted { get; private set; }
        public CarryForwardCredit CreditToPost { get; private set; }
        public int SeparatelyRecordedUnusedHours { get; private set; }
        public DateTime UseBy { get; private set; }

        public static CarryForwardConfirmation Plan(CarryForwardReview review,
            IEnumerable<BalanceEntry> currentSourceHistory, IEnumerable<CarryForwardCredit> existingCredits,
            DateTime asOfMalaysia)
        {
            if (review == null || currentSourceHistory == null || existingCredits == null)
                throw new ArgumentNullException("Review, current ledger and existing carry-forward credits are required.");
            BalanceLedger.CalendarDate(asOfMalaysia);
            var target = review.TargetPeriod;
            var useBy = new DateTime(target.Start.Year, 3, 31);
            if (asOfMalaysia < target.Start || asOfMalaysia > useBy)
                throw new ArgumentException("Carry-forward confirmation must be after year-end and before the 31 March use-by date.");
            if (HashLedgerForConfirmation(review, currentSourceHistory) != review.SourceLedgerHash)
                throw new InvalidOperationException("The closing-year ledger changed after HR review; prepare a new review before confirming.");
            var credits = existingCredits.ToArray();
            if (credits.Any(c => c == null || c.SourcePeriodId != review.SourcePeriod.Id) || credits.Length > 1)
                throw new InvalidOperationException("Carry-forward credit history has a duplicate or foreign source.");
            var wanted = review.ReviewedCarryForwardHours;
            if (wanted < 0 || wanted % 4 != 0 || wanted > review.OriginalCalculation.SourceUnusedHours ||
                wanted > review.OriginalCalculation.LimitHours ||
                review.SeparatelyRecordedUnusedHours != review.OriginalCalculation.SourceUnusedHours - wanted)
                throw new InvalidOperationException("The HR-reviewed carry-forward amount is invalid.");
            var prior = credits.SingleOrDefault();
            if (prior != null && (prior.OperationKey != review.OperationKey ||
                prior.TargetPeriodId != target.Id || prior.Hours != wanted))
                throw new InvalidOperationException("A different carry-forward credit already exists for this source year.");
            if (prior != null && wanted == 0)
                throw new InvalidOperationException("A zero-hour review cannot have a credit.");
            return new CarryForwardConfirmation {
                AlreadyPosted = prior != null,
                CreditToPost = prior != null || wanted == 0 ? null :
                    new CarryForwardCredit(review.SourcePeriod.Id, target.Id, review.OperationKey, wanted),
                SeparatelyRecordedUnusedHours = review.SeparatelyRecordedUnusedHours,
                UseBy = useBy
            };
        }

        static string HashLedgerForConfirmation(CarryForwardReview review, IEnumerable<BalanceEntry> entries)
            => CarryForwardReview.HashLedger(review.SourcePeriod, entries);
    }

    // Pure allocation for a future transactional approval handler. A posted
    // credit is a separate bucket: it must never be counted as an annual grant.
    public sealed class CarryForwardAllocation
    {
        public int CarryForwardHours { get; private set; }
        public int CurrentYearHours { get; private set; }
        public int CarryForwardRemainingHours { get; private set; }

        public static CarryForwardAllocation Plan(CarryForwardCredit credit,
            DateTime useBy, int previouslyUsedCarryHours,
            IEnumerable<AnnualLeaveDate> requestedDates, int earnedCurrentYearAvailableHours)
        {
            if (credit == null || requestedDates == null)
                throw new ArgumentNullException("Carry-forward credit and request dates are required.");
            BalanceLedger.CalendarDate(useBy);
            if (useBy.Month != 3 || useBy.Day != 31 ||
                previouslyUsedCarryHours < 0 || previouslyUsedCarryHours % 4 != 0 ||
                previouslyUsedCarryHours > credit.Hours ||
                earnedCurrentYearAvailableHours < 0 || earnedCurrentYearAvailableHours % 4 != 0)
                throw new ArgumentException("Carry-forward usage, earned balance or use-by date is invalid.");
            var dates = requestedDates.ToArray();
            if (dates.Length == 0 || dates.Any(d => d == null || d.Date.Year != useBy.Year ||
                d.Hours != 4 && d.Hours != 8) ||
                dates.GroupBy(d => d.Date).Any(g => g.Count() != 1))
                throw new ArgumentException("Unique 4/8-hour dates in the target calendar year are required.");
            var availableCarry = credit.Hours - previouslyUsedCarryHours;
            var carried = 0;
            var current = 0;
            foreach (var date in dates.OrderBy(d => d.Date)) {
                var fromCarry = date.Date <= useBy ? Math.Min(date.Hours, availableCarry) : 0;
                carried = checked(carried + fromCarry);
                availableCarry -= fromCarry;
                current = checked(current + date.Hours - fromCarry);
            }
            if (current > earnedCurrentYearAvailableHours)
                throw new InvalidOperationException("The request exceeds earned current-year Annual Leave after using eligible carry-forward first.");
            return new CarryForwardAllocation {
                CarryForwardHours = carried,
                CurrentYearHours = current,
                CarryForwardRemainingHours = availableCarry
            };
        }
    }

    public enum CarryForwardUsageKind { ApprovalDebit, CancellationReversal }

    public sealed class CarryForwardUsageEntry
    {
        public Guid CreditId { get; private set; }
        public Guid RequestId { get; private set; }
        public string OperationKey { get; private set; }
        public CarryForwardUsageKind Kind { get; private set; }
        public int Hours { get; private set; }
        public string Reverses { get; private set; }

        public CarryForwardUsageEntry(Guid creditId, Guid requestId, string operationKey,
            CarryForwardUsageKind kind, int hours, string reverses = null)
        {
            if (creditId == Guid.Empty || requestId == Guid.Empty ||
                string.IsNullOrWhiteSpace(operationKey) || hours <= 0 || hours % 4 != 0 ||
                !Enum.IsDefined(typeof(CarryForwardUsageKind), kind) ||
                (kind == CarryForwardUsageKind.ApprovalDebit && reverses != null) ||
                (kind == CarryForwardUsageKind.CancellationReversal && string.IsNullOrWhiteSpace(reverses)))
                throw new ArgumentException("Carry-forward usage must have a valid immutable debit or reversal identity.");
            CreditId = creditId;
            RequestId = requestId;
            OperationKey = operationKey;
            Kind = kind;
            Hours = hours;
            Reverses = reverses;
        }
    }

    // Validates the posted credit's complete immutable history before approval
    // or cancellation. The caller still has to lock the saved credit row and
    // persist the result inside the request's synchronous transaction.
    public static class CarryForwardUsageLedger
    {
        static CarryForwardUsageEntry[] Valid(Guid creditId, int creditedHours,
            IEnumerable<CarryForwardUsageEntry> history)
        {
            if (creditId == Guid.Empty || creditedHours <= 0 || creditedHours % 4 != 0 || history == null)
                throw new ArgumentException("A posted credit and complete usage history are required.");
            var entries = history.ToArray();
            if (entries.Any(e => e == null || e.CreditId != creditId) ||
                entries.GroupBy(e => e.OperationKey, StringComparer.Ordinal).Any(g => g.Count() != 1) ||
                entries.Where(e => e.Kind == CarryForwardUsageKind.ApprovalDebit)
                    .GroupBy(e => e.RequestId).Any(g => g.Count() != 1))
                throw new InvalidOperationException("Carry-forward usage has a foreign credit or duplicate operation.");
            foreach (var reversal in entries.Where(e => e.Kind == CarryForwardUsageKind.CancellationReversal)) {
                var debit = entries.SingleOrDefault(e => e.OperationKey == reversal.Reverses);
                if (debit == null || debit.Kind != CarryForwardUsageKind.ApprovalDebit ||
                    debit.RequestId != reversal.RequestId || debit.Hours != reversal.Hours ||
                    entries.Count(e => e.Kind == CarryForwardUsageKind.CancellationReversal &&
                        e.Reverses == debit.OperationKey) != 1)
                    throw new InvalidOperationException("A carry-forward reversal must restore one exact original debit once.");
            }
            var used = entries.Where(e => e.Kind == CarryForwardUsageKind.ApprovalDebit)
                .Sum(e => (long)e.Hours) - entries.Where(e => e.Kind == CarryForwardUsageKind.CancellationReversal)
                .Sum(e => (long)e.Hours);
            if (used < 0 || used > creditedHours)
                throw new InvalidOperationException("Carry-forward usage exceeds its posted credit.");
            return entries;
        }

        public static int AvailableHours(Guid creditId, int creditedHours,
            IEnumerable<CarryForwardUsageEntry> history)
        {
            var entries = Valid(creditId, creditedHours, history);
            return checked(creditedHours - entries.Where(e => e.Kind == CarryForwardUsageKind.ApprovalDebit)
                .Sum(e => e.Hours) + entries.Where(e => e.Kind == CarryForwardUsageKind.CancellationReversal)
                .Sum(e => e.Hours));
        }

        public static CarryForwardAllocation PlanApproval(Guid creditId, CarryForwardCredit credit,
            DateTime useBy, IEnumerable<CarryForwardUsageEntry> history,
            Guid requestId, IEnumerable<AnnualLeaveDate> dates, int earnedCurrentYearAvailableHours)
        {
            if (credit == null || requestId == Guid.Empty)
                throw new ArgumentException("A posted credit and approved request are required.");
            var entries = Valid(creditId, credit.Hours, history);
            if (entries.Any(e => e.RequestId == requestId))
                throw new InvalidOperationException("This request already has carry-forward usage history.");
            return CarryForwardAllocation.Plan(credit, useBy,
                credit.Hours - AvailableHours(creditId, credit.Hours, entries),
                dates, earnedCurrentYearAvailableHours);
        }

        public static CarryForwardUsageEntry PlanCancellation(Guid creditId, int creditedHours,
            IEnumerable<CarryForwardUsageEntry> history, Guid requestId, Guid cancellationId)
        {
            if (requestId == Guid.Empty || cancellationId == Guid.Empty)
                throw new ArgumentException("The original request and cancellation are required.");
            var entries = Valid(creditId, creditedHours, history);
            var debit = entries.SingleOrDefault(e => e.Kind == CarryForwardUsageKind.ApprovalDebit &&
                e.RequestId == requestId);
            if (debit == null || entries.Any(e => e.Kind == CarryForwardUsageKind.CancellationReversal &&
                e.Reverses == debit.OperationKey))
                throw new InvalidOperationException("An unreversed carry-forward debit for this request is required.");
            return new CarryForwardUsageEntry(creditId, requestId,
                cancellationId.ToString("D") + ":carry-reversed:" + creditId.ToString("D"),
                CarryForwardUsageKind.CancellationReversal, debit.Hours, debit.OperationKey);
        }
    }

    // Plans one calendar-year slice of an approval or cancellation. A request
    // crossing New Year is handled as one slice per verified balance period.
    // The server must persist both returned entries in the decision transaction.
    public sealed class CarryForwardPeriodPlan
    {
        public BalanceEntry AnnualEntry { get; private set; }
        public CarryForwardUsageEntry CarryEntry { get; private set; }

        static AnnualLeaveDate[] Dates(BalancePeriod period, IEnumerable<AnnualLeaveDate> dates)
        {
            if (period == null || dates == null) throw new ArgumentNullException("Balance period and dates are required.");
            var days = dates.ToArray();
            if (days.Length == 0 || days.Any(d => d == null || d.Date < period.Start || d.Date > period.End) ||
                days.GroupBy(d => d.Date).Any(g => g.Count() != 1))
                throw new ArgumentException("Unique leave dates in this balance period are required.");
            return days;
        }

        public static CarryForwardPeriodPlan Approval(BalancePeriod period,
            IEnumerable<BalanceEntry> annualHistory, int earnedCapHours,
            Guid creditId, CarryForwardCredit credit, DateTime useBy,
            IEnumerable<CarryForwardUsageEntry> carryHistory,
            Guid requestId, IEnumerable<AnnualLeaveDate> dates)
        {
            if (period == null || annualHistory == null || carryHistory == null || requestId == Guid.Empty)
                throw new ArgumentException("Complete approval history and scope are required.");
            var days = Dates(period, dates);
            var annual = annualHistory.ToArray();
            var available = BalanceLedger.EarnedAvailableHours(period, annual, earnedCapHours);
            if (annual.Any(e => e.Kind == BalanceEntryKind.ApprovedLeave && e.RequestId == requestId))
                throw new InvalidOperationException("This request already has an Annual Leave deduction.");
            int carryHours, currentHours;
            if (credit == null) {
                if (creditId != Guid.Empty || carryHistory.Any())
                    throw new InvalidOperationException("Carry-forward usage has no posted credit.");
                carryHours = 0;
                currentHours = days.Sum(d => d.Hours);
            } else {
                if (creditId == Guid.Empty || credit.TargetPeriodId != period.Id)
                    throw new InvalidOperationException("Carry-forward credit does not match the balance period.");
                var allocation = CarryForwardUsageLedger.PlanApproval(creditId, credit, useBy,
                    carryHistory, requestId, days, available);
                carryHours = allocation.CarryForwardHours;
                currentHours = allocation.CurrentYearHours;
            }
            if (currentHours > available)
                throw new InvalidOperationException("Insufficient earned Annual Leave after carry-forward allocation.");
            return new CarryForwardPeriodPlan {
                AnnualEntry = currentHours == 0 ? null : new BalanceEntry(period.Id,
                    requestId.ToString("D") + ":approved:" + period.Id.ToString("D"),
                    BalanceEntryKind.ApprovedLeave, -currentHours, requestId),
                CarryEntry = carryHours == 0 ? null : new CarryForwardUsageEntry(creditId, requestId,
                    requestId.ToString("D") + ":carry-approved:" + creditId.ToString("D"),
                    CarryForwardUsageKind.ApprovalDebit, carryHours)
            };
        }

        public static CarryForwardPeriodPlan Cancellation(BalancePeriod period,
            IEnumerable<BalanceEntry> annualHistory,
            Guid creditId, CarryForwardCredit credit, IEnumerable<CarryForwardUsageEntry> carryHistory,
            Guid requestId, Guid cancellationId, IEnumerable<AnnualLeaveDate> dates)
        {
            if (period == null || annualHistory == null || carryHistory == null ||
                requestId == Guid.Empty || cancellationId == Guid.Empty)
                throw new ArgumentException("Complete cancellation history and scope are required.");
            var days = Dates(period, dates);
            var annual = annualHistory.ToArray();
            BalanceLedger.AvailableHours(period, annual);
            var savedCarry = carryHistory.ToArray();
            if (credit == null && (creditId != Guid.Empty || savedCarry.Length != 0))
                throw new InvalidOperationException("Carry-forward usage has no posted credit.");
            if (credit != null && (creditId == Guid.Empty || credit.TargetPeriodId != period.Id))
                throw new InvalidOperationException("Carry-forward credit does not match the balance period.");
            if (credit != null) CarryForwardUsageLedger.AvailableHours(creditId, credit.Hours, savedCarry);
            var debit = annual.SingleOrDefault(e => e.Kind == BalanceEntryKind.ApprovedLeave && e.RequestId == requestId);
            var carryDebit = savedCarry.SingleOrDefault(e => e.Kind == CarryForwardUsageKind.ApprovalDebit &&
                e.RequestId == requestId);
            var total = days.Sum(d => d.Hours);
            if ((debit == null ? 0 : -debit.Hours) + (carryDebit == null ? 0 : carryDebit.Hours) != total)
                throw new InvalidOperationException("Saved annual and carry-forward debits do not equal the approved request.");
            if (debit != null && annual.Any(e => e.Kind == BalanceEntryKind.Reversal &&
                e.Reverses == debit.OperationKey))
                throw new InvalidOperationException("The Annual Leave deduction was already reversed.");
            return new CarryForwardPeriodPlan {
                AnnualEntry = debit == null ? null : new BalanceEntry(period.Id,
                    cancellationId.ToString("D") + ":reversed:" + period.Id.ToString("D"),
                    BalanceEntryKind.Reversal, -debit.Hours, requestId, debit.OperationKey),
                CarryEntry = carryDebit == null ? null : CarryForwardUsageLedger.PlanCancellation(
                    creditId, credit.Hours, savedCarry, requestId, cancellationId)
            };
        }
    }
}
