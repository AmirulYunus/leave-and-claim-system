using Leave.Plugins;

static class CalendarGrantChecks
{
    public static void Run()
    {
        var count = 0;
        void Check(string name, Action test) { test(); count++; Console.WriteLine("PASS calendar grant: " + name); }
        void Eq(object actual, object expected) { if (!Equals(actual,expected)) throw new Exception($"Expected {expected}; got {actual}"); }
        var asOf = new DateTime(2026,10,10);
        Check("joining month counts even when joining late in February", () => {
            var joined = new DateTime(2026,2,20);
            Eq(CalendarAnnualGrant.InclusiveMonths(joined,new DateTime(2026,12,31)),11);
            Eq(CalendarAnnualGrant.InclusiveMonths(joined,new DateTime(2026,10,31)),9);
            Eq(CalendarAnnualGrant.InclusiveMonths(joined,new DateTime(2026,2,20)),1);
            Eq(CalendarAnnualGrant.InclusiveMonths(joined,new DateTime(2026,2,19)),0);
            Eq(CalendarAnnualGrant.CompletedCalendarMonths(joined,new DateTime(2026,2,20)),0);
            Eq(CalendarAnnualGrant.CompletedCalendarMonths(joined,new DateTime(2026,2,28)),0);
            Eq(CalendarAnnualGrant.CompletedCalendarMonths(joined,new DateTime(2026,3,1)),1);
            Eq(CalendarAnnualGrant.CompletedCalendarMonths(joined,new DateTime(2026,11,3)),9);
        });
        Check("cumulative proration rounds upward once to a half-day", () => {
            Eq(CalendarAnnualGrant.RoundedCumulativeHours(14,11),104); // 13 days
            Eq(CalendarAnnualGrant.RoundedCumulativeHours(14,9),84);  // 10.5 days
            Eq(CalendarAnnualGrant.RoundedCumulativeHours(14,1),12);  // 1.5 days
            Eq(CalendarAnnualGrant.RoundedCumulativeHours(14,0),0);
            Eq(CalendarAnnualGrant.RoundedCumulativeHours(14,12),112);
            Eq(CalendarAnnualGrant.RoundedCumulativeHours(14,9)-
               CalendarAnnualGrant.RoundedCumulativeHours(14,8),8);
        });
        Check("February hire has separate earned and year-end amounts", () => {
            var r = CalendarAnnualGrant.FirstYearAccrual(new DateTime(2026,2,20),14,new DateTime(2026,10,14));
            Eq(r.MonthsEarned,8); Eq(r.MonthsInYear,11);
            Eq(r.EarnedHours,76); Eq(r.YearEndHours,104);
        });
        Check("future join earns nothing before joining date", () => {
            var r = CalendarAnnualGrant.FirstYearAccrual(new DateTime(2026,10,20),14,new DateTime(2026,10,14));
            Eq(r.MonthsEarned,0); Eq(r.YearEndHours,28);
        });
        Check("monthly accrual excludes unfinished calendar month and rounds cumulative total", () => {
            var joined = new DateTime(2026,2,20);
            Eq(CalendarAnnualGrant.CumulativeEarnedHours(joined,new DateTime(2026,10,14),_ => 14),76);
            Eq(CalendarAnnualGrant.CumulativeEarnedHours(joined,new DateTime(2026,11,3),_ => 14),84);
            Eq(CalendarAnnualGrant.CumulativeEarnedHours(joined,new DateTime(2026,11,30),_ => 14),84);
            Eq(CalendarAnnualGrant.CumulativeEarnedHours(joined,new DateTime(2026,12,1),_ => 14),96);
            Eq(CalendarAnnualGrant.CumulativeEarnedHours(joined,new DateTime(2026,12,31),_ => 14),96);
            Eq(CalendarAnnualGrant.CumulativeEarnedHours(joined,new DateTime(2026,12,31),_ => 14,true),104);
            Eq(CalendarAnnualGrant.CumulativeEarnedHours(new DateTime(2022,2,20),
                new DateTime(2026,10,14),_ => 14),84); // Jan–Sep: 9 completed months
        });
        Check("anniversary increase applies from its month, not earlier months", () => {
            var joined = new DateTime(2022,7,15);
            int Rate(DateTime date) => date >= new DateTime(2026,7,15) ? 20 : 14;
            Eq(CalendarAnnualGrant.CumulativeEarnedHours(joined,new DateTime(2026,6,30),Rate),48);
            Eq(CalendarAnnualGrant.CumulativeEarnedHours(joined,new DateTime(2026,7,14),Rate),56);
            Eq(CalendarAnnualGrant.CumulativeEarnedHours(joined,new DateTime(2026,7,15),Rate),56);
            Eq(CalendarAnnualGrant.CumulativeEarnedHours(joined,new DateTime(2026,7,31),Rate),56);
            Eq(CalendarAnnualGrant.CumulativeEarnedHours(joined,new DateTime(2026,8,1),Rate),72);
        });
        Check("approval cap uses today's earned months, not a future request date", () => {
            var earned = CalendarAnnualGrant.ApprovalEarnedCap(new DateTime(2026,2,20),
                EntitlementCalculator.FirstLevel,EntitlementCalculator.FullTime,true,null,
                new DateTime(2026,1,1),new DateTime(2026,10,14));
            Eq(earned,76);
            Eq(CalendarAnnualGrant.ApprovalEarnedCap(new DateTime(2026,2,20),
                EntitlementCalculator.FirstLevel,EntitlementCalculator.FullTime,true,null,
                new DateTime(2027,1,1),new DateTime(2026,10,14)),0);
            Eq(CalendarAnnualGrant.ApprovalEarnedCap(new DateTime(2026,2,20),
                EntitlementCalculator.FirstLevel,EntitlementCalculator.FullTime,true,null,
                new DateTime(2026,1,1),new DateTime(2026,11,30)),84);
            Eq(CalendarAnnualGrant.ApprovalEarnedCap(new DateTime(2026,2,20),
                EntitlementCalculator.FirstLevel,EntitlementCalculator.FullTime,true,null,
                new DateTime(2026,1,1),new DateTime(2026,12,1)),96);
            Eq(CalendarAnnualGrant.ApprovalEarnedCap(new DateTime(2026,2,20),
                EntitlementCalculator.FirstLevel,EntitlementCalculator.FullTime,true,null,
                new DateTime(2026,1,1),new DateTime(2027,1,1)),104);
        });
        Check("approval cap holds missing and unresolved policy inputs", () => {
            void Denied(Action action) { try { action(); } catch (InvalidOperationException) { return; } throw new Exception("Expected policy hold"); }
            Denied(()=>CalendarAnnualGrant.ApprovalEarnedCap(new DateTime(2026,2,20),null,
                EntitlementCalculator.FullTime,true,null,new DateTime(2026,1,1),asOf));
            Denied(()=>CalendarAnnualGrant.ApprovalEarnedCap(new DateTime(2026,2,20),
                EntitlementCalculator.FirstLevel,EntitlementCalculator.FullTime,true,new DateTime(2026,12,31),
                new DateTime(2026,1,1),asOf));
        });
        CalendarGrantAssessment Assess(DateTime? joined = null, DateTime? leaving = null, bool? active = true)
            => CalendarAnnualGrant.Assess(joined ?? new DateTime(2021,10,11), EntitlementCalculator.FirstLevel,
                EntitlementCalculator.FullTime, active, leaving, asOf);
        Check("calendar period includes both year boundaries", () => { var r=Assess(); Eq(r.PeriodStart,new DateTime(2026,1,1)); Eq(r.PeriodEnd,new DateTime(2026,12,31)); });
        Check("existing complete employee receives assessed full-year rate", () => { var r=Assess(); Eq(r.Annual.Status,"Calculated"); Eq(r.Annual.Hours,128); });
        Check("rate increases on exact anniversary within same calendar period", () => {
            var joined=new DateTime(2024,10,11);
            var before=CalendarAnnualGrant.Assess(joined,EntitlementCalculator.FirstLevel,EntitlementCalculator.FullTime,true,null,asOf);
            var after=CalendarAnnualGrant.Assess(joined,EntitlementCalculator.FirstLevel,EntitlementCalculator.FullTime,true,null,asOf.AddDays(1));
            Eq(before.Annual.Hours,112); Eq(after.Annual.Hours,120); Eq(before.PeriodStart,after.PeriodStart);
        });
        foreach(var date in new[]{new DateTime(2026,1,1),new DateTime(2026,5,1)})
            Check("first-year " + date.ToString("yyyy-MM-dd") + " has no assumed full grant", () => {var r=Assess(date);Eq(r.Annual.Status,"PendingPolicy");Eq(r.Annual.Hours,null);});
        foreach(var date in new[]{new DateTime(2026,3,1),new DateTime(2026,12,31)})
            Check("last-year " + date.ToString("yyyy-MM-dd") + " has no assumed grant", () => {var r=Assess(leaving:date);Eq(r.Annual.Status,"PendingPolicy");Eq(r.Annual.Hours,null);});
        Check("leaving in a later year does not prorate this year", () => Eq(Assess(leaving:new DateTime(2027,1,1)).Annual.Status,"Calculated"));
        Check("previously departed employee receives no new grant", () => Eq(Assess(leaving:new DateTime(2025,12,31)).Annual.Status,"Inactive"));
        Check("inactive employee receives no new grant", () => Eq(Assess(active:false).Annual.Status,"Inactive"));
        Check("missing active status receives no new grant", () => Eq(Assess(active:null).Annual.Status,"PendingHrDetails"));
        Check("missing join date preserves pending HR", () => Eq(CalendarAnnualGrant.Assess(null,EntitlementCalculator.FirstLevel,EntitlementCalculator.FullTime,true,null,asOf).Annual.Status,"PendingHrDetails"));
        Check("missing level preserves pending HR", () => Eq(CalendarAnnualGrant.Assess(new DateTime(2024,1,1),null,EntitlementCalculator.FullTime,true,null,asOf).Annual.Status,"PendingHrDetails"));
        Check("missing employment type preserves pending HR", () => Eq(CalendarAnnualGrant.Assess(new DateTime(2024,1,1),EntitlementCalculator.FirstLevel,null,true,null,asOf).Annual.Status,"PendingHrDetails"));
        Check("leaving before joining is invalid", () => Eq(Assess(leaving:new DateTime(2020,1,1)).Annual.Status,"InvalidDetails"));
        Check("time-bearing leaving date is invalid", () => Eq(Assess(leaving:new DateTime(2027,1,1,1,0,0)).Annual.Status,"InvalidDetails"));
        Check("future join date is invalid", () => Eq(Assess(new DateTime(2027,1,1)).Annual.Status,"InvalidDetails"));
        Check("eight-year reward remains held", () => Eq(Assess(new DateTime(2018,10,10)).Annual.Status,"PendingPolicy"));
        Check("29 February anniversary remains held", () => Eq(Assess(new DateTime(2024,2,29)).Annual.Status,"PendingPolicy"));
        Check("part-time rules remain held", () => Eq(CalendarAnnualGrant.Assess(new DateTime(2024,1,1),EntitlementCalculator.FirstLevel,EntitlementCalculator.PartTime,true,null,asOf).Annual.Status,"PendingPolicy"));
        Check("Malaysia year rolls over at local midnight", () => { var r=CalendarAnnualGrant.Assess(new DateTime(2024,1,1),EntitlementCalculator.FirstLevel,EntitlementCalculator.FullTime,true,null,EntitlementCalculator.MalaysiaDate(new DateTime(2026,12,31,16,0,0,DateTimeKind.Utc))); Eq(r.PeriodStart,new DateTime(2027,1,1)); });
        Console.WriteLine($"{count} calendar grant checks passed; no carry-forward or expiry policy inferred.");
    }
}
