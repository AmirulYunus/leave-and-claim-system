using System.Reflection;
using System.Security.Cryptography;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

internal static partial class Development {
    static void HrApprovalPluginDeploy() {
        var hr=Find("role","name",HrRoleName,"name");
        if(hr.Length!=1)throw new Exception("A unique scoped Leave HR role is required before plugin deployment.");
        var requests=Find("sdkmessageprocessingstep","name","Leave safeguard: "+Request+" Update",
            "stage","mode","statecode");
        var approval=Find("sdkmessageprocessingstep","name","Leave annual approval accounting",
            "stage","mode","statecode");
        var employment=Find("sdkmessageprocessingstep","name","Leave employment input safeguard: Update",
            "stage","mode","statecode");
        void Step(Entity[] rows,int stage) {
            if(rows.Length!=1 || rows[0].GetAttributeValue<OptionSetValue>("stage")?.Value!=stage ||
                rows[0].GetAttributeValue<OptionSetValue>("mode")?.Value!=0 ||
                rows[0].GetAttributeValue<OptionSetValue>("statecode")?.Value!=0)
                throw new Exception("A required active synchronous plugin step did not verify.");
        }
        Step(requests,20);Step(approval,40);Step(employment,20);
        string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        void Publish(string name,string path) {
            var row=Find("pluginassembly","name",name,"publickeytoken").Single();
            var bytes=File.ReadAllBytes(path);
            var token=Convert.ToHexString(AssemblyName.GetAssemblyName(path).GetPublicKeyToken()!).ToLowerInvariant();
            if(row.GetAttributeValue<string>("publickeytoken")!=token)
                throw new Exception(name+" signing identity differs; no update made.");
            Client.Update(new Entity("pluginassembly",row.Id){["content"]=Convert.ToBase64String(bytes)});
            var stored=Client.Retrieve("pluginassembly",row.Id,new ColumnSet("content"));
            if(Hash(Convert.FromBase64String(stored.GetAttributeValue<string>("content")))!=Hash(bytes))
                throw new Exception(name+" saved hash did not verify.");
            Console.WriteLine("Verified "+name+" assembly SHA256 "+Hash(bytes));
        }
        // Publish accounting first: the still-old PreOperation guard denies HR
        // decisions until the matching main assembly has also been verified.
        Publish("Leave.BalanceGuards",Root+"/server/Leave.BalanceGuards/bin/Release/net462/Leave.BalanceGuards.dll");
        Publish("Leave.Plugins",Root+"/server/Leave.Plugins/bin/Release/net462/Leave.Plugins.dll");
        Console.WriteLine("HR approval server changes deployed in Developer. No HR user was assigned; ordinary-role, form and live approval checks remain.");
    }
}
