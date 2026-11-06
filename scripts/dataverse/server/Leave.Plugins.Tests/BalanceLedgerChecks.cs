using Leave.Plugins;

static class BalanceLedgerChecks
{
    public static void Run()
    {
        var count = 0;
        void Check(string name, Action test) { test(); count++; Console.WriteLine("PASS balance ledger: " + name); }
        void Eq(object actual, object expected) { if (!Equals(actual, expected)) throw new Exception($"Expected {expected}; got {actual}"); }
        void Denied(Action action) {
            try { action(); } catch (ArgumentException) { return; } catch (InvalidOperationException) { return; }
            throw new Exception("Expected failure.");
        }
        var employee = Guid.NewGuid(); var type = Guid.NewGuid(); var request = Guid.NewGuid();
        var period = new BalancePeriod(Guid.NewGuid(), employee, type, new DateTime(2026,1,1), new DateTime(2026,12,31));
        var next = new BalancePeriod(Guid.NewGuid(), employee, type, new DateTime(2027,1,1), new DateTime(2027,12,31));
        EntitlementRate Rate(int days) => new() { Status = "Calculated", Days = days, Hours = days * 8 };
        BalanceEntry Grant(BalancePeriod p, int hours = 112) => new(p.Id, p.Id + ":grant-fixture", BalanceEntryKind.Grant, hours);
        BalanceEntry[] Deduct(IEnumerable<AnnualLeaveDate> dates, BalanceEntry[] history, params BalancePeriod[] ps)
            => BalanceLedger.PlanApprovedAnnualLeave(employee, type, request, dates, ps.Length == 0 ? new[] { period } : ps, history);
        var twoDays = new[] { new AnnualLeaveDate(new DateTime(2026,10,19),4), new AnnualLeaveDate(new DateTime(2026,10,20),8) };

        Check("first full-period grant adds 112 hours", () => { var r = BalanceLedger.PlanGrant(period, Array.Empty<BalanceEntry>(), Rate(14)); Eq(r.Status,"Ready"); Eq(r.Entry.Hours,112); Eq(r.ExistingAvailableHours,0); });
        Check("Sick Leave grants its own 14/18/22-day bucket and only the service increase", () => {
            var sick=new BalancePeriod(Guid.NewGuid(),employee,Guid.NewGuid(),period.Start,period.End);
            var first=BalanceLedger.PlanGrant(sick,Array.Empty<BalanceEntry>(),Rate(14),"sick").Entry;
            Eq(first.Hours,112);
            if (!first.OperationKey.Contains(":sick-grant:")) throw new Exception("Sick grant key missing.");
            Eq(BalanceLedger.PlanGrant(sick,new[]{first},Rate(14),"sick").Entry,null);
            var used=new BalanceEntry(sick.Id,"sick-approved",BalanceEntryKind.ApprovedLeave,-8,Guid.NewGuid());
            var increase=BalanceLedger.PlanGrant(sick,new[]{first,used},Rate(18),"sick").Entry;
            Eq(increase.Hours,32);
            Eq(BalanceLedger.AvailableHours(sick,new[]{first,used,increase}),136);
        });
        Check("Hospitalisation grant, approval and cancellation are exact and idempotent", () => {
            var hospitalType=Guid.NewGuid(); var hospitalRequest=Guid.NewGuid();
            var hospital=new BalancePeriod(Guid.NewGuid(),employee,hospitalType,period.Start,period.End);
            var grant=BalanceLedger.PlanGrant(hospital,Array.Empty<BalanceEntry>(),Rate(60),"hospitalisation").Entry;
            Eq(grant.Hours,480);
            if (!grant.OperationKey.Contains(":hospitalisation-grant:")) throw new Exception("Hospitalisation grant key missing.");
            Eq(BalanceLedger.PlanGrant(hospital,new[]{grant},Rate(60),"hospitalisation").Entry,null);
            var day=new[]{new AnnualLeaveDate(new DateTime(2026,10,19),8)};
            var debit=BalanceLedger.PlanApprovedAnnualLeave(employee,hospitalType,hospitalRequest,day,
                new[]{hospital},new[]{grant}).Single();
            Eq(debit.Hours,-8);
            Eq(BalanceLedger.AvailableHours(hospital,new[]{grant,debit}),472);
            Eq(BalanceLedger.PlanApprovedAnnualLeave(employee,hospitalType,hospitalRequest,day,
                new[]{hospital},new[]{grant,debit}).Length,0);
            var reversal=BalanceLedger.PlanExactAnnualReversal(employee,hospitalType,hospitalRequest,
                Guid.NewGuid(),day,new[]{hospital},new[]{grant,debit}).Single();
            Eq(reversal.Hours,8);
            Eq(BalanceLedger.AvailableHours(hospital,new[]{grant,debit,reversal}),480);
            Denied(()=>BalanceLedger.PlanExactAnnualReversal(employee,hospitalType,hospitalRequest,
                Guid.NewGuid(),day,new[]{hospital},new[]{grant,debit,reversal}));
        });
        Check("earned grant adds only cumulative positive delta", () => {
            var initial=BalanceLedger.PlanEarnedGrant(period,Array.Empty<BalanceEntry>(),84).Entry;
            Eq(initial.Hours,84);
            Eq(BalanceLedger.PlanEarnedGrant(period,new[]{initial},84).Status,"AlreadyGranted");
            var later=BalanceLedger.PlanEarnedGrant(period,new[]{initial},104).Entry;
            Eq(later.Hours,20);
            Eq(BalanceLedger.PlanEarnedGrant(period,new[]{initial,later},104).Entry,null);
        });
        Check("legacy full-year credit is preserved for review", () => {
            var plan=BalanceLedger.PlanEarnedGrant(period,new[]{Grant(period,112)},84);
            Eq(plan.Status,"PendingAdjustment"); Eq(plan.Entry,null);
        });
        Check("repeat grant does not add twice", () => { var first = BalanceLedger.PlanGrant(period,Array.Empty<BalanceEntry>(),Rate(14)).Entry; var retry = BalanceLedger.PlanGrant(period,new[]{first},Rate(14)); Eq(retry.Status,"AlreadyGranted"); Eq(retry.Entry,null); Eq(retry.ExistingAvailableHours,112); });
        Check("mixed half/full day approval deducts 12 hours", () => { var entry = Deduct(twoDays,new[]{Grant(period)}).Single(); Eq(entry.Hours,-12); Eq(entry.RequestId,request); Eq(BalanceLedger.AvailableHours(period,new[]{Grant(period),entry}),100); });
        Check("grant rerun preserves leave already consumed", () => { var original = Grant(period); var debit = Deduct(twoDays,new[]{original}).Single(); var retry = BalanceLedger.PlanGrant(period,new[]{original,debit},Rate(14)); Eq(retry.Entry,null); Eq(retry.ExistingAvailableHours,100); });
        Check("anniversary increase credits only eight-hour difference", () => { var original = Grant(period); var debit = Deduct(twoDays,new[]{original}).Single(); var update = BalanceLedger.PlanGrant(period,new[]{original,debit},Rate(15)); Eq(update.Entry.Hours,8); Eq(BalanceLedger.AvailableHours(period,new[]{original,debit,update.Entry}),108); Eq(BalanceLedger.PlanGrant(period,new[]{original,debit,update.Entry},Rate(15)).Entry,null); });
        foreach (var status in new[]{"PendingHrDetails","PendingPolicy","InvalidDetails"})
            Check(status + " preserves existing balance", () => { var original=Grant(period); var debit=Deduct(twoDays,new[]{original}).Single(); var plan=BalanceLedger.PlanGrant(period,new[]{original,debit},new EntitlementRate {Status=status,Reason="Unavailable",Days=null,Hours=null}); Eq(plan.Status,status); Eq(plan.Entry,null); Eq(plan.ExistingAvailableHours,100); });
        Check("null rate preserves granted hours", () => { var result = BalanceLedger.PlanGrant(period,new[]{Grant(period)},null); Eq(result.Status,"PendingHrDetails"); Eq(result.Entry,null); Eq(result.ExistingAvailableHours,112); });
        Check("lower rate does not claw back a grant", () => { var r=BalanceLedger.PlanGrant(period,new[]{Grant(period,128)},Rate(14)); Eq(r.Status,"PendingAdjustment"); Eq(r.Entry,null); Eq(r.ExistingAvailableHours,128); });
        Check("exact approval retry has no second debit", () => { var original=Grant(period); var debit=Deduct(twoDays,new[]{original}).Single(); Eq(Deduct(twoDays,new[]{original,debit}).Length,0); });
        Check("changed approval amount conflicts with prior deduction", () => { var original=Grant(period); var debit=Deduct(twoDays,new[]{original}).Single(); Denied(()=>Deduct(new[]{twoDays[1]},new[]{original,debit})); });
        Check("approval without a grant is denied", () => Denied(()=>Deduct(twoDays,Array.Empty<BalanceEntry>())));
        Check("insufficient approval has no mutation", () => { var history=new[]{Grant(period,8)}; Denied(()=>Deduct(twoDays,history)); Eq(history.Length,1); Eq(BalanceLedger.AvailableHours(period,history),8); });
        Check("exact available amount can be consumed", () => { var history=new[]{Grant(period,12)}; var debit=Deduct(twoDays,history).Single(); Eq(BalanceLedger.AvailableHours(period,history.Append(debit)),0); });
        Check("recorded full-year grant cannot bypass earned cap", () => {
            var grant=Grant(period,104);
            var alreadyUsed=new BalanceEntry(period.Id,"earlier-leave",BalanceEntryKind.ApprovedLeave,-80,Guid.NewGuid());
            var history=new[]{grant,alreadyUsed};
            Eq(BalanceLedger.AvailableHours(period,history),24);
            Eq(BalanceLedger.EarnedAvailableHours(period,history,84),4);
            Denied(()=>BalanceLedger.PlanApprovedAnnualLeaveCapped(employee,type,request,
                new[]{new AnnualLeaveDate(new DateTime(2026,10,21),8)},new[]{period},history,
                new Dictionary<Guid,int>{{period.Id,84}}));
            var half=BalanceLedger.PlanApprovedAnnualLeaveCapped(employee,type,request,
                new[]{new AnnualLeaveDate(new DateTime(2026,10,21),4)},new[]{period},history,
                new Dictionary<Guid,int>{{period.Id,84}}).Single();
            Eq(half.Hours,-4);
        });
        Check("cross-year leave splits by date", () => { var dates=new[]{new AnnualLeaveDate(new DateTime(2026,12,31),4),new AnnualLeaveDate(new DateTime(2027,1,4),8)}; var debits=Deduct(dates,new[]{Grant(period),Grant(next)},period,next); Eq(debits.Length,2); Eq(debits.Single(x=>x.PeriodId==period.Id).Hours,-4); Eq(debits.Single(x=>x.PeriodId==next.Id).Hours,-8); });
        Check("cross-year insufficiency returns no partial plan", () => { var dates=new[]{new AnnualLeaveDate(new DateTime(2026,12,31),4),new AnnualLeaveDate(new DateTime(2027,1,4),8)}; var history=new[]{Grant(period),Grant(next,4)}; Denied(()=>Deduct(dates,history,period,next)); Eq(BalanceLedger.AvailableHours(period,history.Where(x=>x.PeriodId==period.Id)),112); });
        Check("explicit service-year period is supported without a reset assumption", () => { var servicePeriod=new BalancePeriod(Guid.NewGuid(),employee,type,new DateTime(2026,10,9),new DateTime(2027,10,8)); var debit=Deduct(new[]{new AnnualLeaveDate(new DateTime(2027,1,4),8)},new[]{Grant(servicePeriod)},servicePeriod).Single(); Eq(debit.PeriodId,servicePeriod.Id); });
        Check("uncovered date is denied", () => Denied(()=>Deduct(new[]{new AnnualLeaveDate(new DateTime(2027,1,4),8)},new[]{Grant(period)})));
        Check("duplicate date cannot deduct twice", () => Denied(()=>Deduct(new[]{twoDays[0],twoDays[0]},new[]{Grant(period)})));
        Check("missing per-date durations are denied", () => Denied(()=>Deduct(Array.Empty<AnnualLeaveDate>(),new[]{Grant(period)})));
        Check("overlapping periods are denied", () => { var overlap=new BalancePeriod(Guid.NewGuid(),employee,type,new DateTime(2026,10,1),new DateTime(2027,9,30)); Denied(()=>Deduct(twoDays,new[]{Grant(period),Grant(overlap)},period,overlap)); });
        Check("another employee's balance is denied", () => { var other=new BalancePeriod(Guid.NewGuid(),Guid.NewGuid(),type,period.Start,period.End); Denied(()=>Deduct(twoDays,new[]{Grant(other)},other)); });
        Check("another leave type's balance is denied", () => { var other=new BalancePeriod(Guid.NewGuid(),employee,Guid.NewGuid(),period.Start,period.End); Denied(()=>Deduct(twoDays,new[]{Grant(other)},other)); });
        Check("foreign ledger entry is denied", () => Denied(()=>Deduct(twoDays,new[]{Grant(next)})));
        Check("duplicate operation history is denied", () => { var grant=Grant(period); Denied(()=>BalanceLedger.AvailableHours(period,new[]{grant,grant})); });
        Check("two deductions for one request are denied", () => { var d1=new BalanceEntry(period.Id,"one",BalanceEntryKind.ApprovedLeave,-4,request); var d2=new BalanceEntry(period.Id,"two",BalanceEntryKind.ApprovedLeave,-8,request); Denied(()=>BalanceLedger.AvailableHours(period,new[]{Grant(period),d1,d2})); });
        Check("reversal restores exact original hours", () => { var grant=Grant(period); var debit=Deduct(twoDays,new[]{grant}).Single(); var reversal=new BalanceEntry(period.Id,"cancel",BalanceEntryKind.Reversal,12,request,debit.OperationKey); Eq(BalanceLedger.AvailableHours(period,new[]{grant,debit,reversal}),112); Denied(()=>Deduct(twoDays,new[]{grant,debit,reversal})); });
        Check("cancellation plan restores only the committed debit", () => { var grant=Grant(period); var debit=Deduct(twoDays,new[]{grant}).Single(); var cancellation=Guid.NewGuid(); var plan=BalanceLedger.PlanExactAnnualReversal(employee,type,request,cancellation,twoDays,new[]{period},new[]{grant,debit}); Eq(plan.Length,1); Eq(plan[0].Hours,12); Eq(plan[0].Reverses,debit.OperationKey); Eq(BalanceLedger.AvailableHours(period,new[]{grant,debit,plan[0]}),112); });
        Check("cancellation plan refuses an undeducted request", () => Denied(()=>BalanceLedger.PlanExactAnnualReversal(employee,type,request,Guid.NewGuid(),twoDays,new[]{period},new[]{Grant(period)})));
        Check("cancellation plan refuses a changed approved amount", () => { var grant=Grant(period); var debit=new BalanceEntry(period.Id,"old-debit",BalanceEntryKind.ApprovedLeave,-8,request); Denied(()=>BalanceLedger.PlanExactAnnualReversal(employee,type,request,Guid.NewGuid(),twoDays,new[]{period},new[]{grant,debit})); });
        Check("cancellation plan refuses a second reversal", () => { var grant=Grant(period); var debit=Deduct(twoDays,new[]{grant}).Single(); var reversal=BalanceLedger.PlanExactAnnualReversal(employee,type,request,Guid.NewGuid(),twoDays,new[]{period},new[]{grant,debit}).Single(); Denied(()=>BalanceLedger.PlanExactAnnualReversal(employee,type,request,Guid.NewGuid(),twoDays,new[]{period},new[]{grant,debit,reversal})); });
        Check("reversal cannot use a new entitlement amount", () => { var grant=Grant(period); var debit=Deduct(twoDays,new[]{grant}).Single(); var reversal=new BalanceEntry(period.Id,"cancel",BalanceEntryKind.Reversal,8,request,debit.OperationKey); Denied(()=>BalanceLedger.AvailableHours(period,new[]{grant,debit,reversal})); });
        Check("duplicate reversal is denied", () => { var grant=Grant(period); var debit=Deduct(twoDays,new[]{grant}).Single(); var r1=new BalanceEntry(period.Id,"cancel-one",BalanceEntryKind.Reversal,12,request,debit.OperationKey); var r2=new BalanceEntry(period.Id,"cancel-two",BalanceEntryKind.Reversal,12,request,debit.OperationKey); Denied(()=>BalanceLedger.AvailableHours(period,new[]{grant,debit,r1,r2})); });
        Check("orphan reversal is denied", () => Denied(()=>BalanceLedger.AvailableHours(period,new[]{Grant(period),new BalanceEntry(period.Id,"cancel",BalanceEntryKind.Reversal,12,request,"absent")})));
        Check("overdrawn imported history is denied", () => Denied(()=>BalanceLedger.AvailableHours(period,new[]{Grant(period,4),new BalanceEntry(period.Id,"debit",BalanceEntryKind.ApprovedLeave,-8,request)})));
        Check("unsupported annual duration and time-bearing date are denied", () => { Denied(()=>new AnnualLeaveDate(new DateTime(2026,10,19),6)); Denied(()=>new AnnualLeaveDate(new DateTime(2026,10,19,1,0,0),4)); });
        Check("inconsistent day/hour entitlement is denied", () => Denied(()=>BalanceLedger.PlanGrant(period,Array.Empty<BalanceEntry>(),new EntitlementRate{Status="Calculated",Days=14,Hours=120})));
        Console.WriteLine($"{count} balance accounting checks passed. Policy periods, calendars, transaction locking and persistence require a Dataverse adapter; this core is not deployed.");
    }
}
