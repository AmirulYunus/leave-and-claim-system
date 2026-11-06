using Leave.Plugins;
using Microsoft.Xrm.Sdk;

static class CarryForwardPreviewChecks
{
    public static void Run()
    {
        static void Denied(Action action) {
            try { action(); } catch (InvalidPluginExecutionException) { return; }
            throw new Exception("Untrusted carry-forward preview was accepted.");
        }
        var ordinary = new Fixture();
        Denied(() => ordinary.PreviewCarry());
        Denied(() => ordinary.ConfirmCarry());
        var unsupported = new Fixture(); unsupported.AsHr();
        Denied(() => unsupported.PreviewCarry(true));
        Denied(() => unsupported.ConfirmCarry(true));
        var disabled = new Fixture(); disabled.AsHr(); disabled.Actor["isdisabled"] = true;
        Denied(() => disabled.PreviewCarry());
        Denied(() => disabled.ConfirmCarry());
        if (EntitlementCalculator.MalaysiaDate(DateTime.UtcNow).Month > 3) {
            var outsideWindow = new Fixture(); outsideWindow.AsHr();
            Denied(() => outsideWindow.ConfirmCarry());
            if (outsideWindow.Rows.Values.Any(x => x.LogicalName == "cr2c9_carryforwardreview" ||
                x.LogicalName == "cr2c9_carryforwardcredit"))
                throw new Exception("Out-of-window HR confirmation created a carry-forward record.");
        }
        var missing = new Fixture(); missing.AsHr();
        if ((string)missing.PreviewCarry()["Status"] != "NoClosedYearBalance")
            throw new Exception("Missing closed-year period was treated as a grant.");
        var ready = new Fixture(); ready.AsHr();
        var year = EntitlementCalculator.MalaysiaDate(DateTime.UtcNow).Year;
        var annual = Guid.Parse("959c93de-1197-4de4-a660-2e2c3266f0fe");
        Entity Period(int y) {
            var row = new Entity(BalancePersistence.PeriodTable, Guid.NewGuid()) { RowVersion="1" };
            row["cr2c9_employee"] = ready.Employee.ToEntityReference();
            row["cr2c9_leavetype"] = new EntityReference("cr2c9_leavetype",annual);
            row["cr2c9_periodstart"] = new DateTime(y,1,1);
            row["cr2c9_periodend"] = new DateTime(y,12,31);
            row["ownerid"] = new EntityReference("systemuser",ready.ActorId);
            ready.Rows[(row.LogicalName,row.Id)] = row;
            return row;
        }
        var source = Period(year-1); Period(year);
        var grant = new Entity(BalancePersistence.EntryTable,Guid.NewGuid());
        grant["cr2c9_balanceperiod"] = source.ToEntityReference();
        grant["cr2c9_operationkey"] = "test-earned-grant";
        grant["cr2c9_entrykind"] = new OptionSetValue(290040000);
        grant["cr2c9_hours"] = 64;
        ready.Rows[(grant.LogicalName,grant.Id)] = grant;
        var result = ready.PreviewCarry();
        if ((string)result["Status"] != "ReadyForHrReview" ||
            (int)result["SourceUnusedHours"] != 64 ||
            (int)result["StandardLimitHours"] != 40 ||
            (int)result["CandidateCarryHours"] != 40 ||
            (int)result["SeparateUnusedHours"] != 24 ||
            ready.Rows.Values.Any(x => x.LogicalName == "cr2c9_carryforwardcredit"))
            throw new Exception("HR preview changed or miscalculated the balance.");
        Console.WriteLine("PASS carry-forward HR preview: role and input gates, absent year, 5-day cap and read-only result.");
    }
}
