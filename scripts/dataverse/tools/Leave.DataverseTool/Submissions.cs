using System.Reflection;
using System.Security.Cryptography;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

internal static partial class Development
{
    static void SubmissionDeploy()
    {
        Entity? assembly = null;
        foreach (var message in new[] { "Create", "Update" }) {
            var step = Find("sdkmessageprocessingstep","name","Leave safeguard: " + Request + " " + message,
                "stage","mode","statecode","eventhandler","filteringattributes","impersonatinguserid").Single();
            if (step.GetAttributeValue<OptionSetValue>("stage")?.Value != 20 || step.GetAttributeValue<OptionSetValue>("mode")?.Value != 0 ||
                step.GetAttributeValue<OptionSetValue>("statecode")?.Value != 0 || !string.IsNullOrEmpty(step.GetAttributeValue<string>("filteringattributes")) ||
                step.GetAttributeValue<EntityReference>("impersonatinguserid") != null)
                throw new Exception("Existing " + message + " step is not the expected synchronous calling-user guard.");
            var handler = Client.Retrieve("plugintype",step.GetAttributeValue<EntityReference>("eventhandler").Id,new ColumnSet("typename","pluginassemblyid"));
            if (handler.GetAttributeValue<string>("typename") != "Leave.Plugins.LeaveRequestGuard") throw new Exception("Unexpected " + message + " handler.");
            var currentAssembly = Client.Retrieve("pluginassembly",handler.GetAttributeValue<EntityReference>("pluginassemblyid").Id,new ColumnSet("name","publickeytoken"));
            if (assembly != null && assembly.Id != currentAssembly.Id) throw new Exception("Create and Update guards use different assemblies.");
            assembly = currentAssembly;
        }
        var path = Path.Combine(Root,"server/Leave.Plugins/bin/Release/net462/Leave.Plugins.dll");
        var name = AssemblyName.GetAssemblyName(path);
        if (assembly == null || assembly.GetAttributeValue<string>("name") != name.Name || assembly.GetAttributeValue<string>("publickeytoken") != Convert.ToHexString(name.GetPublicKeyToken()!).ToLowerInvariant())
            throw new Exception("Assembly signing identity differs.");
        var bytes = File.ReadAllBytes(path);
        Client.Update(new Entity("pluginassembly",assembly.Id) { ["content"] = Convert.ToBase64String(bytes) });
        var saved = Client.Retrieve("pluginassembly",assembly.Id,new ColumnSet("content"));
        if (!SHA256.HashData(Convert.FromBase64String(saved.GetAttributeValue<string>("content"))).SequenceEqual(SHA256.HashData(bytes)))
            throw new Exception("Saved Leave.Plugins assembly hash does not match local build.");
        Console.WriteLine("Updated and verified signed Leave.Plugins assembly; existing Create and Update guards remain active. No new steps, roles, flows or balances changed.");
    }
    static void SubmissionTest()
    {
        var own = Guid.Parse("6938b1f2-22c2-f111-aaad-000d3a826b69");
        var types = Client.RetrieveMultiple(new QueryExpression("cr2c9_leavetype") {
            ColumnSet = new ColumnSet(Meta("cr2c9_leavetype").PrimaryNameAttribute,"statecode")
        }).Entities;
        var primary = Meta("cr2c9_leavetype").PrimaryNameAttribute;
        var annual = types.Single(x => x.GetAttributeValue<string>(primary) == "Annual Leave" && x.GetAttributeValue<OptionSetValue>("statecode")?.Value == 0);
        Entity Submission() => new(Request) {
            ["cr2c9_employeename"] = new EntityReference(Employee,own),
            ["cr2c9_leavetypename"] = annual.ToEntityReference(), ["cr2c9_status"] = new OptionSetValue(0),
            ["cr2c9_startdate"] = new DateTime(2026,11,9), ["cr2c9_enddate"] = new DateTime(2026,11,10),
            ["cr2c9_reason"] = "Fictional development fixture: new-request submission validation only. Pending; no approval or balance deduction."
        };
        var ownBefore = Client.Retrieve(Employee,own,new ColumnSet(false)).RowVersion;
        var countBefore = Client.RetrieveMultiple(new QueryExpression(Request) { ColumnSet = new ColumnSet(false) }).Entities.Count;
        foreach (var field in new[]{"cr2c9_leavetypename","cr2c9_startdate","cr2c9_enddate"}) {
            var request = Submission(); request.Attributes.Remove(field);
            Reject("Missing submission "+field,()=>Client.Create(request),field=="cr2c9_leavetypename"?"Leave Type":field=="cr2c9_startdate"?"Start Date":"End Date");
        }
        var reversed = Submission(); reversed["cr2c9_enddate"] = new DateTime(2026,11,8);
        Reject("Reversed submission dates",()=>Client.Create(reversed),"earlier than Start Date");
        // These Dataverse columns are DateOnly: the platform strips a time before
        // the PreOperation plug-in receives the value. A raw SDK time is therefore
        // not a meaningful live negative test (the local plug-in test still
        // verifies its fail-closed behavior if a time ever reaches the guard).
        if (Client.RetrieveMultiple(new QueryExpression(Request) { ColumnSet = new ColumnSet(false) }).Entities.Count != countBefore ||
            Client.Retrieve(Employee,own,new ColumnSet(false)).RowVersion != ownBefore)
            throw new Exception("Failed submissions persisted a request or changed the employee.");
        Console.WriteLine("PASS rejected submissions create no records and leave employee version unchanged.");
        var id = Client.Create(Submission());
        var saved = Client.Retrieve(Request,id,new ColumnSet("cr2c9_status","cr2c9_startdate","cr2c9_enddate","cr2c9_employeename","cr2c9_leavetypename","cr2c9_decisionon"));
        if (saved.GetAttributeValue<OptionSetValue>("cr2c9_status")?.Value != 0 || saved.Contains("cr2c9_decisionon") ||
            saved.GetAttributeValue<DateTime>("cr2c9_startdate") != new DateTime(2026,11,9) || saved.GetAttributeValue<DateTime>("cr2c9_enddate") != new DateTime(2026,11,10))
            throw new Exception("Valid submission did not retain its pending calendar-date values.");
        Console.WriteLine("PASS valid own request saved as Pending Superior; fictional fixture " + id);
    }
    static void SubmissionDateOnlyInspect()
    {
        const string reason="Fictional development fixture: new-request submission validation only. Pending; no approval or balance deduction.";
        var rows=Find(Request,"cr2c9_reason",reason,"cr2c9_startdate","cr2c9_enddate","cr2c9_status");
        if(rows.Length==0)throw new Exception("No matching DateOnly submission fixture.");
        foreach(var row in rows) {
            var start=row.GetAttributeValue<DateTime>("cr2c9_startdate");
            var end=row.GetAttributeValue<DateTime>("cr2c9_enddate");
            if(start.TimeOfDay!=TimeSpan.Zero || end.TimeOfDay!=TimeSpan.Zero || row.GetAttributeValue<OptionSetValue>("cr2c9_status")?.Value!=0)
                throw new Exception("Submission DateOnly/status verification failed: "+row.Id);
        }
        Console.WriteLine("PASS "+rows.Length+" fictional pending submission fixture(s) store midnight calendar dates; a time supplied to DateOnly was normalized by Dataverse before the plug-in.");
    }
}
