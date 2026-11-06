using System;

namespace Leave.Plugins
{
    public sealed class CalendarGrantAssessment
    {
        public DateTime PeriodStart { get; internal set; }
        public DateTime PeriodEnd { get; internal set; }
        public EntitlementRate Annual { get; internal set; }
    }

    public sealed class FirstYearAnnualAccrual
    {
        public int MonthsEarned { get; internal set; }
        public int MonthsInYear { get; internal set; }
        public int EarnedHours { get; internal set; }
        public int YearEndHours { get; internal set; }
    }

    // Calendar-year grants only. Rates change on service anniversaries; a period
    // boundary does not expire, delete or carry forward an earlier ledger.
    public static class CalendarAnnualGrant
    {
        // Company rule: any day worked in the joining month counts that month.
        // This is a calculation helper only; it does not authorize a grant.
        public static int InclusiveMonths(DateTime joined, DateTime through)
        {
            BalanceLedger.CalendarDate(joined);
            BalanceLedger.CalendarDate(through);
            if (through < joined) return 0;
            return checked((through.Year - joined.Year) * 12 + through.Month - joined.Month + 1);
        }

        // The joining month counts in full once its calendar month has ended.
        // The current, unfinished calendar month has not been earned yet.
        public static int CompletedCalendarMonths(DateTime joined, DateTime asOf)
        {
            BalanceLedger.CalendarDate(joined);
            BalanceLedger.CalendarDate(asOf);
            if (asOf < joined) return 0;
            var completedThrough = new DateTime(asOf.Year, asOf.Month, 1).AddDays(-1);
            if (completedThrough < joined) return 0;
            return InclusiveMonths(joined, completedThrough);
        }

        // Round the cumulative monthly fraction once, to the next half-day.
        // Four hours represent half a standard eight-hour working day. This
        // helper does not post grants or decide when future leave may be used.
        public static int RoundedCumulativeHours(int annualDays, int eligibleMonths)
        {
            if (annualDays <= 0 || eligibleMonths < 0 || eligibleMonths > 12)
                throw new ArgumentOutOfRangeException("A positive annual rate and 0–12 eligible months are required.");
            var halfDays = checked(((long)annualDays * eligibleMonths * 2 + 11) / 12);
            return checked((int)(halfDays * 4));
        }

        // Pure first-year calculation for a fixed full-year rate. Posting to
        // Dataverse and restricting approval to earned balance are separate.
        public static FirstYearAnnualAccrual FirstYearAccrual(DateTime joined,
            int annualDays, DateTime asOf)
        {
            BalanceLedger.CalendarDate(joined);
            BalanceLedger.CalendarDate(asOf);
            if (joined.Year != asOf.Year)
                throw new ArgumentException("First-year accrual needs a date in the joining calendar year.");
            var monthsEarned = CompletedCalendarMonths(joined, asOf);
            var monthsInYear = InclusiveMonths(joined, new DateTime(joined.Year, 12, 31));
            return new FirstYearAnnualAccrual {
                MonthsEarned = monthsEarned,
                MonthsInYear = monthsInYear,
                EarnedHours = RoundedCumulativeHours(annualDays, monthsEarned),
                YearEndHours = RoundedCumulativeHours(annualDays, monthsInYear)
            };
        }

        // Sum each eligible calendar month's annual rate, then round the total
        // upward once to a half-day. The caller supplies the rate effective at
        // the assessment date; this supports service-anniversary changes without
        // retroactively applying the new rate to earlier months. The live grant
        // and approval operations use this through their trusted assessments.
        public static int CumulativeEarnedHours(DateTime joined, DateTime asOf,
            Func<DateTime, int> annualDaysAt, bool assessmentYearClosed = false)
        {
            BalanceLedger.CalendarDate(joined);
            BalanceLedger.CalendarDate(asOf);
            if (annualDaysAt == null) throw new ArgumentNullException("annualDaysAt");
            if (asOf < joined) return 0;
            var firstMonth = joined.Year == asOf.Year ? joined.Month : 1;
            long sumAnnualRates = 0;
            var lastCompletedMonth = assessmentYearClosed ? 12 : asOf.Month - 1;
            for (var month = firstMonth; month <= lastCompletedMonth; month++) {
                var monthEnd = new DateTime(asOf.Year, month,
                    DateTime.DaysInMonth(asOf.Year, month));
                var days = annualDaysAt(monthEnd);
                if (days <= 0) throw new ArgumentOutOfRangeException("annualDaysAt",
                    "Each month's annual rate must be positive.");
                sumAnnualRates = checked(sumAnnualRates + days);
            }
            var halfDays = checked((sumAnnualRates * 2 + 11) / 12);
            return checked((int)(halfDays * 4));
        }

