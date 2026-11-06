using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Crm.Sdk.Messages;

internal static partial class Development
{
    static void RequestEditDiagnostic()
    {
        var who = (WhoAmIResponse)Client.Execute(new WhoAmIRequest());
        var me = Client.Retrieve("systemuser", who.UserId,
            new ColumnSet("fullname", "internalemailaddress", "azureactivedirectoryobjectid", "isdisabled"));
        Console.WriteLine("Caller: " + me.GetAttributeValue<string>("fullname") +
            "; AAD present=" + (me.GetAttributeValue<Guid>("azureactivedirectoryobjectid") != Guid.Empty) +
            "; disabled=" + me.GetAttributeValue<bool>("isdisabled"));
        var q = new QueryExpression("plugintracelog") {
            ColumnSet = new ColumnSet("createdon", "messageblock", "exceptiondetails", "typename", "correlationid"),
            TopCount = 12,
            Orders = { new OrderExpression("createdon", OrderType.Descending) }
        };
        q.Criteria.AddCondition("createdon", ConditionOperator.LastXDays, 2);
        foreach (var row in Client.RetrieveMultiple(q).Entities) {
            var details = row.GetAttributeValue<string>("exceptiondetails") ?? "";
            var block = row.GetAttributeValue<string>("messageblock") ?? "";
            if (!details.Contains("Entra identity", StringComparison.OrdinalIgnoreCase) &&
                !block.Contains("Entra identity", StringComparison.OrdinalIgnoreCase)) continue;
            Console.WriteLine("Trace " + row.GetAttributeValue<DateTime>("createdon").ToString("u") +
                " " + row.GetAttributeValue<string>("typename") + " " + row.Id);
            Console.WriteLine((details + "\n" + block).Substring(0, Math.Min(2400, details.Length + block.Length + 1)));
        }
        var rq = new QueryExpression(Request) { ColumnSet = new ColumnSet("cr2c9_leaverequestnumber", "cr2c9_status", "cr2c9_employeename", "cr2c9_startdate", "cr2c9_enddate", "cr2c9_hoursperday") };
        rq.Criteria.AddCondition("cr2c9_leaverequestnumber", ConditionOperator.Equal, "LR-0075");
        foreach (var request in Client.RetrieveMultiple(rq).Entities) {
            var employee = request.GetAttributeValue<EntityReference>("cr2c9_employeename");
            Console.WriteLine("LR-0075: status=" + request.GetAttributeValue<OptionSetValue>("cr2c9_status")?.Value +
                "; employee=" + employee?.Id + "; dates=" + request.GetAttributeValue<DateTime>("cr2c9_startdate").ToString("yyyy-MM-dd") +
                ".." + request.GetAttributeValue<DateTime>("cr2c9_enddate").ToString("yyyy-MM-dd") +
                "; hours=" + request.GetAttributeValue<decimal>("cr2c9_hoursperday"));
            var dates = new QueryExpression("cr2c9_leaverequestdate") {
                ColumnSet = new ColumnSet("cr2c9_leavedate", "cr2c9_duration")
            };
            dates.Criteria.AddCondition("cr2c9_leaverequest", ConditionOperator.Equal, request.Id);
            dates.Orders.Add(new OrderExpression("cr2c9_leavedate", OrderType.Ascending));
            foreach (var date in Client.RetrieveMultiple(dates).Entities)
                Console.WriteLine("  date=" + date.GetAttributeValue<DateTime>("cr2c9_leavedate").ToString("yyyy-MM-dd") +
                    "; duration=" + date.GetAttributeValue<OptionSetValue>("cr2c9_duration")?.Value);
        }
    }
    static void RequestEditProbe()
    {
        var q = new QueryExpression(Request) { ColumnSet = new ColumnSet("cr2c9_leaverequestnumber", "cr2c9_status", "cr2c9_hoursperday") };
        q.Criteria.AddCondition("cr2c9_leaverequestnumber", ConditionOperator.Equal, "LR-0075");
        var row = Client.RetrieveMultiple(q).Entities.Single();
        if (row.GetAttributeValue<OptionSetValue>("cr2c9_status")?.Value != 0 ||
            row.GetAttributeValue<decimal>("cr2c9_hoursperday") != 8m)
            throw new Exception("Fixture has changed; no probe run.");
        Client.Update(new Entity(Request, row.Id) { ["cr2c9_hoursperday"] = 8m });
        Console.WriteLine("PASS no-op pending edit on LR-0075.");
    }
}
