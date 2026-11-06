using System.Reflection;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

internal static partial class Development
{
    static void SuperiorAccessDeploy()
    {
        var columns = Meta(SuperiorGrant).Attributes.Select(x => x.LogicalName).ToHashSet();
        foreach (var field in new[] { "cr2c9_grantee", "cr2c9_leaverequest", "cr2c9_leaverequestdate",
                     "cr2c9_leavedecision", "cr2c9_leavecancellation" })
            if (!columns.Contains(field)) throw new Exception("Superior grant schema is incomplete: " + field);

        var roles = Find("role", "name", SuperiorRoleName);
        foreach (var role in roles) {
            var assigned = new QueryExpression("systemuserroles") { ColumnSet = new ColumnSet(false), TopCount = 1 };
            assigned.Criteria.AddCondition("roleid", ConditionOperator.Equal, role.Id);
            if (Client.RetrieveMultiple(assigned).Entities.Count != 0)
                throw new Exception("An ordinary user already has the Superior role; test the deployment before modifying it.");
        }

        var assembly = Find("pluginassembly", "name", "Leave.Plugins", "publickeytoken").Single();
        var path = Root + "/server/Leave.Plugins/bin/Release/net462/Leave.Plugins.dll";
        var local = AssemblyName.GetAssemblyName(path);
        if (assembly.GetAttributeValue<string>("publickeytoken") !=
            Convert.ToHexString(local.GetPublicKeyToken()!).ToLowerInvariant())
            throw new Exception("The signed Leave.Plugins assembly identity differs.");
        Client.Update(new Entity("pluginassembly", assembly.Id) {
            ["content"] = Convert.ToBase64String(File.ReadAllBytes(path))
        });
        var typeId = PluginType(assembly.Id, "Leave.Plugins.SuperiorRecordAccess");

        Guid Register(string table, string message, int stage, int rank, string? filtering = null)
        {
            var name = "Leave Superior access: " + table + " " + message;
            var messageId = Find("sdkmessage", "name", message).Single().Id;
            var query = new QueryExpression("sdkmessagefilter") { ColumnSet = new ColumnSet(false) };
            query.Criteria.AddCondition("sdkmessageid", ConditionOperator.Equal, messageId);
            query.Criteria.AddCondition("primaryobjecttypecode", ConditionOperator.Equal, table);
            var filter = Client.RetrieveMultiple(query).Entities.Single();
            var prior = Find("sdkmessageprocessingstep", "name", name);
            if (prior.Length > 1) throw new Exception("Duplicate Superior access step: " + name);
            var step = new Entity("sdkmessageprocessingstep") {
                ["name"] = name,
                ["eventhandler"] = new EntityReference("plugintype", typeId),
                ["sdkmessageid"] = new EntityReference("sdkmessage", messageId),
                ["sdkmessagefilterid"] = filter.ToEntityReference(),
                ["stage"] = new OptionSetValue(stage), ["mode"] = new OptionSetValue(0),
                ["rank"] = rank, ["supporteddeployment"] = new OptionSetValue(0),
                ["description"] = "Maintain only system-provenanced assigned-Superior shares in the same transaction."
            };
            if (filtering != null) step["filteringattributes"] = filtering;
            Guid id;
            if (prior.Length == 0) id = Client.Create(step);
            else { id = prior[0].Id; step.Id = id; Client.Update(step); }
            Client.Update(new Entity("sdkmessageprocessingstep", id) {
                ["statecode"] = new OptionSetValue(0), ["statuscode"] = new OptionSetValue(1)
            });
            Add(id, 92);
            var saved = Client.Retrieve("sdkmessageprocessingstep", id,
                new ColumnSet("eventhandler", "statecode", "stage", "mode", "rank", "filteringattributes"));
            if (saved.GetAttributeValue<EntityReference>("eventhandler")?.Id != typeId ||
                saved.GetAttributeValue<OptionSetValue>("statecode")?.Value != 0 ||
                saved.GetAttributeValue<OptionSetValue>("stage")?.Value != stage ||
                saved.GetAttributeValue<OptionSetValue>("mode")?.Value != 0 ||
                saved.GetAttributeValue<int>("rank") != rank ||
                saved.GetAttributeValue<string>("filteringattributes") != filtering)
                throw new Exception("Superior access step did not verify: " + name);
            return id;
        }

        Register(Employee, "Update", 40, 30, "cr2c9_superior");
        Register(Request, "Create", 40, 30);
        Register(Cancellation, "Create", 40, 30);
        Register("cr2c9_leaverequestdate", "Create", 40, 30);
        Register(History, "Create", 40, 30);
        Register("cr2c9_leaverequestdate", "Delete", 20, 30);
        Add(assembly.Id, 91);
        Console.WriteLine("Superior share steps active in Developer environment. Existing requests still require audited backfill and assigned-staff app views; role remains unassigned.");
    }
}
