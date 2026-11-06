using System.Reflection;
using System.Security.Cryptography;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

internal static partial class Development
{
    static void SuperiorOwnerFixDeploy()
    {
        var assembly = Find("pluginassembly", "name", "Leave.Plugins", "publickeytoken", "content").Single();
        var path = Root + "/server/Leave.Plugins/bin/Release/net462/Leave.Plugins.dll";
        var bytes = File.ReadAllBytes(path);
        var identity = AssemblyName.GetAssemblyName(path);
        var token = Convert.ToHexString(identity.GetPublicKeyToken()!).ToLowerInvariant();
        if (assembly.GetAttributeValue<string>("publickeytoken") != token)
            throw new Exception("The signed Leave.Plugins assembly identity differs.");
        foreach (var name in new[] {
            "Leave Superior access: cr2c9_employee Update",
            "Leave safeguard: cr2c9_leavedecision Update",
            "Leave safeguard: cr2c9_leavedecision Create"
        }) {
            var steps = Find("sdkmessageprocessingstep", "name", name, "statecode", "mode", "stage");
            if (steps.Length != 1 || steps[0].GetAttributeValue<OptionSetValue>("statecode")?.Value != 0 ||
                steps[0].GetAttributeValue<OptionSetValue>("mode")?.Value != 0 ||
                steps[0].GetAttributeValue<OptionSetValue>("stage")?.Value !=
                    (name.Contains("Superior") ? 40 : 20))
                throw new Exception("Required registered step is missing or inactive: " + name);
        }
        var existing = Convert.FromBase64String(assembly.GetAttributeValue<string>("content")!);
        var beforeHash = Convert.ToHexString(SHA256.HashData(existing));
        var afterHash = Convert.ToHexString(SHA256.HashData(bytes));
        if (beforeHash == afterHash) {
            Console.WriteLine("Leave.Plugins already matches local build " + afterHash);
            return;
        }
        Client.Update(new Entity("pluginassembly", assembly.Id) {
            ["content"] = Convert.ToBase64String(bytes)
        });
        var saved = Client.Retrieve("pluginassembly", assembly.Id, new ColumnSet("content"));
        var savedHash = Convert.ToHexString(SHA256.HashData(Convert.FromBase64String(
            saved.GetAttributeValue<string>("content")!)));
        if (savedHash != afterHash) throw new Exception("Updated plug-in assembly did not verify.");
        Console.WriteLine("Deployed Superior reassignment fix. Previous assembly SHA-256: " + beforeHash +
            "; new assembly SHA-256: " + afterHash);
    }
}
