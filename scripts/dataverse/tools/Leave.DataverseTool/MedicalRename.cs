using System.Reflection;
using System.Security.Cryptography;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

internal static partial class Development
{
    static void MedicalRenameDeploy()
    {
        var type = Client.Retrieve("cr2c9_leavetype", SickLeaveType,
            new ColumnSet("cr2c9_leavetypename", "statecode"));
        if (type.GetAttributeValue<OptionSetValue>("statecode")?.Value != 0 ||
            !new[] { "Sick Leave", "Medical Leave" }.Contains(type.GetAttributeValue<string>("cr2c9_leavetypename")))
            throw new Exception("The registered Medical Leave type is not active or has an unexpected name.");
        foreach (var name in new[] { "Leave.Plugins", "Leave.BalanceGuards" }) {
            var path = Root + "/server/" + name + "/bin/Release/net462/" + name + ".dll";
            var source = File.ReadAllBytes(path);
            var existing = Find("pluginassembly", "name", name, "publickeytoken").Single();
            var token = Convert.ToHexString(AssemblyName.GetAssemblyName(path).GetPublicKeyToken()!).ToLowerInvariant();
            if (existing.GetAttributeValue<string>("publickeytoken") != token)
                throw new Exception("Plugin assembly signature differs: " + name);
            Client.Update(new Entity("pluginassembly", existing.Id) { ["content"] = Convert.ToBase64String(source) });
            var saved = Client.Retrieve("pluginassembly", existing.Id, new ColumnSet("content"));
            if (!SHA256.HashData(Convert.FromBase64String(saved.GetAttributeValue<string>("content")))
                    .SequenceEqual(SHA256.HashData(source)))
                throw new Exception("Plugin assembly readback differs: " + name);
        }
        var resources = new[] { "cr2c9_leaveRequestForm.js", "cr2c9_employeeEntitlement.js" }
            .Select(name => Find("webresource", "name", name, "name").Single()).ToArray();
        foreach (var resource in resources)
            Client.Update(new Entity("webresource", resource.Id) {
                ["content"] = Convert.ToBase64String(File.ReadAllBytes(Root + "/" + resource.GetAttributeValue<string>("name")))
            });
        if (type.GetAttributeValue<string>("cr2c9_leavetypename") != "Medical Leave")
            Client.Update(new Entity("cr2c9_leavetype", SickLeaveType) {
                ["cr2c9_leavetypename"] = "Medical Leave"
            });
        var api = Find("customapi", "uniquename", SickGrantApi, "displayname", "description").Single();
        Client.Update(new Entity("customapi", api.Id) {
            ["displayname"] = "Grant current calendar Medical Leave",
            ["description"] = "HR-only calendar-year outpatient Medical Leave grant. Adds only a verified service-based increase."
        });
        var after = Client.Retrieve("cr2c9_leavetype", SickLeaveType,
            new ColumnSet("cr2c9_leavetypename"));
        if (after.GetAttributeValue<string>("cr2c9_leavetypename") != "Medical Leave")
            throw new Exception("Medical Leave type rename did not verify.");
        Client.Execute(new PublishXmlRequest { ParameterXml =
            "<importexportxml><entities><entity>" + Request + "</entity><entity>cr2c9_leavetype</entity>" +
            "</entities><webresources>" + string.Concat(resources.Select(resource =>
                "<webresource>" + resource.Id + "</webresource>")) +
            "</webresources></importexportxml>" });
        Console.WriteLine("Medical Leave label, approval rules and Superior form published.");
    }
}
