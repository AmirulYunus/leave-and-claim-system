using Leave.Plugins;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

internal static partial class Development {
    static void SickGrantLiveAudit() {
        var today=EntitlementCalculator.MalaysiaDate(DateTime.UtcNow);
        var employeeId=Guid.Parse("6938b1f2-22c2-f111-aaad-000d3a826b69");
        var employee=Client.Retrieve(Employee,employeeId,new ColumnSet(
            "cr2c9_employeename","cr2c9_email","cr2c9_datejoined","cr2c9_employmenttype",
            "cr2c9_isactive","cr2c9_leavingdate","statecode"));
        if(employee.GetAttributeValue<string>("cr2c9_employeename")!="Muhammad Amirul" ||
           employee.GetAttributeValue<string>("cr2c9_email")!="Muhammad.Amirul@Asteriskdigital.com.my" ||
           !employee.GetAttributeValue<bool>("cr2c9_isactive") ||
           employee.GetAttributeValue<OptionSetValue>("statecode")?.Value!=0)
            throw new Exception("Expected active real Employee profile was not found.");
        var rate=EntitlementCalculator.Calculate(
            employee.GetAttributeValue<DateTime?>("cr2c9_datejoined"),null,
            employee.GetAttributeValue<OptionSetValue>("cr2c9_employmenttype")?.Value,today).Sick;
        if(rate.Status!="Calculated" || !rate.Hours.HasValue)
            throw new Exception("Sick allowance is held for HR details or policy: "+rate.Status+" "+rate.Reason);
        if(employee.GetAttributeValue<DateTime?>("cr2c9_leavingdate") is DateTime leaving && leaving<today)
            throw new Exception("Employee has departed; no live Sick grant assertion made.");
        var start=new DateTime(today.Year,1,1);
        var end=new DateTime(today.Year,12,31);
        var periods=Find(BalancePeriodTable,"cr2c9_employee",employeeId,
            "cr2c9_leavetype","cr2c9_periodstart","cr2c9_periodend")
            .Where(x=>x.GetAttributeValue<EntityReference>("cr2c9_leavetype")?.Id==SickLeaveType &&
                x.GetAttributeValue<DateTime>("cr2c9_periodstart")<=end &&
                x.GetAttributeValue<DateTime>("cr2c9_periodend")>=start).ToArray();
        if(periods.Length!=1 || periods[0].GetAttributeValue<DateTime>("cr2c9_periodstart")!=start ||
           periods[0].GetAttributeValue<DateTime>("cr2c9_periodend")!=end)
            throw new Exception("Expected one exact calendar-year Sick balance period.");
        var entries=Find(BalanceEntryTable,"cr2c9_balanceperiod",periods[0].Id,
            "cr2c9_hours","cr2c9_entrykind","cr2c9_operationkey","createdon");
        if(entries.Any(x=>string.IsNullOrWhiteSpace(x.GetAttributeValue<string>("cr2c9_operationkey"))) ||
           entries.GroupBy(x=>x.GetAttributeValue<string>("cr2c9_operationkey"),StringComparer.Ordinal)
               .Any(x=>x.Count()!=1))
            throw new Exception("Sick ledger has a missing or duplicate operation key.");
        var grants=entries.Where(x=>x.GetAttributeValue<OptionSetValue>("cr2c9_entrykind")?.Value==290040000).ToArray();
        var granted=grants.Sum(x=>x.GetAttributeValue<int>("cr2c9_hours"));
        var available=entries.Sum(x=>x.GetAttributeValue<int>("cr2c9_hours"));
        if(grants.Length!=1 || granted!=rate.Hours.Value || available!=rate.Hours.Value || entries.Length!=1)
            throw new Exception($"Sick ledger differs: rate={rate.Hours}h, grants={grants.Length}, granted={granted}h, available={available}h, entries={entries.Length}.");
        Console.WriteLine($"PASS real Employee Sick ledger: {rate.Days} days = {rate.Hours}h; one calendar period, one grant entry, no debit/reversal, available {available}h; grant created UTC {grants[0].GetAttributeValue<DateTime>("createdon"):yyyy-MM-dd HH:mm:ss}; operation {grants[0].GetAttributeValue<string>("cr2c9_operationkey")}. Read-only audit.");
    }
}
