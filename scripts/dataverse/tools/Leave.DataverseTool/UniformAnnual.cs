using System.Reflection;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

internal static partial class Development
{
    static void UniformAnnualDeploy()
    {
        var assembly = Find("pluginassembly", "name", "Leave.Plugins", "name", "publickeytoken").Single();
        var path = Root + "/server/Leave.Plugins/bin/Release/net462/Leave.Plugins.dll";
        var signed = AssemblyName.GetAssemblyName(path);
        if (assembly.GetAttributeValue<string>("name") != signed.Name ||
            assembly.GetAttributeValue<string>("publickeytoken") != Convert.ToHexString(signed.GetPublicKeyToken()!).ToLowerInvariant())
            throw new Exception("The signed leave safeguard assembly differs.");
        foreach (var message in new[] { "Create", "Update" }) {
            var pre = Find("sdkmessageprocessingstep", "name", "Leave safeguard: " + Request + " " + message,
                "stage", "mode", "statecode", "filteringattributes").Single();
            if (pre.GetAttributeValue<OptionSetValue>("stage")?.Value != 20 ||
                pre.GetAttributeValue<OptionSetValue>("mode")?.Value != 0 ||
                pre.GetAttributeValue<OptionSetValue>("statecode")?.Value != 0 ||
                !string.IsNullOrEmpty(pre.GetAttributeValue<string>("filteringattributes")))
                throw new Exception("The calling-user request guard is not active for " + message);
        }
        Client.Update(new Entity("pluginassembly", assembly.Id) {
            ["content"] = Convert.ToBase64String(File.ReadAllBytes(path))
        });
        const string typename = "Leave.Plugins.UniformAnnualDates";
        var found = Find("plugintype", "typename", typename, "pluginassemblyid");
        var typeId = found.Length == 0 ? Client.Create(new Entity("plugintype") {
            ["pluginassemblyid"] = assembly.ToEntityReference(),
            ["typename"] = typename, ["name"] = typename, ["friendlyname"] = typename
        }) : found.Single().Id;
        foreach (var message in new[] { "Create", "Update" }) {
            var messageId = Find("sdkmessage", "name", message).Single().Id;
            var query = new QueryExpression("sdkmessagefilter") { ColumnSet = new ColumnSet(false) };
            query.Criteria.AddCondition("sdkmessageid", ConditionOperator.Equal, messageId);
            query.Criteria.AddCondition("primaryobjecttypecode", ConditionOperator.Equal, Request);
            var filter = Client.RetrieveMultiple(query).Entities.Single();
            var name = "Leave uniform annual dates: " + message;
            var steps = Find("sdkmessageprocessingstep", "name", name);
            var step = new Entity("sdkmessageprocessingstep") {
                ["name"] = name,
                ["eventhandler"] = new EntityReference("plugintype", typeId),
                ["sdkmessageid"] = new EntityReference("sdkmessage", messageId),
                ["sdkmessagefilterid"] = filter.ToEntityReference(),
                ["stage"] = new OptionSetValue(40), ["mode"] = new OptionSetValue(0),
                ["rank"] = 20, ["supporteddeployment"] = new OptionSetValue(0),
                ["description"] = "Atomically generate date rows from the single pending leave request form."
            };
            if (message == "Update")
                step["filteringattributes"] = "cr2c9_startdate,cr2c9_enddate,cr2c9_hoursperday,cr2c9_leavetypename";
            Guid stepId;
            if (steps.Length == 0) stepId = Client.Create(step);
            else { stepId = steps.Single().Id; step.Id = stepId; Client.Update(step); }
            Client.Update(new Entity("sdkmessageprocessingstep", stepId) {
                ["statecode"] = new OptionSetValue(0), ["statuscode"] = new OptionSetValue(1)
            });
            Add(stepId, 92);
            var saved = Client.Retrieve("sdkmessageprocessingstep", stepId,
                new ColumnSet("stage", "mode", "statecode", "filteringattributes"));
            if (saved.GetAttributeValue<OptionSetValue>("stage")?.Value != 40 ||
                saved.GetAttributeValue<OptionSetValue>("mode")?.Value != 0 ||
                saved.GetAttributeValue<OptionSetValue>("statecode")?.Value != 0 ||
                (message == "Update" && saved.GetAttributeValue<string>("filteringattributes") !=
                    "cr2c9_startdate,cr2c9_enddate,cr2c9_hoursperday,cr2c9_leavetypename"))
                throw new Exception("Uniform date step verification failed for " + message);
        }
        Add(assembly.Id, 91);
        Console.WriteLine("PASS single-form leave-date Create/Update post-steps active; signed safeguard assembly updated.");
    }

