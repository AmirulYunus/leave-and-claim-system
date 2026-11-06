using System.Reflection;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

internal static partial class Development
{
    static readonly string[] CarryForwardTables = { CarryForwardReviewTable, CarryForwardCreditTable, CarryForwardUsageTable };

    static void CarryForwardGuardDeploy()
    {
        foreach (var table in CarryForwardTables) {
            var metadata = ((RetrieveEntityResponse)Client.Execute(new RetrieveEntityRequest {
                LogicalName = table, EntityFilters = EntityFilters.All, RetrieveAsIfPublished = true
            })).EntityMetadata;
            if (metadata.IsOptimisticConcurrencyEnabled != true || metadata.Keys.Length != 1 ||
                metadata.Keys[0].EntityKeyIndexStatus != EntityKeyIndexStatus.Active)
                throw new Exception("Active unique key and concurrency required before carry-forward guard deployment: " + table);
            if (Client.RetrieveMultiple(new QueryExpression(table) {
                ColumnSet = new ColumnSet(false), TopCount = 1
            }).Entities.Count != 0)
                throw new Exception("Carry-forward guard requires empty staging storage: " + table);
        }
        var path = Root + "/server/Leave.Plugins/bin/Release/net462/Leave.Plugins.dll";
        var identity = AssemblyName.GetAssemblyName(path);
        var assembly = Find("pluginassembly", "name", "Leave.Plugins", "publickeytoken").Single();
        if (identity.Name != "Leave.Plugins" ||
            assembly.GetAttributeValue<string>("publickeytoken") !=
            Convert.ToHexString(identity.GetPublicKeyToken()!).ToLowerInvariant())
            throw new Exception("Leave plug-in signing identity differs.");
        var bytes = File.ReadAllBytes(path);
        Client.Update(new Entity("pluginassembly", assembly.Id) {
            ["content"] = Convert.ToBase64String(bytes)
        });
        const string typename = "Leave.Plugins.CarryForwardWriteGuard";
        var types = Find("plugintype", "typename", typename, "pluginassemblyid")
            .Where(x => x.GetAttributeValue<EntityReference>("pluginassemblyid")?.Id == assembly.Id).ToArray();
        var typeId = types.Length == 0 ? Client.Create(new Entity("plugintype") {
            ["pluginassemblyid"] = assembly.ToEntityReference(),
            ["typename"] = typename, ["name"] = typename, ["friendlyname"] = typename
        }) : types.Single().Id;
        foreach (var table in CarryForwardTables)
        foreach (var message in new[] { "Create", "Update", "Delete", "Assign", "SetState", "CreateMultiple", "UpdateMultiple" }) {
            var sdkMessages = Find("sdkmessage", "name", message);
            if (sdkMessages.Length == 0) {
                if (message == "Create" || message == "Update" || message == "Delete")
                    throw new Exception("Mandatory carry-forward write message is unavailable: " + message);
                continue;
            }
            var filterQuery = new QueryExpression("sdkmessagefilter") {
                ColumnSet = new ColumnSet("iscustomprocessingstepallowed")
            };
            filterQuery.Criteria.AddCondition("sdkmessageid", ConditionOperator.Equal, sdkMessages.Single().Id);
            filterQuery.Criteria.AddCondition("primaryobjecttypecode", ConditionOperator.Equal, table);
            var filters = Client.RetrieveMultiple(filterQuery).Entities;
            if (filters.Count != 1 || filters[0].GetAttributeValue<bool>("iscustomprocessingstepallowed") != true) {
                if (message == "Create" || message == "Update" || message == "Delete")
                    throw new Exception("Mandatory carry-forward guard filter is unavailable: " + table + " " + message);
                Console.WriteLine("No standalone registration for " + table + " " + message);
                continue;
            }
            var name = "Leave carry-forward write safeguard: " + table + " " + message;
            var old = Find("sdkmessageprocessingstep", "name", name);
            if (old.Length > 1) throw new Exception("Duplicate carry-forward guard step: " + name);
            var row = new Entity("sdkmessageprocessingstep") {
                ["name"] = name,
                ["eventhandler"] = new EntityReference("plugintype", typeId),
                ["sdkmessageid"] = sdkMessages.Single().ToEntityReference(),
                ["sdkmessagefilterid"] = filters.Single().ToEntityReference(),
                ["stage"] = new OptionSetValue(20), ["mode"] = new OptionSetValue(0), ["rank"] = 1,
                ["supporteddeployment"] = new OptionSetValue(0),
                ["statecode"] = new OptionSetValue(0), ["statuscode"] = new OptionSetValue(1),
                ["description"] = "Only synchronous approval/cancellation accounting may lock a confirmed credit or append exact usage. HR review and credit posting remain closed."
            };
            var id = old.Length == 0 ? Client.Create(row) : old.Single().Id;
            if (old.Length != 0) { row.Id = id; Client.Update(row); }
            Add(id, 92);
            var saved = Client.Retrieve("sdkmessageprocessingstep", id,
                new ColumnSet("statecode", "stage", "mode", "rank", "eventhandler", "filteringattributes"));
            if (saved.GetAttributeValue<OptionSetValue>("statecode")?.Value != 0 ||
                saved.GetAttributeValue<OptionSetValue>("stage")?.Value != 20 ||
                saved.GetAttributeValue<OptionSetValue>("mode")?.Value != 0 ||
                saved.GetAttributeValue<int>("rank") != 1 ||
                saved.GetAttributeValue<EntityReference>("eventhandler")?.Id != typeId ||
                saved.GetAttributeValue<string>("filteringattributes") != null)
                throw new Exception("Carry-forward guard registration differs: " + name);
            Console.WriteLine("Verified active " + name);
        }
        Add(assembly.Id, 91);
        foreach (var table in CarryForwardTables)
            if (Client.RetrieveMultiple(new QueryExpression(table) {
                ColumnSet = new ColumnSet(false), TopCount = 1
            }).Entities.Count != 0)
                throw new Exception("Carry-forward staging storage changed during guard deployment: " + table);
        Console.WriteLine("Carry-forward review and credit writes are guarded. Usage is limited to synchronous decision accounting; no confirmation or credit posting is active.");
    }

