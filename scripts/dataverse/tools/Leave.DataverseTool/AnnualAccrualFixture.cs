using System.Text.Json;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

internal static partial class Development
{
    static void AnnualAccrualFixtureTest()
    {
        var today = DateTime.UtcNow.AddHours(8).Date;
        if (today.Month < 2) throw new Exception("February-join test requires February or later.");
        var path = Root + "/annual-accrual-fixture-2026-10-14.json";
        Guid id;
        if (File.Exists(path)) {
            using var prior = JsonDocument.Parse(File.ReadAllText(path));
            id = prior.RootElement.GetProperty("employeeId").GetGuid();
            var employee = Client.Retrieve(Employee, id, new ColumnSet("cr2c9_employeename", "cr2c9_datejoined"));
            if (employee.GetAttributeValue<string>("cr2c9_employeename") != "SYNTHETIC earned annual accrual test" ||
                employee.GetAttributeValue<DateTime>("cr2c9_datejoined") != new DateTime(today.Year,2,20))
                throw new Exception("Stored fixture identity or join date differs; no grant attempted.");
        } else {
            var who = (Microsoft.Crm.Sdk.Messages.WhoAmIResponse)Client.Execute(new Microsoft.Crm.Sdk.Messages.WhoAmIRequest());
            var employee = new Entity(Employee) {
                ["cr2c9_employeename"] = "SYNTHETIC earned annual accrual test",
                ["cr2c9_email"] = "synthetic-earned-accrual@example.invalid",
                ["cr2c9_entrauserid"] = Guid.NewGuid().ToString("D"),
                ["cr2c9_datejoined"] = new DateTime(today.Year,2,20),
                ["cr2c9_employeelevel"] = new OptionSetValue(290040000),
                ["cr2c9_employmenttype"] = new OptionSetValue(290040000),
                ["cr2c9_isactive"] = true,
                ["ownerid"] = new EntityReference("systemuser",who.UserId)
            };
            id = Client.Create(employee);
            File.WriteAllText(path, JsonSerializer.Serialize(new {employeeId=id, joined=employee["cr2c9_datejoined"], purpose="Synthetic Developer-environment accrued grant and retry test"},new JsonSerializerOptions{WriteIndented=true}));
        }
        var months = today.Month - 1; // February through current month inclusive.
        var expected = checked((int)Math.Ceiling(14m * months * 2 / 12) * 4);
        var first = Grant(id);
        var firstCredited = (int)first["CreditedHours"];
        var firstStatus = (string)first["Status"];
        if (!((firstStatus == "Granted" && firstCredited == expected) ||
              (firstStatus == "AlreadyGranted" && firstCredited == 0)) ||
            (int)first["AvailableHours"] != expected)
            throw new Exception("First accrued grant does not match expected " + expected + " hours: " + firstStatus + ", " + firstCredited);
        var second = Grant(id);
        if ((string)second["Status"] != "AlreadyGranted" ||
            (int)second["CreditedHours"] != 0 || (int)second["AvailableHours"] != expected)
            throw new Exception("Accrued grant retry added credit or changed the balance.");
        var period = Find(BalancePeriodTable,"cr2c9_employee",id,"cr2c9_periodstart","cr2c9_periodend").Single();
        var entries = Find(BalanceEntryTable,"cr2c9_balanceperiod",period.Id,"cr2c9_hours","cr2c9_entrykind");
        if (entries.Length != 1 || entries[0].GetAttributeValue<int>("cr2c9_hours") != expected ||
            entries[0].GetAttributeValue<OptionSetValue>("cr2c9_entrykind")?.Value != 290040000)
            throw new Exception("Synthetic accrued grant ledger differs from the expected single credit.");
        Console.WriteLine("PASS live synthetic February join: earned=" + expected + " hours; first credit=" + firstCredited +
            "; repeat credit=0; one grant entry; employee=" + id + ". No leave request, decision or email was created.");
    }
}
