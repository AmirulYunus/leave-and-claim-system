using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Crm.Sdk.Messages;

internal static partial class Development {
    const string AccountingStep="Leave annual approval accounting";
    static readonly Guid AccountingEmployee=Guid.Parse("6dd608e3-45c5-4b8b-93a2-5eec5758f62b");
    static readonly Guid HrNotifyFlow=Guid.Parse("9646d8f4-96c3-f111-a05a-000d3a826b69");
    static void ApprovalAccountingDeploy(){
        foreach(var table in new[]{BalancePeriodTable,BalanceEntryTable,"cr2c9_leaverequestdate",History}){
            var meta=((RetrieveEntityResponse)Client.Execute(new RetrieveEntityRequest{LogicalName=table,EntityFilters=EntityFilters.All})).EntityMetadata;
            if(meta.Keys.Length==0 || meta.Keys.Any(k=>k.EntityKeyIndexStatus!=EntityKeyIndexStatus.Active))throw new Exception("Active uniqueness keys required: "+table);
        }
        var main=Find("pluginassembly","name","Leave.Plugins","content").Single();
        var mainHash=Convert.ToHexString(SHA256.HashData(Convert.FromBase64String(main.GetAttributeValue<string>("content")))).ToLowerInvariant();
        foreach(var table in new[]{Request,"cr2c9_leaverequestdate",History})foreach(var message in new[]{"Create","Update","Delete"}){
            var s=Find("sdkmessageprocessingstep","name","Leave safeguard: "+table+" "+message,"statecode","stage","mode","rank","filteringattributes").Single();
            if(s.GetAttributeValue<OptionSetValue>("statecode")?.Value!=0 || s.GetAttributeValue<OptionSetValue>("stage")?.Value!=20 || s.GetAttributeValue<OptionSetValue>("mode")?.Value!=0 || s.GetAttributeValue<int>("rank")!=10 || !string.IsNullOrEmpty(s.GetAttributeValue<string>("filteringattributes")))throw new Exception("Main guard registration differs.");
        }
        var assembly=Find("pluginassembly","name","Leave.BalanceGuards","publickeytoken").Single();
        var guard=Find("plugintype","typename","Leave.BalanceGuards.BalanceStagingGuard").Single();
        var steps=Find("sdkmessageprocessingstep","eventhandler",guard.Id,"statecode","stage","mode","filteringattributes");
        if(steps.Length!=14 || steps.Any(s=>s.GetAttributeValue<OptionSetValue>("statecode")?.Value!=0 || s.GetAttributeValue<OptionSetValue>("stage")?.Value!=20 || s.GetAttributeValue<OptionSetValue>("mode")?.Value!=0 || !string.IsNullOrEmpty(s.GetAttributeValue<string>("filteringattributes"))))throw new Exception("All balance guards must be active.");
        var path=Root+"/server/Leave.BalanceGuards/bin/Release/net462/Leave.BalanceGuards.dll";
        var bytes=File.ReadAllBytes(path);
        if(assembly.GetAttributeValue<string>("publickeytoken")!=Convert.ToHexString(AssemblyName.GetAssemblyName(path).GetPublicKeyToken()!).ToLowerInvariant())throw new Exception("Assembly signing identity differs.");
        Client.Update(new Entity("pluginassembly",assembly.Id){["content"]=Convert.ToBase64String(bytes),["description"]="Calendar annual grants, atomic approval deductions and immutable balance guards."});
        var typename="Leave.BalanceGuards.AnnualApprovalAccounting";
        var types=Find("plugintype","typename",typename,"pluginassemblyid");
        var type=types.Length==0?Client.Create(new Entity("plugintype"){["pluginassemblyid"]=assembly.ToEntityReference(),["typename"]=typename,["name"]=typename,["friendlyname"]=typename}):types.Single().Id;
        var messageId=Find("sdkmessage","name","Update").Single().Id;
        var q=new QueryExpression("sdkmessagefilter"){ColumnSet=new ColumnSet(false)};q.Criteria.AddCondition("sdkmessageid",ConditionOperator.Equal,messageId);q.Criteria.AddCondition("primaryobjecttypecode",ConditionOperator.Equal,Request);
        var filter=Client.RetrieveMultiple(q).Entities.Single();
        var prior=Find("sdkmessageprocessingstep","name",AccountingStep);
        // A newly created step uses the platform's default enabled status.
        // Until its image exists, the handler fails closed on annual approval.
        var row=new Entity("sdkmessageprocessingstep"){
            ["name"]=AccountingStep,["eventhandler"]=new EntityReference("plugintype",type),["sdkmessageid"]=new EntityReference("sdkmessage",messageId),["sdkmessagefilterid"]=filter.ToEntityReference(),
            ["stage"]=new OptionSetValue(40),["mode"]=new OptionSetValue(0),["rank"]=10,["supporteddeployment"]=new OptionSetValue(0),["filteringattributes"]="cr2c9_status",
            ["description"]="Synchronous annual deduction in the approval transaction. Explicit saved dates; insufficient or missing balance rolls back approval/history."
        };
        Guid step;
        if(prior.Length==0)step=Client.Create(row);else{step=prior.Single().Id;row.Id=step;Client.Update(row);}
        var images=Find("sdkmessageprocessingstepimage","sdkmessageprocessingstepid",step,"name");
        var image=new Entity("sdkmessageprocessingstepimage"){
            ["sdkmessageprocessingstepid"]=new EntityReference("sdkmessageprocessingstep",step),["name"]="BeforeApproval",["entityalias"]="BeforeApproval",["imagetype"]=new OptionSetValue(0),["messagepropertyname"]="Target",["attributes"]="cr2c9_status"
        };
        if(images.Length==0)Client.Create(image);else{image.Id=images.Single().Id;Client.Update(image);}
        Client.Update(new Entity("sdkmessageprocessingstep",step){["statecode"]=new OptionSetValue(0),["statuscode"]=new OptionSetValue(1)});Add(step,92);Add(assembly.Id,91);
        var saved=Client.Retrieve("sdkmessageprocessingstep",step,new ColumnSet("stage","mode","statecode","filteringattributes"));
        if(saved.GetAttributeValue<OptionSetValue>("statecode")?.Value!=0 || saved.GetAttributeValue<OptionSetValue>("stage")?.Value!=40 || saved.GetAttributeValue<OptionSetValue>("mode")?.Value!=0 || saved.GetAttributeValue<string>("filteringattributes")!="cr2c9_status")throw new Exception("Accounting registration verification failed.");
        var after=Client.Retrieve("pluginassembly",main.Id,new ColumnSet("content"));
        if(Convert.ToHexString(SHA256.HashData(Convert.FromBase64String(after.GetAttributeValue<string>("content")))).ToLowerInvariant()!=mainHash)throw new Exception("Main DLL changed unexpectedly.");
        File.WriteAllText(Root+"/annual-approval-accounting-2026-10-10.json",JsonSerializer.Serialize(new{assemblyId=assembly.Id,pluginTypeId=type,stepId=step,stage=40,mode=0,preImage="BeforeApproval",sha256=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),mainAssemblySha256=mainHash,deductionsActive=true,liveTestsComplete=false,fixtureEmployeeId=AccountingEmployee},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("PASS deployed enabled synchronous accounting step "+step+"; main DLL unchanged and all balance guards retained.");
    }
    static void ApprovalNotificationInspect(){
        var flow=Client.Retrieve("workflow",HrNotifyFlow,new ColumnSet("clientdata","statecode","name"));
        var json=JsonNode.Parse(flow.GetAttributeValue<string>("clientdata"))!;
        Console.WriteLine("Flow state="+flow.GetAttributeValue<OptionSetValue>("statecode")?.Value);
        Console.WriteLine(json["properties"]?["definition"]?["triggers"]?.ToJsonString() ?? "Definition path differs: "+string.Join(",",json.AsObject().Select(p=>p.Key)));
    }
}
