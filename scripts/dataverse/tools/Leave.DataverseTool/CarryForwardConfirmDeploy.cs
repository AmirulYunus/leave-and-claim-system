using System.Reflection;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

internal static partial class Development
{
    const string CarryForwardConfirmApi = "cr2c9_ConfirmAnnualCarryForward";

    static void CarryForwardConfirmDeploy()
    {
        // The October/November rollout has no closing-year HR review to
        // migrate. Refuse to replace guarded assemblies if records appear.
        foreach (var table in CarryForwardTables) {
            var meta = ((RetrieveEntityResponse)Client.Execute(new RetrieveEntityRequest {
                LogicalName = table, EntityFilters = EntityFilters.All, RetrieveAsIfPublished = true
            })).EntityMetadata;
            if (meta.IsOptimisticConcurrencyEnabled != true || meta.Keys.Length != 1 ||
                meta.Keys[0].EntityKeyIndexStatus != EntityKeyIndexStatus.Active)
                throw new Exception("Active carry-forward uniqueness and concurrency are required: " + table);
            if (Client.RetrieveMultiple(new QueryExpression(table) {
                ColumnSet = new ColumnSet(false), TopCount = 1
            }).Entities.Count != 0)
                throw new Exception("Confirm API rollout requires empty carry-forward storage: " + table);
            foreach (var message in new[] { "Create", "Update", "Delete" }) {
                var name = "Leave carry-forward write safeguard: " + table + " " + message;
                var steps = Find("sdkmessageprocessingstep", "name", name,
                    "statecode", "stage", "mode", "rank", "filteringattributes");
                if (steps.Length != 1 ||
                    steps[0].GetAttributeValue<OptionSetValue>("statecode")?.Value != 0 ||
                    steps[0].GetAttributeValue<OptionSetValue>("stage")?.Value != 20 ||
                    steps[0].GetAttributeValue<OptionSetValue>("mode")?.Value != 0 ||
                    steps[0].GetAttributeValue<int>("rank") != 1 ||
                    steps[0].GetAttributeValue<string>("filteringattributes") != null)
                    throw new Exception("Carry-forward write guard differs: " + name);
            }
        }
        var balanceGuard = Find("plugintype", "typename", "Leave.BalanceGuards.BalanceStagingGuard").Single();
        var balanceSteps = Find("sdkmessageprocessingstep", "eventhandler", balanceGuard.Id,
            "statecode", "stage", "mode", "rank", "filteringattributes");
        if (balanceSteps.Length != 14 || balanceSteps.Any(s =>
            s.GetAttributeValue<OptionSetValue>("statecode")?.Value != 0 ||
            s.GetAttributeValue<OptionSetValue>("stage")?.Value != 20 ||
            s.GetAttributeValue<OptionSetValue>("mode")?.Value != 0 ||
            s.GetAttributeValue<int>("rank") != 1 ||
            s.GetAttributeValue<string>("filteringattributes") != null))
            throw new Exception("Existing balance safeguard registrations differ.");
        var employee = ((RetrieveEntityResponse)Client.Execute(new RetrieveEntityRequest {
            LogicalName = Employee, EntityFilters = EntityFilters.Privileges
        })).EntityMetadata;
        var readPrivilege = employee.Privileges.Single(p => p.PrivilegeType == PrivilegeType.Read).Name;
        if (string.IsNullOrWhiteSpace(readPrivilege) ||
            !readPrivilege.StartsWith("prvRead", StringComparison.Ordinal))
            throw new Exception("Employee read privilege metadata is invalid.");

        Entity Assembly(string name, string path) {
            var saved = Find("pluginassembly", "name", name, "publickeytoken").Single();
            var identity = AssemblyName.GetAssemblyName(path);
            if (identity.Name != name || saved.GetAttributeValue<string>("publickeytoken") !=
                Convert.ToHexString(identity.GetPublicKeyToken()!).ToLowerInvariant())
                throw new Exception("Signed plug-in identity differs: " + name);
            Client.Update(new Entity("pluginassembly", saved.Id) {
                ["content"] = Convert.ToBase64String(File.ReadAllBytes(path))
            });
            Add(saved.Id, 91);
            return saved;
        }
        Assembly("Leave.Plugins", Root + "/server/Leave.Plugins/bin/Release/net462/Leave.Plugins.dll");
        var balanceAssembly = Assembly("Leave.BalanceGuards",
            Root + "/server/Leave.BalanceGuards/bin/Release/net462/Leave.BalanceGuards.dll");
        const string typeName = "Leave.BalanceGuards.CarryForwardConfirmOperation";
        var types = Find("plugintype", "typename", typeName, "pluginassemblyid")
            .Where(t => t.GetAttributeValue<EntityReference>("pluginassemblyid")?.Id == balanceAssembly.Id).ToArray();
        if (types.Length > 1) throw new Exception("Duplicate carry-forward confirmation plug-in types.");
        var typeId = types.Length == 1 ? types[0].Id : Client.Create(new Entity("plugintype") {
            ["pluginassemblyid"] = balanceAssembly.ToEntityReference(),
            ["typename"] = typeName, ["name"] = typeName, ["friendlyname"] = typeName
        });
        var apis = Find("customapi", "uniquename", CarryForwardConfirmApi,
            "isfunction", "bindingtype", "boundentitylogicalname", "allowedcustomprocessingsteptype",
            "executeprivilegename", "plugintypeid");
        if (apis.Length > 1) throw new Exception("Duplicate carry-forward confirmation APIs.");
        Guid apiId;
        if (apis.Length == 0) apiId = CreateInSolution(new Entity("customapi") {
            ["uniquename"] = CarryForwardConfirmApi, ["name"] = CarryForwardConfirmApi,
            ["displayname"] = "Confirm standard Annual Leave carry-forward",
            ["description"] = "HR-only standard five-day confirmation for the last closed year; atomically records review and separate credit through 31 March.",
            ["isfunction"] = false, ["isprivate"] = false,
            ["bindingtype"] = new OptionSetValue(1), ["boundentitylogicalname"] = Employee,
            ["allowedcustomprocessingsteptype"] = new OptionSetValue(0),
            ["workflowsdkstepenabled"] = false,
            ["executeprivilegename"] = readPrivilege,
            ["plugintypeid"] = new EntityReference("plugintype", typeId)
        });
        else {
            var api = apis.Single(); apiId = api.Id;
            if (api.GetAttributeValue<bool>("isfunction") ||
                api.GetAttributeValue<OptionSetValue>("bindingtype")?.Value != 1 ||
                api.GetAttributeValue<string>("boundentitylogicalname") != Employee ||
                api.GetAttributeValue<OptionSetValue>("allowedcustomprocessingsteptype")?.Value != 0 ||
                api.GetAttributeValue<string>("executeprivilegename") != readPrivilege)
                throw new Exception("Existing carry-forward confirmation API contract differs.");
            Client.Update(new Entity("customapi", apiId) {
                ["plugintypeid"] = new EntityReference("plugintype", typeId)
            });
        }
        var outputs = Find("customapiresponseproperty", "customapiid", apiId, "uniquename");
        foreach (var (name, type) in new[] {
            ("Status",10), ("ReviewId",10), ("CreditId",10),
            ("SourceUnusedHours",7), ("CreditedHours",7), ("SeparateUnusedHours",7)
        }) {
            var matches = outputs.Where(p => p.GetAttributeValue<string>("uniquename") == name).ToArray();
            if (matches.Length > 1) throw new Exception("Duplicate confirmation response: " + name);
            if (matches.Length == 0) CreateInSolution(new Entity("customapiresponseproperty") {
                ["customapiid"] = new EntityReference("customapi", apiId),
                ["uniquename"] = name, ["name"] = CarryForwardConfirmApi + "." + name,
                ["displayname"] = name, ["type"] = new OptionSetValue(type)
            });
        }
        Console.WriteLine("PASS deployed dormant HR standard carry-forward confirmation API " + apiId +
            ". October calls remain outside the January-March confirmation window; no records were posted.");
    }

    static void CarryForwardConfirmTest()
    {
        var before = CarryForwardTables.ToDictionary(t => t, t => Client.RetrieveMultiple(
            new QueryExpression(t) { ColumnSet = new ColumnSet(false), TopCount = 1 }).Entities.Count);
        Reject("out-of-window carry-forward confirmation", () => Client.Execute(
            new OrganizationRequest(CarryForwardConfirmApi) {
                ["Target"] = new EntityReference(Employee,
                    Guid.Parse("6938b1f2-22c2-f111-aaad-000d3a826b69"))
            }), "directly assigned active Leave HR reviewer");
        foreach (var table in CarryForwardTables)
            if (Client.RetrieveMultiple(new QueryExpression(table) {
                ColumnSet = new ColumnSet(false), TopCount = 1
            }).Entities.Count != before[table])
                throw new Exception("The denied carry-forward confirmation changed " + table);
        Console.WriteLine("PASS non-HR confirmation denied; no review, credit or usage changed.");
    }
}