        // A trusted server-side cap for approval. Future-dated requests are
        // assessed against today's earned amount, never their requested date.
        // A future balance year has earned nothing yet. Unresolved employment
        // rules fail closed rather than turning a full-year rate into credit.
        public static int ApprovalEarnedCap(DateTime? joined, int? level,
            int? employmentType, bool? active, DateTime? leaving,
            DateTime periodStart, DateTime today)
        {
            BalanceLedger.CalendarDate(periodStart);
            BalanceLedger.CalendarDate(today);
            if (periodStart.Month != 1 || periodStart.Day != 1)
                throw new ArgumentException("Annual balance must start on 1 January.");
            if (!active.HasValue || !active.Value || !joined.HasValue ||
                !level.HasValue || !employmentType.HasValue)
                throw new InvalidOperationException("Complete active employment details are required to assess earned leave.");
            BalanceLedger.CalendarDate(joined.Value);
            if (leaving.HasValue) {
                BalanceLedger.CalendarDate(leaving.Value);
                if (leaving.Value < joined.Value)
                    throw new InvalidOperationException("Leaving date precedes joining date.");
                if (leaving.Value.Year == periodStart.Year)
                    throw new InvalidOperationException("Last-year annual leave policy remains pending.");
                if (leaving.Value.Year < periodStart.Year)
                    throw new InvalidOperationException("The employee left before this balance year.");
            }
            if (periodStart.Year > today.Year) return 0;
            var through = periodStart.Year == today.Year
                ? today : new DateTime(periodStart.Year, 12, 31);
            if (joined.Value > through)
                throw new InvalidOperationException("The employee had not joined by the balance assessment date.");
            return CumulativeEarnedHours(joined.Value, through, monthDate => {
                var rate = EntitlementCalculator.Calculate(joined, level, employmentType, monthDate).Annual;
                if (rate.Status != "Calculated" || !rate.Days.HasValue)
                    throw new InvalidOperationException("Annual leave rate is unresolved: " + rate.Reason);
                return rate.Days.Value;
            }, periodStart.Year < today.Year);
        }

        public static CalendarGrantAssessment Assess(DateTime? joined, int? level,
            int? employmentType, bool? active, DateTime? leaving, DateTime asOf)
        {
            BalanceLedger.CalendarDate(asOf);
            var result = new CalendarGrantAssessment {
                PeriodStart = new DateTime(asOf.Year, 1, 1),
                PeriodEnd = new DateTime(asOf.Year, 12, 31),
                Annual = EntitlementCalculator.Calculate(joined, level, employmentType, asOf).Annual
            };
            if (result.Annual.Status != "Calculated") return result;
            if (!active.HasValue) {
                result.Annual = Pending("PendingHrDetails", "Employee active status must be supplied.");
                return result;
            }
            if (leaving.HasValue && (leaving.Value.TimeOfDay != TimeSpan.Zero || leaving.Value < joined.Value)) {
                result.Annual = Pending("InvalidDetails", "Leaving date must be a calendar date on or after Date Joined.");
                return result;
            }
            if (!active.Value || (leaving.HasValue && leaving.Value < result.PeriodStart)) {
                result.Annual = Pending("Inactive", "No new grant for an inactive or previously departed employee; preserve history.");
                return result;
            }
            // This full-year assessment never turns a rate into a first-year
            // grant. The secured grant operation separately calculates earned
            // first-year hours using the confirmed month and rounding rules.
            if (joined.Value >= result.PeriodStart) {
                result.Annual = Pending("PendingPolicy", "First-year grants and proration are on hold.");
                return result;
            }
            if (leaving.HasValue && leaving.Value <= result.PeriodEnd) {
                result.Annual = Pending("PendingPolicy", "Last-year grants and proration are on hold.");
                return result;
            }
            return result;
        }
        static EntitlementRate Pending(string status, string reason)
            => new EntitlementRate { Status = status, Reason = reason };
    }
}
