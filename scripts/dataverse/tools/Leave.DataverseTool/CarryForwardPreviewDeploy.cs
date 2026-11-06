using System.Reflection;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

internal static partial class Development
{
    const string CarryForwardPreviewApi = "cr2c9_PreviewAnnualCarryForward";

    static void CarryForwardPreviewDeploy()
    {
        foreach (var table in CarryForwardTables)
            if (Client.RetrieveMultiple(new QueryExpression(table) {
                ColumnSet = new ColumnSet(false), TopCount = 1
            }).Entities.Count != 0)
                throw new Exception("The first preview rollout requires empty carry-forward tables: " + table);
        var assembly = Find("pluginassembly", "name", "Leave.BalanceGuards", "publickeytoken").Single();
        var path = Root + "/server/Leave.BalanceGuards/bin/Release/net462/Leave.BalanceGuards.dll";
        var identity = AssemblyName.GetAssemblyName(path);
        if (identity.Name != "Leave.BalanceGuards" ||
            assembly.GetAttributeValue<string>("publickeytoken") !=
            Convert.ToHexString(identity.GetPublicKeyToken()!).ToLowerInvariant())
            throw new Exception("The signed balance assembly identity differs.");
        var balanceGuard = Find("plugintype", "typename", "Leave.BalanceGuards.BalanceStagingGuard").Single();
        var existingSteps = Find("sdkmessageprocessingstep", "eventhandler", balanceGuard.Id,
            "statecode", "stage", "mode", "rank", "filteringattributes");
        if (existingSteps.Length != 14 || existingSteps.Any(s =>
            s.GetAttributeValue<OptionSetValue>("statecode")?.Value != 0 ||
            s.GetAttributeValue<OptionSetValue>("stage")?.Value != 20 ||
            s.GetAttributeValue<OptionSetValue>("mode")?.Value != 0 ||
            s.GetAttributeValue<int>("rank") != 1 ||
            s.GetAttributeValue<string>("filteringattributes") != null))
            throw new Exception("Existing balance write guards differ; preview was not registered.");
        var employee = ((RetrieveEntityResponse)Client.Execute(new RetrieveEntityRequest {
            LogicalName = Employee, EntityFilters = EntityFilters.Privileges
        })).EntityMetadata;
        var readPrivilege = employee.Privileges.Single(p => p.PrivilegeType == PrivilegeType.Read).Name;
        if (string.IsNullOrWhiteSpace(readPrivilege) ||
            !readPrivilege.StartsWith("prvRead", StringComparison.Ordinal))
            throw new Exception("Employee read privilege metadata is invalid.");
        Client.Update(new Entity("pluginassembly", assembly.Id) {
            ["content"] = Convert.ToBase64String(File.ReadAllBytes(path))
        });
        const string typeName = "Leave.BalanceGuards.CarryForwardPreviewOperation";
        var types = Find("plugintype", "typename", typeName, "pluginassemblyid")
            .Where(t => t.GetAttributeValue<EntityReference>("pluginassemblyid")?.Id == assembly.Id).ToArray();
        if (types.Length > 1) throw new Exception("Duplicate preview plugin types.");
        var typeId = types.Length == 1 ? types[0].Id : Client.Create(new Entity("plugintype") {
            ["pluginassemblyid"] = assembly.ToEntityReference(),
            ["typename"] = typeName, ["name"] = typeName, ["friendlyname"] = typeName
        });
        var apis = Find("customapi", "uniquename", CarryForwardPreviewApi,
            "isfunction", "bindingtype", "boundentitylogicalname", "allowedcustomprocessingsteptype",
            "executeprivilegename", "plugintypeid");
        if (apis.Length > 1) throw new Exception("Duplicate carry-forward preview APIs.");
        Guid apiId;
        if (apis.Length == 0) apiId = CreateInSolution(new Entity("customapi") {
            ["uniquename"] = CarryForwardPreviewApi, ["name"] = CarryForwardPreviewApi,
            ["displayname"] = "Preview last closed Annual Leave carry-forward",
            ["description"] = "HR-only read of the most recently closed year; no review, credit or expiry is posted.",
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
                throw new Exception("Existing carry-forward preview API contract differs.");
            Client.Update(new Entity("customapi", apiId) {
                ["plugintypeid"] = new EntityReference("plugintype", typeId)
            });
        }
        var outputs = Find("customapiresponseproperty", "customapiid", apiId, "uniquename");
        foreach (var (name, type) in new[] {
            ("Status",10), ("ReviewId",10), ("SourceYear",7),
            ("SourceUnusedHours",7), ("StandardLimitHours",7),
            ("CandidateCarryHours",7), ("SeparateUnusedHours",7)
        }) {
            var matches = outputs.Where(p => p.GetAttributeValue<string>("uniquename") == name).ToArray();
            if (matches.Length > 1) throw new Exception("Duplicate preview response: " + name);
            if (matches.Length == 0) CreateInSolution(new Entity("customapiresponseproperty") {
                ["customapiid"] = new EntityReference("customapi", apiId),
                ["uniquename"] = name, ["name"] = CarryForwardPreviewApi + "." + name,
                ["displayname"] = name, ["type"] = new OptionSetValue(type)
            });
        }
        Add(assembly.Id, 91);
        var saved = Client.Retrieve("customapi", apiId,
            new ColumnSet("plugintypeid", "bindingtype", "boundentitylogicalname", "executeprivilegename"));
        if (saved.GetAttributeValue<EntityReference>("plugintypeid")?.Id != typeId ||
            saved.GetAttributeValue<OptionSetValue>("bindingtype")?.Value != 1 ||
            saved.GetAttributeValue<string>("boundentitylogicalname") != Employee ||
            saved.GetAttributeValue<string>("executeprivilegename") != readPrivilege)
            throw new Exception("The registered HR preview API did not verify.");
        Console.WriteLine("PASS deployed HR-only read-only carry-forward preview API " + apiId + ". Existing balance guards remain active.");
    }

    static void CarryForwardPreviewTest()
    {
        var me = ((WhoAmIResponse)Client.Execute(new WhoAmIRequest())).UserId;
        var memberships = Find("systemuserroles", "systemuserid", me, "roleid");
        if (memberships.Any(m => Client.Retrieve("role", m.GetAttributeValue<Guid>("roleid"),
            new ColumnSet("name")).GetAttributeValue<string>("name") == HrRoleName))
            throw new Exception("Negative authorization probe requires a caller without the Leave HR role.");
        var before = CarryForwardTables.ToDictionary(t => t, t => Client.RetrieveMultiple(
            new QueryExpression(t) { ColumnSet = new ColumnSet(false), TopCount = 1 }).Entities.Count);
        Reject("non-HR carry-forward preview", () => Client.Execute(new OrganizationRequest(CarryForwardPreviewApi) {
            ["Target"] = new EntityReference(Employee, Guid.Parse("6938b1f2-22c2-f111-aaad-000d3a826b69"))
        }), "directly assigned active Leave HR reviewer");
        foreach (var table in CarryForwardTables)
            if (Client.RetrieveMultiple(new QueryExpression(table) {
                ColumnSet = new ColumnSet(false), TopCount = 1
            }).Entities.Count != before[table])
                throw new Exception("The read-only authorization probe changed " + table);
        Console.WriteLine("PASS live non-HR preview denied; no carry-forward records changed.");
    }
}
