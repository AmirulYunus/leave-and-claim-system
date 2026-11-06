using System.Reflection;
using System.Text.Json;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Crm.Sdk.Messages;

internal static partial class Development {
    const string GrantApi="cr2c9_GrantCalendarAnnualLeave";
    static void AnnualGrantDeploy() {
        foreach(var table in ProtectedBalanceTables) {
            var meta=((RetrieveEntityResponse)Client.Execute(new RetrieveEntityRequest{LogicalName=table,EntityFilters=EntityFilters.All})).EntityMetadata;
            if(meta.IsOptimisticConcurrencyEnabled!=true || meta.Keys.Length!=1 || meta.Keys[0].EntityKeyIndexStatus!=EntityKeyIndexStatus.Active)throw new Exception("Active key/concurrency required: "+table);
        }
        var empMeta=Meta(Employee);
        if(!empMeta.Attributes.Any(x=>x.LogicalName=="cr2c9_leavingdate")) {
            Client.Execute(new CreateAttributeRequest{EntityName=Employee,SolutionUniqueName=Solution,Attribute=new DateTimeAttributeMetadata {
                SchemaName="cr2c9_leavingdate",DisplayName=L("Leaving Date"),Description=L("Optional final employment date maintained by authorized HR/administration. First/last-year grants remain pending policy."),
                DateTimeBehavior=DateTimeBehavior.DateOnly,Format=DateTimeFormat.DateOnly,RequiredLevel=new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.None)
            }});
            Client.Execute(new PublishXmlRequest{ParameterXml="<importexportxml><entities><entity>cr2c9_employee</entity></entities></importexportxml>"});
        } else if(empMeta.Attributes.Single(x=>x.LogicalName=="cr2c9_leavingdate") is not DateTimeAttributeMetadata a || a.DateTimeBehavior?.Value!="DateOnly")throw new Exception("Leaving date contract differs.");
        var path=Root+"/server/Leave.BalanceGuards/bin/Release/net462/Leave.BalanceGuards.dll";
        var bytes=File.ReadAllBytes(path);var identity=AssemblyName.GetAssemblyName(path);
        var assembly=Find("pluginassembly","name","Leave.BalanceGuards","publickeytoken").Single();
        if(assembly.GetAttributeValue<string>("publickeytoken")!=Convert.ToHexString(identity.GetPublicKeyToken()!).ToLowerInvariant())throw new Exception("Signing identity differs.");
        var guardType=Find("plugintype","typename","Leave.BalanceGuards.BalanceStagingGuard","pluginassemblyid").Single();
        if(guardType.GetAttributeValue<EntityReference>("pluginassemblyid")?.Id!=assembly.Id)throw new Exception("Guard assembly differs.");
        var q=new QueryExpression("sdkmessageprocessingstep"){ColumnSet=new ColumnSet("name","statecode","stage","mode","rank","filteringattributes")};
        q.Criteria.AddCondition("eventhandler",ConditionOperator.Equal,guardType.Id);
        var steps=Client.RetrieveMultiple(q).Entities;
        if(steps.Count!=14 || steps.Any(x=>x.GetAttributeValue<OptionSetValue>("statecode")?.Value!=0 || x.GetAttributeValue<OptionSetValue>("stage")?.Value!=20 || x.GetAttributeValue<OptionSetValue>("mode")?.Value!=0 || x.GetAttributeValue<int>("rank")!=1 || x.GetAttributeValue<string>("filteringattributes")!=null))throw new Exception("All fourteen write guards must remain active.");
        var balanceMeta=((RetrieveEntityResponse)Client.Execute(new RetrieveEntityRequest{LogicalName=BalancePeriodTable,EntityFilters=EntityFilters.Privileges})).EntityMetadata;
        var privilege=balanceMeta.Privileges.Single(x=>x.PrivilegeType==PrivilegeType.Create).Name;
        if(privilege!="prvCreatecr2c9_leavebalanceperiod")throw new Exception("Unexpected grant privilege: "+privilege);
        // Same registered guard class. No protection steps are disabled during replacement.
        Client.Update(new Entity("pluginassembly",assembly.Id){["content"]=Convert.ToBase64String(bytes),["description"]="Authorized calendar annual grants; direct balance writes and ledger edits denied."});
        Guid Plugin(string name) {
            var existing=Find("plugintype","typename",name,"pluginassemblyid").Where(x=>x.GetAttributeValue<EntityReference>("pluginassemblyid")?.Id==assembly.Id).ToArray();
            return existing.Length==0?Client.Create(new Entity("plugintype"){["pluginassemblyid"]=assembly.ToEntityReference(),["typename"]=name,["name"]=name,["friendlyname"]=name}):existing.Single().Id;
        }
        var maintenance=Plugin("Leave.BalanceGuards.EmploymentInputGuard");
        foreach(var message in new[]{"Create","Update","CreateMultiple","UpdateMultiple"}) {
            var sdkmessage=Find("sdkmessage","name",message).Single();
            var fq=new QueryExpression("sdkmessagefilter"){ColumnSet=new ColumnSet(false)};
            fq.Criteria.AddCondition("sdkmessageid",ConditionOperator.Equal,sdkmessage.Id);fq.Criteria.AddCondition("primaryobjecttypecode",ConditionOperator.Equal,Employee);
            var filter=Client.RetrieveMultiple(fq).Entities.Single();var name="Leave employment input safeguard: "+message;
            var prior=Find("sdkmessageprocessingstep","name",name);var row=new Entity("sdkmessageprocessingstep"){
                ["name"]=name,["eventhandler"]=new EntityReference("plugintype",maintenance),["sdkmessageid"]=sdkmessage.ToEntityReference(),["sdkmessagefilterid"]=filter.ToEntityReference(),
                ["stage"]=new OptionSetValue(20),["mode"]=new OptionSetValue(0),["rank"]=1,["supporteddeployment"]=new OptionSetValue(0)
            };
            if(prior.Length==0)Add(Client.Create(row),92);else{row.Id=prior.Single().Id;Client.Update(row);}
        }
        var plugin=Plugin("Leave.BalanceGuards.CalendarAnnualGrantOperation");
        var apis=Find("customapi","uniquename",GrantApi,"isfunction","bindingtype","boundentitylogicalname","allowedcustomprocessingsteptype","executeprivilegename","plugintypeid");
        Guid api;
        if(apis.Length==0)api=CreateInSolution(new Entity("customapi") {
            ["uniquename"]=GrantApi,["name"]=GrantApi,["displayname"]="Grant current calendar annual leave",
            ["description"]="Administrative current-year annual grants from saved HR inputs. Missing details and held policy skip; repeat runs and increases credit only differences.",
            ["isfunction"]=false,["isprivate"]=false,["bindingtype"]=new OptionSetValue(1),["boundentitylogicalname"]=Employee,
            ["allowedcustomprocessingsteptype"]=new OptionSetValue(0),["workflowsdkstepenabled"]=true,
            ["executeprivilegename"]=privilege,["plugintypeid"]=new EntityReference("plugintype",plugin)
        });
        else {
            var e=apis.Single();api=e.Id;
            if(e.GetAttributeValue<bool>("isfunction") || e.GetAttributeValue<OptionSetValue>("bindingtype")?.Value!=1 || e.GetAttributeValue<string>("boundentitylogicalname")!=Employee || e.GetAttributeValue<OptionSetValue>("allowedcustomprocessingsteptype")?.Value!=0 || e.GetAttributeValue<string>("executeprivilegename")!=privilege)throw new Exception("Existing annual grant API contract differs.");
            Client.Update(new Entity("customapi",api){["plugintypeid"]=new EntityReference("plugintype",plugin)});
        }
        foreach(var prop in new[]{("Status",10),("Reason",10),("PeriodStart",10),("PeriodEnd",10),("CreditedHours",7),("AvailableHours",7)})
            if(!Find("customapiresponseproperty","customapiid",api,"uniquename").Any(x=>x.GetAttributeValue<string>("uniquename")==prop.Item1))
                CreateInSolution(new Entity("customapiresponseproperty"){["customapiid"]=new EntityReference("customapi",api),["uniquename"]=prop.Item1,["name"]=GrantApi+"."+prop.Item1,["displayname"]=prop.Item1,["type"]=new OptionSetValue(prop.Item2)});
        File.WriteAllText(Root+"/annual-grants-2026-10-10.json",JsonSerializer.Serialize(new{assemblyId=assembly.Id,apiId=api,pluginTypeId=plugin,
            sha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant(),executePrivilege=privilege,requiresGlobalDepth=true,
            annualTypeId="959c93de-1197-4de4-a660-2e2c3266f0fe",grantsActive=true,deductionsActive=false,scheduled=false},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("Deployed annual administrative grant action "+api+". All fourteen balance guards retained; no main leave DLL, roles, approvals or notification runs changed.");
    }
    static OrganizationResponse Grant(Guid employee)=>Client.Execute(new OrganizationRequest(GrantApi){["Target"]=new EntityReference(Employee,employee)});
    static void AnnualGrantTest() {
        void Expect(OrganizationResponse r,string status,int credited,int available) {
            if((string)r["Status"]!=status || (int)r["CreditedHours"]!=credited || (int)r["AvailableHours"]!=available)throw new Exception("Unexpected grant response: "+string.Join(";",r.Results.Select(x=>x.Key+"="+x.Value)));
            Console.WriteLine("PASS live "+status+" credited="+credited+" available="+available+" hours, "+r["PeriodStart"]+" through "+r["PeriodEnd"]);
        }
        var today=DateTime.UtcNow.AddHours(8).Date;
        // Mutate only the pre-existing explicitly fictional fixture. Retain test ledger.
        var original=Client.Retrieve(Employee,FixtureEmployee,new ColumnSet("cr2c9_datejoined","cr2c9_employeelevel","cr2c9_employmenttype","cr2c9_isactive","cr2c9_leavingdate"));
        var periods=Find(BalancePeriodTable,"cr2c9_employee",FixtureEmployee,"cr2c9_periodstart");
        if(periods.Length!=0)throw new Exception("First live test requires fixture with no periods; use annual-grant-verify afterwards.");
        Client.Update(new Entity(Employee,FixtureEmployee){["cr2c9_datejoined"]=today.AddYears(-2).AddDays(1),["cr2c9_employeelevel"]=new OptionSetValue(290040000),["cr2c9_employmenttype"]=new OptionSetValue(290040000),["cr2c9_isactive"]=true,["cr2c9_leavingdate"]=null});
        try {
            Expect(Grant(FixtureEmployee),"Granted",112,112);Expect(Grant(FixtureEmployee),"AlreadyGranted",0,112);
            Client.Update(new Entity(Employee,FixtureEmployee){["cr2c9_employeelevel"]=null});Expect(Grant(FixtureEmployee),"PendingHrDetails",0,112);
            Client.Update(new Entity(Employee,FixtureEmployee){["cr2c9_employeelevel"]=new OptionSetValue(290040000),["cr2c9_datejoined"]=today.AddYears(-2)});
            Expect(Grant(FixtureEmployee),"Granted",8,120);Expect(Grant(FixtureEmployee),"AlreadyGranted",0,120);
            Client.Update(new Entity(Employee,FixtureEmployee){["cr2c9_leavingdate"]=today});Expect(Grant(FixtureEmployee),"PendingPolicy",0,120);
            var period=Find(BalancePeriodTable,"cr2c9_employee",FixtureEmployee).Single();
            Reject("Direct period modification",()=>Client.Update(new Entity(BalancePeriodTable,period.Id){["cr2c9_periodend"]=today}),"only by the authorized");
            var entry=Find(BalanceEntryTable,"cr2c9_balanceperiod",period.Id,"cr2c9_hours").First();
            Reject("Direct ledger modification",()=>Client.Update(new Entity(BalanceEntryTable,entry.Id){["cr2c9_hours"]=999}),"only by the authorized");
            Reject("Direct ledger deletion",()=>Client.Delete(BalanceEntryTable,entry.Id),"only by the authorized");
            Reject("Direct ledger creation",()=>Client.Create(new Entity(BalanceEntryTable){["cr2c9_balanceperiod"]=period.ToEntityReference(),["cr2c9_name"]="rejected direct credit",["cr2c9_operationkey"]="rejected direct credit",["cr2c9_hours"]=999,["cr2c9_entrykind"]=new OptionSetValue(290040000)}),"only by the authorized");
            var own=Guid.Parse("6938b1f2-22c2-f111-aaad-000d3a826b69");var held=Grant(own);
            if (held.Results["Status"] as string != "PendingHrDetails" && held.Results["Status"] as string != "PendingPolicy") throw new Exception("Own profile unexpectedly eligible; do not change its HR details for this test.");
            if ((int)held.Results["CreditedHours"] != 0 || (int)held.Results["AvailableHours"] != -1) throw new Exception("Held own profile received a grant.");
            if(Find(BalancePeriodTable,"cr2c9_employee",own).Length!=0)throw new Exception("Missing HR unexpectedly created period.");
        } finally {
            var restore=new Entity(Employee,FixtureEmployee);foreach(var field in new[]{"cr2c9_datejoined","cr2c9_employeelevel","cr2c9_employmenttype","cr2c9_isactive","cr2c9_leavingdate"})restore[field]=original.Contains(field)?original[field]:null;Client.Update(restore);
        }
        Expect(Grant(FixtureEmployee),"Granted",8,128);Expect(Grant(FixtureEmployee),"AlreadyGranted",0,128);
        Console.WriteLine("PASS grant, retry, null skip/preservation, anniversary delta, leaving-date hold and direct-write denials. Fixture original HR details restored. No request approvals or emails triggered.");
    }
    static void AnnualGrantVerify() {
        var response=Grant(FixtureEmployee);
        Console.WriteLine("Fictional fixture current grant response: "+string.Join("; ",response.Results.Select(x=>x.Key+"="+x.Value)));
        foreach(var p in Find(BalancePeriodTable,"cr2c9_employee",FixtureEmployee,"cr2c9_periodstart","cr2c9_periodend")) {
            var entries=Find(BalanceEntryTable,"cr2c9_balanceperiod",p.Id,"cr2c9_hours","cr2c9_entrykind");
            Console.WriteLine("Fixture period "+p.Id+": entries="+entries.Length+", total="+entries.Sum(x=>x.GetAttributeValue<int>("cr2c9_hours"))+" hours");
        }
    }
    static void HrConfigurationInspect() {
        var definition=Find("environmentvariabledefinition","schemaname","cr2c9_HRNotificationEmails","displayname","defaultvalue","type").Single();
        var values=Find("environmentvariablevalue","environmentvariabledefinitionid",definition.Id,"value");
        if(values.Length!=1 || values.Single().GetAttributeValue<string>("value")!="KhorLord.Yong@Asteriskdigital.com.my;Jennie.Cheong@Asteriskdigital.com.my")throw new Exception("HR recipient current value differs.");
        var flow=Client.Retrieve("workflow",Guid.Parse("9646d8f4-96c3-f111-a05a-000d3a826b69"),new ColumnSet("name","clientdata","statecode"));
        var json=flow.GetAttributeValue<string>("clientdata")??"";
        if(!json.Contains("parameters('HR Notification Emails (cr2c9_HRNotificationEmails)')") || json.Contains("emailMessage/To\":\"muhammad.amirul@asteriskdigital.com.my"))throw new Exception("Flow recipient reference differs.");
        Console.WriteLine("PASS HR email Text environment variable "+definition.Id+" with both configured recipients, and flow uses parameter reference. Default="+(definition.GetAttributeValue<string>("defaultvalue")??"<none>")+". Flow state="+flow.GetAttributeValue<OptionSetValue>("statecode")?.Value+". No email sent.");
    }
}
