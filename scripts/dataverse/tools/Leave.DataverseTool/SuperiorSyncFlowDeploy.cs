using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

internal static partial class Development
{
    static void SuperiorSyncFlowDeploy()
    {
        var flows = Find("workflow", "name", "Sync Employees from Microsoft 365",
            "name", "statecode", "category", "clientdata");
        if (flows.Length != 1 || flows[0].GetAttributeValue<OptionSetValue>("category")?.Value != 5)
            throw new InvalidOperationException("Expected exactly one employee sync cloud flow.");
        var flow = flows[0];
        var original = flow.GetAttributeValue<string>("clientdata") ?? throw new InvalidOperationException("Flow has no definition.");
        var json = JsonNode.Parse(original) ?? throw new InvalidOperationException("Invalid flow JSON.");
        var actions = json["properties"]?["definition"]?["actions"]?.AsObject()
            ?? throw new InvalidOperationException("Flow actions missing.");
        if (actions.ContainsKey("Initialize_Superior_Sync_Errors") || actions.ContainsKey("Check_Superior_Sync_Errors"))
            throw new InvalidOperationException("Flow already has sync error reporting; inspect before changing it.");
        var loop = actions["Sync_Superiors"] ?? throw new InvalidOperationException("Superior sync loop missing.");
        if (loop["runAfter"]?["Apply_to_each"]?.AsArray().SingleOrDefault()?.ToString() != "Succeeded")
            throw new InvalidOperationException("Unexpected sync loop dependency.");
        var body = loop["actions"]?.AsObject() ?? throw new InvalidOperationException("Sync loop body missing.");
        foreach (var name in new[] { "Get_manager_(V2)", "Find_Superior_Employee", "Condition_5",
            "Manager_Not_Found", "Superior_Sync_Complete" })
            if (!body.ContainsKey(name)) throw new InvalidOperationException("Sync action missing: " + name);

        // The variable is global to a flow run. Power Automate runs Apply to each sequentially by default;
        // fix the concurrency explicitly so every failed employee is recorded reliably.
        if (loop["runtimeConfiguration"] == null) loop["runtimeConfiguration"] = new JsonObject();
        loop["runtimeConfiguration"]!["concurrency"] = JsonNode.Parse("{\"repetitions\":1}");
        actions["Initialize_Superior_Sync_Errors"] = JsonNode.Parse("""
            {"type":"InitializeVariable","inputs":{"variables":[{"name":"SuperiorSyncErrors","type":"Array","value":[]}]},"runAfter":{"Apply_to_each":["Succeeded"]}}
            """);
        loop["runAfter"] = JsonNode.Parse("{\"Initialize_Superior_Sync_Errors\":[\"Succeeded\"]}");

        static JsonNode Append(string message, string? after = null)
        {
            var action = new JsonObject {
                ["type"] = "AppendToArrayVariable",
                ["inputs"] = new JsonObject { ["name"] = "SuperiorSyncErrors", ["value"] = message }
            };
            if (after != null)
                action["runAfter"] = new JsonObject { [after] = new JsonArray("Failed", "TimedOut") };
            return action;
        }
        static JsonObject ElseActions(JsonNode condition)
        {
            if (condition["else"] == null) condition["else"] = new JsonObject();
            if (condition["else"]!["actions"] == null) condition["else"]!["actions"] = new JsonObject();
            return condition["else"]!["actions"]!.AsObject();
        }
        static string Item(string reason) =>
            "@concat('Superior sync: " + reason + " for Microsoft 365 user ', string(items('Sync_Superiors')?['Id']))";

        var managerMissing = body["Manager_Not_Found"]!;
        managerMissing["runAfter"] = JsonNode.Parse("{\"Get_manager_(V2)\":[\"Failed\",\"TimedOut\"]}");
        ElseActions(managerMissing)["Record_Manager_Lookup_Error"] = Append(Item("manager lookup failed (not a confirmed 404)"));
        var managerFound = body["Condition_5"]!;
        ElseActions(managerFound)["Record_Manager_Mapping_Error"] = Append(Item("manager Employee row missing or duplicated"));
        var current = managerFound["actions"]!["Condition_6"]!;
        ElseActions(current)["Record_Current_Employee_Error"] = Append(Item("current Employee row missing or duplicated"));
        var noManager = managerMissing["actions"]!["Condition_7"]!;
        ElseActions(noManager)["Record_Clear_Employee_Error"] = Append(Item("current Employee row missing or duplicated while clearing manager"));

        body["Record_Find_Manager_Employee_Failure"] = Append(Item("manager Employee lookup action failed"), "Find_Superior_Employee");
        managerFound["actions"]!["Record_Find_Current_Employee_Failure"] =
            Append(Item("current Employee lookup action failed"), "Find_Current_Employee");
        current["actions"]!["Record_Update_Superior_Failure"] =
            Append("@concat('Superior update failed for Microsoft 365 user ', string(items('Sync_Superiors')?['Id']), ': ', coalesce(outputs('Update_a_row_3')?['body']?['error']?['message'], 'Unknown Dataverse error'))", "Update_a_row_3");
        managerMissing["actions"]!["Record_Find_Employee_To_Clear_Failure"] =
            Append(Item("Employee lookup before clearing manager failed"), "Find_Employee_Without_Manager");
        noManager["actions"]!["Record_Clear_Superior_Failure"] =
            Append(Item("Dataverse Superior clear failed"), "Clear_Employee_Superior");

        actions["Check_Superior_Sync_Errors"] = JsonNode.Parse("""
            {"type":"If","expression":"@greater(length(variables('SuperiorSyncErrors')),0)",
            "actions":{"Fail_Superior_Sync":{"type":"Terminate","inputs":{"runStatus":"Failed","runError":{"code":"SuperiorSyncIncomplete","message":"@concat('One or more employee manager changes could not be applied: ', join(variables('SuperiorSyncErrors'), '; '))"}}}},
            "else":{"actions":{}},"runAfter":{"Sync_Superiors":["Succeeded","Failed","TimedOut"]}}
            """);

        var updated = json.ToJsonString();
        var backupPath = Path.Combine(Path.GetTempPath(), "leave-sync-before-" + flow.Id + "-" +
            DateTime.UtcNow.ToString("yyyyMMddHHmmss") + ".json");
        File.WriteAllText(backupPath, original);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(backupPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        Console.WriteLine("Original definition backed up at " + backupPath);
        Console.WriteLine("Original SHA-256 " + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(original))));
        Client.Update(new Entity("workflow", flow.Id) { ["clientdata"] = updated });
        var saved = Client.Retrieve("workflow", flow.Id, new ColumnSet("clientdata", "statecode"));
        var actual = saved.GetAttributeValue<string>("clientdata") ?? "";
        var verify = JsonNode.Parse(actual)?["properties"]?["definition"]?["actions"];
        if (verify?["Check_Superior_Sync_Errors"] == null ||
            verify["Sync_Superiors"]?["runAfter"]?["Initialize_Superior_Sync_Errors"] == null)
            throw new InvalidOperationException("Saved sync flow failed verification.");
        Console.WriteLine("Flow updated; state=" + saved.GetAttributeValue<OptionSetValue>("statecode")?.Value +
            "; new SHA-256 " + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(actual))));
    }
}
