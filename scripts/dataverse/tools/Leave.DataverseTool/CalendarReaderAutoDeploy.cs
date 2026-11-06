using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

internal static partial class Development
{
    const string CalendarSyncAssembly = "Leave.CalendarAccess";
    const string CalendarSyncType = "Leave.CalendarAccess.CalendarReaderRoleSync";
    const string CalendarSyncStepPrefix = "Leave calendar reader sync: ";
    const string SyncFlowName = "Sync Employees from Microsoft 365";

    static void CalendarReaderAutoDeploy()
    {
        var flow = Find("workflow", "name", SyncFlowName,
            "name", "statecode", "category", "clientdata").Single();
        if (flow.GetAttributeValue<OptionSetValue>("statecode")?.Value != 1 ||
            flow.GetAttributeValue<OptionSetValue>("category")?.Value != 5)
            throw new Exception("The scheduled Employee sync flow must be active.");
        var original = flow.GetAttributeValue<string>("clientdata")
            ?? throw new Exception("Employee sync flow definition is missing.");
        var json = JsonNode.Parse(original) ?? throw new Exception("Invalid employee sync definition.");
        var firstLoop = json["properties"]?["definition"]?["actions"]?["Apply_to_each"]?["actions"]
            ?? throw new Exception("Employee sync loop missing.");
        var byIdentity = firstLoop["Condition"]?["actions"]?["Update_a_row"]?["inputs"]?["parameters"]?.AsObject()
            ?? throw new Exception("Employee identity-match update missing.");
        var byEmail = firstLoop["Condition"]?["else"]?["actions"]?["Condition_2"]?["actions"]?
            ["Condition_3"]?["actions"]?["Update_a_row_2"]?["inputs"]?["parameters"]?.AsObject()
            ?? throw new Exception("Employee email-match update missing.");
        var newRow = firstLoop["Condition"]?["else"]?["actions"]?["Condition_2"]?["actions"]?
            ["Condition_3"]?["else"]?["actions"]?["Condition_4"]?["actions"]?
            ["Add_a_new_row"]?["inputs"]?["parameters"]?.AsObject()
            ?? throw new Exception("New Employee creation action missing.");
        foreach (var p in new[] { byIdentity, byEmail }) {
            if (p["entityName"]?.ToString() != "cr2c9_employees" || p["recordId"] == null)
                throw new Exception("Employee sync update contract changed.");
        }
        if (newRow["entityName"]?.ToString() != "cr2c9_employees")
            throw new Exception("New Employee creation contract changed.");
        const string m365Id = "@items('Apply_to_each')?['Id']";
        foreach (var p in new[] { byIdentity, byEmail, newRow })
            p["item/cr2c9_entrauserid"] = m365Id;

        var assemblyPath = Root + "/server/Leave.CalendarAccess/bin/Release/net462/Leave.CalendarAccess.dll";
        var bytes = File.ReadAllBytes(assemblyPath);
        var identity = AssemblyName.GetAssemblyName(assemblyPath);
        var token = Convert.ToHexString(identity.GetPublicKeyToken()!).ToLowerInvariant();
        if (identity.Name != CalendarSyncAssembly || token.Length != 16)
            throw new Exception("Unexpected calendar access assembly identity.");
        var existing = Find("pluginassembly", "name", CalendarSyncAssembly, "name", "publickeytoken");
        if (existing.Length > 1 || existing.Length == 1 &&
            existing[0].GetAttributeValue<string>("publickeytoken") != token)
            throw new Exception("Calendar access assembly identity conflict.");

        // Check registration targets before modifying the live assembly or flow.
        var filters = new Dictionary<string, Guid>();
        foreach (var message in new[] { "Create", "Update" }) {
            var id = Find("sdkmessage", "name", message).Single().Id;
            var q = new QueryExpression("sdkmessagefilter") { ColumnSet = new ColumnSet("iscustomprocessingstepallowed") };
            q.Criteria.AddCondition("sdkmessageid", ConditionOperator.Equal, id);
            q.Criteria.AddCondition("primaryobjecttypecode", ConditionOperator.Equal, Employee);
            var matches = Client.RetrieveMultiple(q).Entities;
            if (matches.Count != 1 || matches[0].GetAttributeValue<bool>("iscustomprocessingstepallowed") == false)
                throw new Exception("Employee " + message + " plugin registration is unavailable.");
            filters[message] = matches[0].Id;
        }

        var row = new Entity("pluginassembly") {
            ["name"] = identity.Name,
            ["version"] = identity.Version!.ToString(),
            ["culture"] = "neutral",
            ["publickeytoken"] = token,
            ["content"] = Convert.ToBase64String(bytes),
            ["isolationmode"] = new OptionSetValue(2),
            ["sourcetype"] = new OptionSetValue(0),
            ["description"] = "Verified active employees receive read-only access to the public absence calendar."
        };
        Guid assemblyId;
        if (existing.Length == 0) assemblyId = Client.Create(row);
        else { assemblyId = existing[0].Id; row.Id = assemblyId; Client.Update(row); }
        Add(assemblyId, 91);
        var typeId = PluginType(assemblyId, CalendarSyncType);
        foreach (var message in new[] { "Create", "Update" }) {
            var name = CalendarSyncStepPrefix + message;
            var prior = Find("sdkmessageprocessingstep", "name", name, "name");
            if (prior.Length > 1) throw new Exception("Duplicate calendar sync step: " + name);
            var step = new Entity("sdkmessageprocessingstep") {
                ["name"] = name,
                ["eventhandler"] = new EntityReference("plugintype", typeId),
                ["sdkmessageid"] = new EntityReference("sdkmessage", Find("sdkmessage", "name", message).Single().Id),
                ["sdkmessagefilterid"] = new EntityReference("sdkmessagefilter", filters[message]),
                ["stage"] = new OptionSetValue(40),
                ["mode"] = new OptionSetValue(0),
                ["rank"] = 50,
                ["supporteddeployment"] = new OptionSetValue(0),
                ["description"] = "Grant or remove only the dedicated read-only calendar role after verified Employee identity changes."
            };
            if (message == "Update")
                step["filteringattributes"] = "cr2c9_entrauserid,cr2c9_isactive,statecode";
            Guid stepId;
            if (prior.Length == 0) stepId = Client.Create(step);
            else { stepId = prior[0].Id; step.Id = stepId; Client.Update(step); }
            if (message == "Update") {
                var images = Find("sdkmessageprocessingstepimage", "sdkmessageprocessingstepid", stepId, "name");
                if (images.Length > 1) throw new Exception("Duplicate calendar sync preimage.");
                var image = new Entity("sdkmessageprocessingstepimage") {
                    ["sdkmessageprocessingstepid"] = new EntityReference("sdkmessageprocessingstep", stepId),
                    ["name"] = "BeforeCalendarIdentity",
                    ["entityalias"] = "BeforeCalendarIdentity",
                    ["imagetype"] = new OptionSetValue(0),
                    ["messagepropertyname"] = "Target",
                    ["attributes"] = "cr2c9_entrauserid,cr2c9_isactive,statecode"
                };
                if (images.Length == 0) Client.Create(image);
                else { image.Id = images[0].Id; Client.Update(image); }
            }
            Client.Update(new Entity("sdkmessageprocessingstep", stepId) {
                ["statecode"] = new OptionSetValue(0), ["statuscode"] = new OptionSetValue(1)
            });
            Add(stepId, 92);
            var savedStep = Client.Retrieve("sdkmessageprocessingstep", stepId,
                new ColumnSet("statecode", "stage", "mode", "rank", "filteringattributes"));
            if (savedStep.GetAttributeValue<OptionSetValue>("statecode")?.Value != 0 ||
                savedStep.GetAttributeValue<OptionSetValue>("stage")?.Value != 40 ||
                savedStep.GetAttributeValue<OptionSetValue>("mode")?.Value != 0 ||
                savedStep.GetAttributeValue<int>("rank") != 50 ||
                savedStep.GetAttributeValue<string>("filteringattributes") !=
                    (message == "Update" ? "cr2c9_entrauserid,cr2c9_isactive,statecode" : null))
                throw new Exception("Calendar reader sync step did not verify: " + message);
        }

        // The sync performs a deliberate identity update on both existing-row
        // paths every run. This repairs old email-matched rows and retries
        // reader-role assignment if a Dataverse account was not ready earlier.
        var backup = Path.Combine(Path.GetTempPath(), "leave-employee-sync-before-" + flow.Id + "-" +
            DateTime.UtcNow.ToString("yyyyMMddHHmmss") + ".json");
        File.WriteAllText(backup, original);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(backup, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        Client.Update(new Entity("workflow", flow.Id) { ["clientdata"] = json.ToJsonString() });
        var savedFlow = Client.Retrieve("workflow", flow.Id, new ColumnSet("clientdata", "statecode"));
        var saved = JsonNode.Parse(savedFlow.GetAttributeValue<string>("clientdata") ?? "")?
            ["properties"]?["definition"]?["actions"]?["Apply_to_each"]?["actions"];
        var savedByIdentity = saved?["Condition"]?["actions"]?["Update_a_row"]?
            ["inputs"]?["parameters"]?["item/cr2c9_entrauserid"]?.ToString();
        var savedByEmail = saved?["Condition"]?["else"]?["actions"]?["Condition_2"]?["actions"]?
            ["Condition_3"]?["actions"]?["Update_a_row_2"]?["inputs"]?["parameters"]?
            ["item/cr2c9_entrauserid"]?.ToString();
        if (savedFlow.GetAttributeValue<OptionSetValue>("statecode")?.Value != 1 ||
            savedByIdentity != m365Id || savedByEmail != m365Id)
            throw new Exception("Employee sync identity changes did not save.");
        Console.WriteLine("Active calendar reader automation. Sync flow=" + flow.Id +
            "; prior SHA-256=" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(original))) +
            "; backup=" + backup);
    }
}
