using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

internal static partial class Development
{
    static void SuperiorSyncFlowFinalize()
    {
        var matches = Find("workflow", "name", "Sync Employees from Microsoft 365",
            "name", "category", "clientdata", "statecode");
        if (matches.Length != 1 || matches[0].GetAttributeValue<OptionSetValue>("category")?.Value != 5)
            throw new InvalidOperationException("Expected one employee sync flow.");
        var flow = matches[0];
        var original = flow.GetAttributeValue<string>("clientdata") ?? throw new InvalidOperationException("Missing definition.");
        var json = JsonNode.Parse(original) ?? throw new InvalidOperationException("Invalid definition.");
        var actions = json["properties"]?["definition"]?["actions"]?.AsObject()
            ?? throw new InvalidOperationException("Missing actions.");
        var check = actions["Check_Superior_Sync_Errors"] ?? throw new InvalidOperationException("Error check missing.");
        const string previous = "@or(greater(length(variables('SuperiorSyncErrors')),0),not(equals(actions('Sync_Superiors')?['status'],'Succeeded')))";
        if (check["expression"]?.ToString() != previous)
            throw new InvalidOperationException("Unexpected existing error check; inspect manually.");
        // A missing Microsoft 365 manager legitimately returns 404. The handled failure can mark
        // the loop Failed even when its branch successfully clears the old manager. The explicit
        // error array distinguishes a genuine failure from this expected branch.
        check["expression"] = "@greater(length(variables('SuperiorSyncErrors')),0)";
        var updateError = actions["Sync_Superiors"]?["actions"]?["Condition_5"]?["actions"]?
            ["Condition_6"]?["actions"]?["Record_Update_Superior_Failure"]?["inputs"];
        if (updateError == null) throw new InvalidOperationException("Update error handler missing.");
        updateError["value"] = "@concat('Superior update failed for Microsoft 365 user ', string(items('Sync_Superiors')?['Id']), ': ', coalesce(outputs('Update_a_row_3')?['body']?['error']?['message'], 'Unknown Dataverse error'))";
        var updated = json.ToJsonString();
        var backupPath = Path.Combine(Path.GetTempPath(), "leave-sync-before-final-" + flow.Id + "-" +
            DateTime.UtcNow.ToString("yyyyMMddHHmmss") + ".json");
        File.WriteAllText(backupPath, original);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(backupPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        Client.Update(new Entity("workflow", flow.Id) { ["clientdata"] = updated });
        var saved = Client.Retrieve("workflow", flow.Id, new ColumnSet("clientdata", "statecode"));
        var actual = saved.GetAttributeValue<string>("clientdata") ?? "";
        if (JsonNode.Parse(actual)?["properties"]?["definition"]?["actions"]?
            ["Check_Superior_Sync_Errors"]?["expression"]?.ToString() !=
            "@greater(length(variables('SuperiorSyncErrors')),0)")
            throw new InvalidOperationException("Saved error check failed verification.");
        Console.WriteLine("Flow error handling finalized; state=" + saved.GetAttributeValue<OptionSetValue>("statecode")?.Value +
            "; backup=" + backupPath + "; SHA-256=" +
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(actual))));
    }
}