    static void CarryForwardGuardTest()
    {
        foreach (var table in CarryForwardTables)
            if (Client.RetrieveMultiple(new QueryExpression(table) {
                ColumnSet = new ColumnSet(false), TopCount = 1
            }).Entities.Count != 0)
                throw new Exception("The fail-closed test requires empty carry-forward storage: " + table);
        var period = Client.RetrieveMultiple(new QueryExpression(BalancePeriodTable) {
            ColumnSet = new ColumnSet("cr2c9_employee"), TopCount = 1
        }).Entities.Single();
        var employee = period.GetAttributeValue<EntityReference>("cr2c9_employee")
            ?? throw new Exception("Balance period has no employee.");
        var review = new Entity(CarryForwardReviewTable) {
            ["cr2c9_name"] = "Rejected fictional write probe",
            ["cr2c9_employee"] = employee,
            ["cr2c9_sourceperiod"] = period.ToEntityReference(),
            ["cr2c9_targetperiod"] = period.ToEntityReference(),
            ["cr2c9_reviewstatus"] = new OptionSetValue(290040000)
        };
        var credit = new Entity(CarryForwardCreditTable) {
            ["cr2c9_name"] = "Rejected fictional write probe",
            ["cr2c9_employee"] = employee,
            ["cr2c9_sourceperiod"] = period.ToEntityReference(),
            ["cr2c9_targetperiod"] = period.ToEntityReference(),
            ["cr2c9_hours"] = 4,
            ["cr2c9_operationkey"] = "rejected-fictional-carry-probe",
            ["cr2c9_useby"] = new DateTime(2027, 3, 31)
        };
        var usage = new Entity(CarryForwardUsageTable) {
            ["cr2c9_name"] = "Rejected fictional write probe",
            ["cr2c9_hours"] = 4,
            ["cr2c9_operationkey"] = "rejected-fictional-usage-probe",
            ["cr2c9_entrykind"] = new OptionSetValue(290040000)
        };
        foreach (var row in new[] { review, credit, usage }) {
            Reject("Direct " + row.LogicalName + " Create", () => Client.Create(row),
                "read-only until HR confirmation processing is activated");
            Reject("Bulk " + row.LogicalName + " CreateMultiple", () => Client.Execute(
                new OrganizationRequest("CreateMultiple") {
                    ["Targets"] = new EntityCollection(new[] { row }) { EntityName = row.LogicalName }
                }), "read-only until HR confirmation processing is activated");
        }
        foreach (var table in CarryForwardTables)
            if (Client.RetrieveMultiple(new QueryExpression(table) {
                ColumnSet = new ColumnSet(false), TopCount = 1
            }).Entities.Count != 0)
                throw new Exception("A rejected carry-forward probe persisted: " + table);
        Console.WriteLine("PASS live direct/bulk carry-forward writes rejected; all three tables remain empty.");
    }
}
