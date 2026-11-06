using Leave.Plugins;
using Microsoft.Xrm.Sdk;

static class CarryForwardConfirmationChecks
{
    static readonly Guid AnnualType = Guid.Parse("959c93de-1197-4de4-a660-2e2c3266f0fe");

    static Fixture Ready(int sourceHours = 64)
    {
        var f = new Fixture(); f.AsHr();
        Entity Period(int year) {
            var row = new Entity(BalancePersistence.PeriodTable, Guid.NewGuid()) { RowVersion="1" };
            row["cr2c9_employee"] = f.Employee.ToEntityReference();
            row["cr2c9_leavetype"] = new EntityReference("cr2c9_leavetype",AnnualType);
            row["cr2c9_periodstart"] = new DateTime(year,1,1);
            row["cr2c9_periodend"] = new DateTime(year,12,31);
            row["ownerid"] = new EntityReference("systemuser",f.ActorId);
            f.Rows[(row.LogicalName,row.Id)] = row;
            return row;
        }
        var source = Period(2026); Period(2027);
        if (sourceHours > 0) {
            var grant = new Entity(BalancePersistence.EntryTable,Guid.NewGuid());
            grant["cr2c9_balanceperiod"] = source.ToEntityReference();
            grant["cr2c9_operationkey"] = "fixture-earned-grant";
            grant["cr2c9_entrykind"] = new OptionSetValue(290040000);
            grant["cr2c9_hours"] = sourceHours;
            f.Rows[(grant.LogicalName,grant.Id)] = grant;
        }
        return f;
    }

    public static void Run()
    {
        var day = new DateTime(2027,1,10);
        var f = Ready();
        var result = f.ConfirmCarryAt(day);
        var review = f.Rows.Values.Single(x => x.LogicalName == "cr2c9_carryforwardreview");
        var credit = f.Rows.Values.Single(x => x.LogicalName == "cr2c9_carryforwardcredit");
        if ((string)result["Status"] != "Confirmed" || (int)result["SourceUnusedHours"] != 64 ||
            (int)result["CreditedHours"] != 40 || (int)result["SeparateUnusedHours"] != 24 ||
            review.GetAttributeValue<OptionSetValue>("cr2c9_reviewstatus")?.Value != 290040002 ||
            credit.GetAttributeValue<int>("cr2c9_hours") != 40 ||
            credit.GetAttributeValue<DateTime>("cr2c9_useby") != new DateTime(2027,3,31))
            throw new Exception("HR standard confirmation did not save the reviewed 40/24 split.");
        var retry = f.ConfirmCarryAt(day);
        if ((string)retry["Status"] != "AlreadyConfirmed" ||
            f.Rows.Values.Count(x => x.LogicalName == "cr2c9_carryforwardreview") != 1 ||
            f.Rows.Values.Count(x => x.LogicalName == "cr2c9_carryforwardcredit") != 1)
            throw new Exception("A repeat confirmation posted twice.");
        var late = Ready(); late.FailCarryCreditCreate = true;
        try { late.ConfirmCarryAt(day); throw new Exception("Late credit failure was ignored."); }
        catch (InvalidPluginExecutionException) { }
        if (late.Rows.Values.Any(x => x.LogicalName == "cr2c9_carryforwardreview" ||
            x.LogicalName == "cr2c9_carryforwardcredit"))
            throw new Exception("A review persisted after the credit write failed.");
        var zero = Ready(0);
        var zeroResult = zero.ConfirmCarryAt(day);
        if ((string)zeroResult["Status"] != "Confirmed" ||
            zero.Rows.Values.Count(x => x.LogicalName == "cr2c9_carryforwardreview") != 1 ||
            zero.Rows.Values.Any(x => x.LogicalName == "cr2c9_carryforwardcredit"))
            throw new Exception("A zero unused balance created a credit.");
        var closed = Ready();
        try { closed.ConfirmCarryAt(new DateTime(2027,4,1));
            throw new Exception("April confirmation was accepted."); }
        catch (InvalidPluginExecutionException) { }
        Console.WriteLine("PASS carry-forward confirmation core: 5-day cap, separate unused, retry, rollback, zero and March cutoff.");
    }
}
