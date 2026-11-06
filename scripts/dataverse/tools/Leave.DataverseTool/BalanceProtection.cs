using System.Reflection;
using System.Text.Json;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Crm.Sdk.Messages;

internal static partial class Development
{
    static readonly string[] ProtectedBalanceTables = { "cr2c9_leavebalanceperiod", "cr2c9_leavebalanceentry" };
    static void EmptyBalanceTables() {
        if (Find("customapi", "uniquename", "cr2c9_GrantCalendarAnnualLeave").Length != 0)
            throw new Exception("Staging commands are obsolete after annual-grant activation; use annual-grant-regression.");
        foreach (var table in ProtectedBalanceTables)
            if (Client.RetrieveMultiple(new QueryExpression(table) { ColumnSet = new ColumnSet(false), TopCount = 1 }).Entities.Count != 0)
                throw new Exception("Staging protection expects an empty balance table: " + table);
    }
    static void BalanceProtect() {
        EmptyBalanceTables();
        foreach (var table in ProtectedBalanceTables) {
            var metadata = ((RetrieveEntityResponse)Client.Execute(new RetrieveEntityRequest {
                LogicalName = table, EntityFilters = EntityFilters.All, RetrieveAsIfPublished = true
            })).EntityMetadata;
            if (metadata.IsOptimisticConcurrencyEnabled != true || metadata.Keys.Length != 1 ||
                metadata.Keys[0].EntityKeyIndexStatus != EntityKeyIndexStatus.Active)
                throw new Exception("Active balance key and optimistic concurrency required: " + table);
        }
        var path = Root + "/server/Leave.BalanceGuards/bin/Release/net462/Leave.BalanceGuards.dll";
        var bytes = File.ReadAllBytes(path);
        var identity = AssemblyName.GetAssemblyName(path);
        var token = Convert.ToHexString(identity.GetPublicKeyToken()!).ToLowerInvariant();
        if (identity.Name != "Leave.BalanceGuards" || token.Length != 16) throw new Exception("Unexpected staging assembly.");
        var existing = Find("pluginassembly", "name", identity.Name!, "name");
        if (existing.Length > 1) throw new Exception("Ambiguous balance staging assembly.");
        var row = new Entity("pluginassembly") {
            ["name"] = identity.Name, ["version"] = identity.Version!.ToString(), ["culture"] = "neutral",
            ["publickeytoken"] = token, ["content"] = Convert.ToBase64String(bytes),
            ["isolationmode"] = new OptionSetValue(2), ["sourcetype"] = new OptionSetValue(0),
            ["description"] = "Block balance writes while accounting processing is not activated. Separate from live leave decisions."
        };
        var assembly = existing.Length == 0 ? Client.Create(row) : existing.Single().Id;
        if (existing.Length != 0) { row.Id = assembly; Client.Update(row); }
        Add(assembly,91);
        const string typename = "Leave.BalanceGuards.BalanceStagingGuard";
        var types = Find("plugintype", "typename", typename, "pluginassemblyid")
            .Where(x => x.GetAttributeValue<EntityReference>("pluginassemblyid")?.Id == assembly).ToArray();
        var type = types.Length == 0 ? Client.Create(new Entity("plugintype") {
            ["pluginassemblyid"] = new EntityReference("pluginassembly",assembly),
            ["typename"] = typename, ["name"] = typename, ["friendlyname"] = typename
        }) : types.Single().Id;
        var steps = new Dictionary<string,Guid>();
        foreach (var table in ProtectedBalanceTables)
        foreach (var message in new[] { "Create", "Update", "Delete", "Assign", "SetState", "CreateMultiple", "UpdateMultiple" }) {
            var messages = Find("sdkmessage","name",message);
            if (messages.Length == 0) { Console.WriteLine("Unsupported message: " + message); continue; }
            var messageId = messages.Single().Id;
            var query = new QueryExpression("sdkmessagefilter") { ColumnSet = new ColumnSet("sdkmessagefilterid", "iscustomprocessingstepallowed") };
            query.Criteria.AddCondition("sdkmessageid",ConditionOperator.Equal,messageId);
            query.Criteria.AddCondition("primaryobjecttypecode",ConditionOperator.Equal,table);
            var filters = Client.RetrieveMultiple(query).Entities;
            if (filters.Count == 0 || filters.Single().GetAttributeValue<bool>("iscustomprocessingstepallowed") != true) {
                if (message == "Create" || message == "Update" || message == "Delete") throw new Exception("Required safeguard message unsupported: " + table + " " + message);
                Console.WriteLine("No standalone registration supported: " + table + " " + message); continue;
            }
            var name = "Leave balance staging safeguard: " + table + " " + message;
            var old = Find("sdkmessageprocessingstep","name",name);
            var step = new Entity("sdkmessageprocessingstep") {
                ["name"] = name, ["eventhandler"] = new EntityReference("plugintype",type),
                ["sdkmessageid"] = new EntityReference("sdkmessage",messageId), ["sdkmessagefilterid"] = filters.Single().ToEntityReference(),
                ["stage"] = new OptionSetValue(20), ["mode"] = new OptionSetValue(0), ["rank"] = 1,
                ["supporteddeployment"] = new OptionSetValue(0), ["statecode"] = new OptionSetValue(0), ["statuscode"] = new OptionSetValue(1),
                ["description"] = "Fail closed while balance processing is inactive. No caller, role or depth bypass."
            };
            var id = old.Length == 0 ? Client.Create(step) : old.Single().Id;
            if (old.Length != 0) { step.Id = id; Client.Update(step); }
            Add(id,92);
            var saved = Client.Retrieve("sdkmessageprocessingstep",id,new ColumnSet("statecode","stage","mode","rank","eventhandler","filteringattributes"));
            if (saved.GetAttributeValue<OptionSetValue>("statecode")?.Value != 0 || saved.GetAttributeValue<OptionSetValue>("stage")?.Value != 20 ||
                saved.GetAttributeValue<OptionSetValue>("mode")?.Value != 0 || saved.GetAttributeValue<int>("rank") != 1 ||
                saved.GetAttributeValue<EntityReference>("eventhandler")?.Id != type || saved.GetAttributeValue<string>("filteringattributes") != null)
                throw new Exception("Safeguard registration mismatch: " + name);
            steps.Add(table + " " + message,id); Console.WriteLine("Verified enabled " + name + " " + id);
        }
        EmptyBalanceTables();
        File.WriteAllText(Root + "/balance-protection-2026-10-10.json",JsonSerializer.Serialize(new {
            assemblyId = assembly, pluginTypeId = type,
            sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant(),
            steps, processingActive = false, purpose = "Fail-closed staging only; no grant or debit processor enabled"
        },new JsonSerializerOptions { WriteIndented = true }));
    }
    static void BalanceProtectionTest() {
        EmptyBalanceTables();
        var employee = new EntityReference(Employee,FixtureEmployee);
        var annual = new EntityReference("cr2c9_leavetype",Guid.Parse("959c93de-1197-4de4-a660-2e2c3266f0fe"));
        var period = new Entity(ProtectedBalanceTables[0]) {
            ["cr2c9_name"] = "Rejected fictional staging probe", ["cr2c9_employee"] = employee, ["cr2c9_leavetype"] = annual,
            ["cr2c9_periodstart"] = new DateTime(2026,1,1), ["cr2c9_periodend"] = new DateTime(2026,12,31)
        };
        var entry = new Entity(ProtectedBalanceTables[1]) {
            ["cr2c9_name"] = "Rejected fictional staging probe", ["cr2c9_operationkey"] = "rejected-staging-probe",
            // Both tables are empty. An invented foreign reference would fail
            // Dataverse reference validation before reaching this guard. Omit
            // the application-required period to probe the actual plugin path;
            // no invalid or test ledger record is ever persisted.
            ["cr2c9_entrykind"] = new OptionSetValue(290040000), ["cr2c9_hours"] = 112
        };
        foreach (var target in new[] { period,entry }) {
            Reject("Direct " + target.LogicalName + " Create",()=>Client.Create(target),"processing is not activated");
            // A standard-table bulk operation must not evade single-row guards.
            Reject("Bulk " + target.LogicalName + " CreateMultiple",()=>Client.Execute(new OrganizationRequest("CreateMultiple") {
                ["Targets"] = new EntityCollection(new[] { target }) { EntityName = target.LogicalName }
            }),"processing is not activated");
        }
        EmptyBalanceTables();
        Console.WriteLine("PASS all rejected probes left both balance tables empty; no credits, debits or notifications.");
    }
    static void AttachmentsInspect() {
        var requestId = Guid.Parse("c307e48e-24c2-f111-aaad-000d3a826b69");
        var query = new QueryExpression("annotation") { ColumnSet = new ColumnSet("filename","mimetype","filesize","isdocument","objectid","documentbody") };
        query.Criteria.AddCondition("objectid",ConditionOperator.Equal,requestId);
        foreach (var note in Client.RetrieveMultiple(query).Entities) {
            // Verify retrieval in memory only. Do not export or back up file contents.
            var file = Convert.FromBase64String(note.GetAttributeValue<string>("documentbody") ?? "");
            if (!note.GetAttributeValue<bool>("isdocument") || file.Length != note.GetAttributeValue<int>("filesize"))
                throw new Exception("Attachment contents do not match their metadata.");
            Console.WriteLine("LR-0007 Note attachment: file=" + note.GetAttributeValue<string>("filename") +
                " mime=" + note.GetAttributeValue<string>("mimetype") + " bytes=" + note.GetAttributeValue<int>("filesize") +
                " isDocument=" + note.GetAttributeValue<bool>("isdocument") + " note=" + note.Id);
            Console.WriteLine("PASS attachment payload can be retrieved and its size matches metadata; no file content exported.");
        }
        var form = Client.Retrieve("systemform",Guid.Parse("f297bf4c-1a1d-4560-b594-572612a10d5c"),new ColumnSet("formxml"));
        var xml = System.Xml.Linq.XDocument.Parse(form.GetAttributeValue<string>("formxml"));
        Console.WriteLine("Review timeline: " + string.Join("\n",xml.Descendants("control").Where(c=>(string?)c.Attribute("id")=="notescontrol").Select(c=>c.ToString())));
    }
}
