using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

internal static partial class Development
{
    // Read-only inspection for a developer-account cancellation walkthrough.
    static void CancellationCandidates()
    {
        var ownId = Guid.Parse("6938b1f2-22c2-f111-aaad-000d3a826b69");
        var actor = Client.Retrieve("systemuser", ((WhoAmIResponse)Client.Execute(new WhoAmIRequest())).UserId,
            new ColumnSet("azureactivedirectoryobjectid"));
        var identity = actor.GetAttributeValue<Guid>("azureactivedirectoryobjectid").ToString("D");
        var employee = Client.Retrieve(Employee, ownId,
            new ColumnSet("cr2c9_entrauserid", "cr2c9_superior", "cr2c9_employeename"));
        if (employee.GetAttributeValue<string>("cr2c9_entrauserid") != identity)
            throw new Exception("Signed-in user no longer matches the expected Employee row.");

        var superior = employee.GetAttributeValue<EntityReference>("cr2c9_superior");
        Console.WriteLine("Own Employee=" + ownId + "; Superior=" + (superior?.Id.ToString() ?? "none"));
        if (superior != null)
        {
            var manager = Client.Retrieve(Employee, superior.Id,
                new ColumnSet("cr2c9_employeename", "cr2c9_entrauserid"));
            var managerIdentity = manager.GetAttributeValue<string>("cr2c9_entrauserid");
            Console.WriteLine("Superior name=" + manager.GetAttributeValue<string>("cr2c9_employeename") +
                "; Entra ID set=" + Guid.TryParse(managerIdentity, out var managerGuid));
            if (Guid.TryParse(managerIdentity, out managerGuid))
            {
                var users = Find("systemuser", "azureactivedirectoryobjectid", managerGuid,
                    "fullname", "isdisabled");
                Console.WriteLine("Matching Superior Dataverse users=" + users.Length +
                    (users.Length == 1 ? "; enabled=" + !users[0].GetAttributeValue<bool>("isdisabled") : ""));
            }
        }

        var annualId = Guid.Parse("959c93de-1197-4de4-a660-2e2c3266f0fe");
        var number = Meta(Request).PrimaryNameAttribute;
        var query = new QueryExpression(Request) {
            ColumnSet = new ColumnSet(number, "cr2c9_startdate", "cr2c9_enddate", "cr2c9_status", "cr2c9_leavetypename")
        };
        query.Criteria.AddCondition("cr2c9_employeename", ConditionOperator.Equal, ownId);
        query.Criteria.AddCondition("cr2c9_startdate", ConditionOperator.OnOrAfter, DateTime.Today);
        query.AddOrder("cr2c9_startdate", OrderType.Ascending);
        var requests = Client.RetrieveMultiple(query).Entities;
        foreach (var request in requests.Where(r => r.GetAttributeValue<EntityReference>("cr2c9_leavetypename")?.Id == annualId))
        {
            var cancellations = Find(Cancellation, "cr2c9_leaverequest", request.Id, "cr2c9_status");
            Console.WriteLine("Request=" + request.GetAttributeValue<string>(number) +
                "; id=" + request.Id +
                "; dates=" + request.GetAttributeValue<DateTime>("cr2c9_startdate").ToString("yyyy-MM-dd") +
                ".." + request.GetAttributeValue<DateTime>("cr2c9_enddate").ToString("yyyy-MM-dd") +
                "; status=" + request.GetAttributeValue<OptionSetValue>("cr2c9_status")?.Value +
                "; cancellations=" + string.Join(",", cancellations.Select(c => c.GetAttributeValue<OptionSetValue>("cr2c9_status")?.Value)));
        }

        var periods = Find(BalancePeriodTable, "cr2c9_employee", ownId,
            "cr2c9_periodstart", "cr2c9_periodend");
        if (periods.Length == 0) Console.WriteLine("No annual balance period exists for this Employee.");
        foreach (var period in periods)
        {
            var hours = Find(BalanceEntryTable, "cr2c9_balanceperiod", period.Id, "cr2c9_hours")
                .Sum(e => e.GetAttributeValue<int>("cr2c9_hours"));
            Console.WriteLine("Balance period=" + period.GetAttributeValue<DateTime>("cr2c9_periodstart").ToString("yyyy-MM-dd") +
                "; available hours=" + hours);
        }
    }
}