    static void UniformAnnualTest()
    {
        var own = Guid.Parse("6938b1f2-22c2-f111-aaad-000d3a826b69");
        var annual = Guid.Parse("959c93de-1197-4de4-a660-2e2c3266f0fe");
        var created = Client.Create(new Entity(Request) {
            ["cr2c9_employeename"] = new EntityReference(Employee, own),
            ["cr2c9_leavetypename"] = new EntityReference("cr2c9_leavetype", annual),
            ["cr2c9_status"] = new OptionSetValue(0),
            ["cr2c9_startdate"] = new DateTime(2026, 11, 20),
            ["cr2c9_enddate"] = new DateTime(2026, 11, 23),
            ["cr2c9_hoursperday"] = 4m,
            ["cr2c9_reason"] = "SYNTHETIC DEVELOPMENT UNIFORM RANGE TEST; no approval requested."
        });
        Entity[] Dates() => Find("cr2c9_leaverequestdate", "cr2c9_leaverequest", created,
            "cr2c9_leavedate", "cr2c9_duration").OrderBy(x => x.GetAttributeValue<DateTime>("cr2c9_leavedate")).ToArray();
        void Expect(int hours) {
            var rows = Dates();
            if (rows.Length != 2 || rows[0].GetAttributeValue<DateTime>("cr2c9_leavedate") != new DateTime(2026,11,20) ||
                rows[1].GetAttributeValue<DateTime>("cr2c9_leavedate") != new DateTime(2026,11,23) ||
                rows.Any(x => x.GetAttributeValue<OptionSetValue>("cr2c9_duration")?.Value != (hours == 4 ? 290040000 : 290040001)))
                throw new Exception("Uniform Friday-Monday date generation failed.");
        }
        Expect(4);
        Client.Update(new Entity(Request, created) { ["cr2c9_hoursperday"] = 8m });
        Expect(8);
        Reject("Direct mixed duration", () => Client.Update(new Entity("cr2c9_leaverequestdate", Dates()[0].Id) {
            ["cr2c9_duration"] = new OptionSetValue(290040000)
        }), "individual date changes are not allowed");
        Console.WriteLine("PASS synthetic own Annual Leave Friday-Monday range generated exactly two uniform weekdays, edit replaced hours, direct mixed edit rejected; request=" + created);
    }

    static void UniformRangeTest()
    {
        var own = Guid.Parse("6938b1f2-22c2-f111-aaad-000d3a826b69");
        var typeName = Meta("cr2c9_leavetype").PrimaryNameAttribute;
        var types = Client.RetrieveMultiple(new QueryExpression("cr2c9_leavetype") {
            ColumnSet = new ColumnSet(typeName, "statecode")
        }).Entities;
        Console.WriteLine("Configured leave types: " + string.Join(", ",
            types.Select(x => x.GetAttributeValue<string>(typeName))));
        var type = types.Single(x => {
            var name = x.GetAttributeValue<string>(typeName) ?? "";
            return name.Contains("Medical", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Sick", StringComparison.OrdinalIgnoreCase);
        });
        if (type.GetAttributeValue<OptionSetValue>("statecode")?.Value != 0)
            throw new Exception("The configured Sick/Medical Leave type is not active.");
        var created = Client.Create(new Entity(Request) {
            ["cr2c9_employeename"] = new EntityReference(Employee, own),
            ["cr2c9_leavetypename"] = type.ToEntityReference(),
            ["cr2c9_status"] = new OptionSetValue(0),
            ["cr2c9_startdate"] = new DateTime(2026, 11, 27),
            ["cr2c9_enddate"] = new DateTime(2026, 11, 30),
            ["cr2c9_hoursperday"] = 8m,
            ["cr2c9_reason"] = "SYNTHETIC DEVELOPMENT FORM TEST; no approval requested."
        });
        Entity[] Rows() => Find("cr2c9_leaverequestdate", "cr2c9_leaverequest", created,
            "cr2c9_leavedate", "cr2c9_duration").OrderBy(x => x.GetAttributeValue<DateTime>("cr2c9_leavedate")).ToArray();
        void Expect(int hours) {
            var rows = Rows();
            if (rows.Length != 2 || rows[0].GetAttributeValue<DateTime>("cr2c9_leavedate") != new DateTime(2026,11,27) ||
                rows[1].GetAttributeValue<DateTime>("cr2c9_leavedate") != new DateTime(2026,11,30) ||
                rows.Any(x => x.GetAttributeValue<OptionSetValue>("cr2c9_duration")?.Value != (hours == 4 ? 290040000 : 290040001)))
                throw new Exception("Sick/Medical Leave form dates were not generated atomically.");
        }
        Expect(8);
        Client.Update(new Entity(Request, created) {
            ["cr2c9_hoursperday"] = 4m,
            ["cr2c9_reason"] = "SYNTHETIC DEVELOPMENT FORM EDIT TEST; no approval requested."
        });
        Expect(4);
        Console.WriteLine("PASS synthetic Sick/Medical Leave Friday–Monday request and same-form edit generated two uniform weekdays; request=" + created);
    }
}
