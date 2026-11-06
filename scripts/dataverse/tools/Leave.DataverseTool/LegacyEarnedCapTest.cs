using System.Text.Json;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

internal static partial class Development
{
    static void LegacyEarnedCapTest()
    {
        var path = Root + "/annual-accrual-approval-fixture-2026-10-14.json";
        using var fixture = JsonDocument.Parse(File.ReadAllText(path));
        var employeeId = fixture.RootElement.GetProperty("employeeId").GetGuid();
        var aboveId = fixture.RootElement.GetProperty("requests").GetProperty("above-earned").GetGuid();
        var employee = Client.Retrieve(Employee,employeeId,new ColumnSet("cr2c9_employeename","cr2c9_datejoined","cr2c9_leavingdate"));
        var today = DateTime.UtcNow.AddHours(8).Date;
        var joined = new DateTime(today.Year,2,20);
        if (employee.GetAttributeValue<string>("cr2c9_employeename") != "SYNTHETIC earned annual accrual test" ||
            employee.GetAttributeValue<DateTime>("cr2c9_datejoined") != joined ||
            employee.Contains("cr2c9_leavingdate"))
            throw new Exception("Fictional employee details differ; no legacy overcredit test attempted.");
        var request = Client.Retrieve(Request,aboveId,new ColumnSet("cr2c9_status","cr2c9_employeename"));
        if (request.GetAttributeValue<OptionSetValue>("cr2c9_status")?.Value != 0 ||
            request.GetAttributeValue<EntityReference>("cr2c9_employeename")?.Id != employeeId)
            throw new Exception("The fictional above-earned request is not pending.");
        ExcludeAccrualFixtureFromHrEmail(employeeId);
        var period = Find(BalancePeriodTable,"cr2c9_employee",employeeId).Single();
        int RecordedRemaining() => Find(BalanceEntryTable,"cr2c9_balanceperiod",period.Id,"cr2c9_hours")
            .Sum(e=>e.GetAttributeValue<int>("cr2c9_hours"));
        if (RecordedRemaining() != 76) throw new Exception("Expected synthetic 84-hour grant minus an 8-hour approval.");

        // Reproduce a legacy full-year overcredit only on the fictional record,
        // through the authorized grant API. The original join date is restored
        // even if Dataverse rejects the temporary grant.
        Client.Update(new Entity(Employee,employeeId) {
            ["cr2c9_datejoined"] = new DateTime(today.Year-6,1,1)
        });
        try {
            var grant = Grant(employeeId);
            if ((string)grant["Status"] != "Granted" || (int)grant["CreditedHours"] <= 0)
                throw new Exception("Temporary fictional overcredit was not granted.");
        } finally {
            Client.Update(new Entity(Employee,employeeId) { ["cr2c9_datejoined"] = joined });
        }
        var recorded = RecordedRemaining();
        if (recorded < 88) throw new Exception("Fictional recorded balance is below 88h; this would not isolate the earned cap.");
        var before = Client.Retrieve(Request,aboveId,new ColumnSet("cr2c9_status"));
        Reject("88-hour approval with legacy ledger overcredit",()=>Client.Update(new Entity(Request,aboveId) {
            ["cr2c9_status"] = new OptionSetValue(1),
            ["cr2c9_decisionexpectedversion"] = before.RowVersion,
            ["cr2c9_decisioncomments"] = "SYNTHETIC earned-cap overcredit test; HR email excluded."
        }),"Insufficient earned annual leave balance");
        var after = Client.Retrieve(Request,aboveId,new ColumnSet("cr2c9_status"));
        if (after.RowVersion != before.RowVersion ||
            after.GetAttributeValue<OptionSetValue>("cr2c9_status")?.Value != 0 ||
            Find(History,"cr2c9_leaverequest",aboveId).Length != 0 ||
            Find(BalanceEntryTable,"cr2c9_leaverequest",aboveId).Length != 0 ||
            RecordedRemaining() != recorded)
            throw new Exception("Earned-cap denial changed the request, history, or ledger.");
        Console.WriteLine("PASS live earned cap: 88h denied even though fictional ledger recorded " + recorded +
            "h available; no request, history or ledger change; HR email excluded; employee=" + employeeId);
    }
}
