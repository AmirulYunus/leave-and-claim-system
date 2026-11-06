using System.Text.Json.Nodes;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

internal static partial class Development
{
    static void NotificationFlowInspect()
    {
        var query = new QueryExpression("workflow") {
            ColumnSet = new ColumnSet("name", "category", "statecode", "clientdata"),
            PageInfo = new PagingInfo { Count = 500, PageNumber = 1 }
        };
        query.Criteria.AddCondition("category", ConditionOperator.Equal, 5);
        while (true) {
            var page = Client.RetrieveMultiple(query);
            foreach (var flow in page.Entities.Where(f =>
                (f.GetAttributeValue<string>("name") ?? "").Contains("leave", StringComparison.OrdinalIgnoreCase) ||
                (f.GetAttributeValue<string>("name") ?? "").Contains("employee", StringComparison.OrdinalIgnoreCase))) {
                JsonNode? definition;
                try { definition = JsonNode.Parse(flow.GetAttributeValue<string>("clientdata") ?? "")["properties"]?["definition"]; }
                catch { continue; }
                if (definition == null) continue;
                Console.WriteLine("FLOW " + flow.Id + " " + flow.GetAttributeValue<string>("name") +
                    " state=" + flow.GetAttributeValue<OptionSetValue>("statecode")?.Value);
                foreach (var trigger in definition["triggers"]?.AsObject() ?? new JsonObject())
                    Console.WriteLine("  TRIGGER " + trigger.Key + " " + trigger.Value?["inputs"]?["parameters"]?.ToJsonString());
                foreach (var action in definition["actions"]?.AsObject() ?? new JsonObject())
                    Console.WriteLine("  ACTION " + action.Key + " type=" + action.Value?["type"] +
                        " operation=" + action.Value?["inputs"]?["host"]?["operationId"] +
                        " runAfter=" + action.Value?["runAfter"]?.ToJsonString());
                if (flow.Id == HrNotifyFlow) {
                    Console.WriteLine("  PARAMETERS " + definition["parameters"]?.ToJsonString());
                    Console.WriteLine("  CONNECTIONS " + definition["$connections"]?.ToJsonString());
                    Console.WriteLine("  TRIGGER FULL " + definition["triggers"]?.ToJsonString());
                    Console.WriteLine("  EMAIL ACTION " + definition["actions"]?["Send_an_email_(V2)"]?.ToJsonString());
                    Console.WriteLine("  FLOW CONNECTION REFERENCES " + JsonNode.Parse(flow.GetAttributeValue<string>("clientdata") ?? "{}")?["properties"]?["connectionReferences"]?.ToJsonString());
                }
                if (flow.GetAttributeValue<string>("name") == "Grant calendar annual leave balances") {
                    Console.WriteLine("  LIST ROWS ACTION " + definition["actions"]?["List_rows"]?.ToJsonString());
                    Console.WriteLine("  FLOW CONNECTION REFERENCES " + JsonNode.Parse(flow.GetAttributeValue<string>("clientdata") ?? "{}")?["properties"]?["connectionReferences"]?.ToJsonString());
                }
            }
            if (!page.MoreRecords) break;
            query.PageInfo.PageNumber++;
            query.PageInfo.PagingCookie = page.PagingCookie;
        }
    }
}
