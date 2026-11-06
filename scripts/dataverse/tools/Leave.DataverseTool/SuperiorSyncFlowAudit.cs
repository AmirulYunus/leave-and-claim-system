using System.Text.Json.Nodes;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

internal static partial class Development
{
    static void SuperiorSyncFlowAudit()
    {
        var flows = Find("workflow", "name", "Sync Employees from Microsoft 365",
            "name", "statecode", "category", "clientdata");
        if (flows.Length != 1) throw new Exception("Expected one employee sync flow, found " + flows.Length);
        var flow = flows[0];
        if (flow.GetAttributeValue<string>("name") != "Sync Employees from Microsoft 365" ||
            flow.GetAttributeValue<OptionSetValue>("category")?.Value != 5)
            throw new Exception("Unexpected employee sync flow identity.");
        Console.WriteLine("FLOW state=" + flow.GetAttributeValue<OptionSetValue>("statecode")?.Value);
        var definition = JsonNode.Parse(flow.GetAttributeValue<string>("clientdata") ?? "")?
            ["properties"]?["definition"] ?? throw new Exception("Flow definition missing.");
        static void Walk(JsonNode? actions, string parent)
        {
            if (actions is not JsonObject group) return;
            foreach (var pair in group) {
                var action = pair.Value;
                var path = parent.Length == 0 ? pair.Key : parent + "/" + pair.Key;
                var after = action?["runAfter"] is JsonObject r
                    ? string.Join(";", r.Select(x => x.Key + "=" + string.Join(",", x.Value?.AsArray().Select(v => v?.ToString()) ?? [])))
                    : "";
                Console.WriteLine(path + " type=" + action?["type"] + " runAfter=" + after);
                Walk(action?["actions"], path);
                Walk(action?["else"]?["actions"], path + "/else");
                if (action?["cases"] is JsonObject cases)
                    foreach (var choice in cases) Walk(choice.Value?["actions"], path + "/case:" + choice.Key);
                Walk(action?["default"]?["actions"], path + "/default");
            }
        }
        Walk(definition["actions"], "");
        var superiorActions = definition["actions"]?["Sync_Superiors"]?["actions"];
        void Detail(string label, JsonNode? action)
        {
            if (action == null) throw new Exception("Missing sync action " + label);
            Console.WriteLine("DETAIL " + label + " foreach=" + action["foreach"] +
                " expression=" + action["expression"]?.ToJsonString() +
                " parameters=" + action["inputs"]?["parameters"]?.ToJsonString());
        }
        Detail("manager lookup", superiorActions?["Get_manager_(V2)"]);
        Detail("manager failure branch", superiorActions?["Manager_Not_Found"]);
        Detail("find superior", superiorActions?["Find_Superior_Employee"]);
        Detail("superior found", superiorActions?["Condition_5"]);
        Detail("current employee", superiorActions?["Condition_5"]?["actions"]?["Find_Current_Employee"]);
        Detail("employee changed", superiorActions?["Condition_5"]?["actions"]?["Condition_6"]);
        Detail("update superior", superiorActions?["Condition_5"]?["actions"]?["Condition_6"]?["actions"]?["Update_a_row_3"]);
        Detail("clear condition", superiorActions?["Manager_Not_Found"]?["actions"]?["Condition_7"]);
        Detail("clear superior", superiorActions?["Manager_Not_Found"]?["actions"]?["Condition_7"]?["actions"]?["Clear_Employee_Superior"]);
    }
}
