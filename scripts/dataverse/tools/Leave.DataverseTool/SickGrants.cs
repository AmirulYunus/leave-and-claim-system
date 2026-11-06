using System.Reflection;
using System.Security.Cryptography;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Crm.Sdk.Messages;
using System.Xml.Linq;

internal static partial class Development {
    const string SickGrantApi = "cr2c9_GrantCalendarSickLeave";
    static readonly Guid SickLeaveType = Guid.Parse("261e2bd3-770e-4880-bcef-e4f5be6571e6");

    static async Task SickGrantWebApiMetadata(string token) {
        using var http=new HttpClient();
        http.DefaultRequestHeaders.Authorization=new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",token);
        var response=await http.GetAsync(Org+"/api/data/v9.2/$metadata");
        Console.WriteLine("Dataverse Web API metadata HTTP "+(int)response.StatusCode);
        response.EnsureSuccessStatusCode();
        var xml=XDocument.Parse(await response.Content.ReadAsStringAsync());
        foreach(var name in new[]{"cr2c9_GrantCalendarAnnualLeave",SickGrantApi}) {
            var matches=xml.Descendants().Where(x=>x.Name.LocalName=="Action" &&
                (string?)x.Attribute("Name")==name).ToArray();
            Console.WriteLine(name+" Web API action count="+matches.Length);
            foreach(var action in matches)
                Console.WriteLine("  bound="+(string?)action.Attribute("IsBound")+
                    " parameters="+string.Join(",",action.Elements().Where(x=>x.Name.LocalName=="Parameter")
                        .Select(x=>(string?)x.Attribute("Name")+":"+(string?)x.Attribute("Type"))));
        }
    }

    static void SickGrantFlowPreflight() {
        var flow=CheckedAnnualGrantFlow();
        foreach(var action in new[]{"cr2c9_GrantCalendarAnnualLeave",SickGrantApi}) {
            var api=Find("customapi","uniquename",action,"uniquename","isprivate","isfunction","bindingtype","boundentitylogicalname","workflowsdkstepenabled").Single();
            var components=Find("solutioncomponent","objectid",api.Id,"componenttype","solutionid");
            Console.WriteLine(action+" id="+api.Id+" private="+api.GetAttributeValue<bool>("isprivate")+
                " function="+api.GetAttributeValue<bool>("isfunction")+
                " binding="+api.GetAttributeValue<OptionSetValue>("bindingtype")?.Value+
                " bound="+api.GetAttributeValue<string>("boundentitylogicalname")+
                " workflow="+api.GetAttributeValue<bool>("workflowsdkstepenabled")+
                " solutions="+string.Join(",",components.Select(x=>x.GetAttributeValue<EntityReference>("solutionid")?.Id+":"+x.GetAttributeValue<OptionSetValue>("componenttype")?.Value)));
        }
        var flowComponents=Find("solutioncomponent","objectid",flow.Id,"componenttype","solutionid");
        Console.WriteLine("Flow solutions="+string.Join(",",flowComponents.Select(x=>x.GetAttributeValue<EntityReference>("solutionid")?.Id+":"+x.GetAttributeValue<OptionSetValue>("componenttype")?.Value)));
    }

    static void SickGrantRegistrationCompare() {
        foreach (var name in new[]{"cr2c9_GrantCalendarAnnualLeave",SickGrantApi}) {
            var api = Find("customapi", "uniquename", name, "uniquename").Single();
            var full = Client.Retrieve("customapi", api.Id, new ColumnSet(true));
            Console.WriteLine(name + " customapi=" + api.Id);
            foreach (var key in new[]{"isprivate","isfunction","bindingtype","boundentitylogicalname",
                         "workflowsdkstepenabled","allowedcustomprocessingsteptype","executeprivilegename",
                         "statecode","statuscode","createdon","modifiedon"})
                Console.WriteLine("  " + key + "=" + (full.Contains(key)
                    ? full[key] is OptionSetValue option ? option.Value : full[key]
                    : "<missing>"));
            var messages = Find("sdkmessage", "name", name, "name","isactive");
            Console.WriteLine("  sdkmessage count=" + messages.Length);
            foreach (var message in messages) {
                Console.WriteLine("  sdkmessage=" + message.Id + " active=" + message.GetAttributeValue<bool>("isactive"));
                var filters = Find("sdkmessagefilter", "sdkmessageid", message.Id,
                    "primaryobjecttypecode","secondaryobjecttypecode","isvisible");
                foreach (var filter in filters)
                    Console.WriteLine("  filter=" + filter.Id + " primary=" +
                        filter.GetAttributeValue<string>("primaryobjecttypecode") + " visible=" +
                        filter.GetAttributeValue<bool>("isvisible"));
            }
        }
    }

    static void SickGrantPublishMetadata() {
        SickGrantRegistrationCompare();
        Client.Execute(new PublishAllXmlRequest());
        Console.WriteLine("Published Developer-environment customizations; recheck Power Automate action discovery.");
    }

    static void SickGrantDeploy() {
        var leaveType = Client.Retrieve("cr2c9_leavetype",SickLeaveType,
            new ColumnSet("cr2c9_leavetypename","statecode"));
        if(!new[]{"Sick Leave","Medical Leave"}.Contains(leaveType.GetAttributeValue<string>("cr2c9_leavetypename")) ||
           leaveType.GetAttributeValue<OptionSetValue>("statecode")?.Value!=0)
            throw new Exception("Configured Sick Leave row is changed or inactive; no deployment.");
        foreach(var table in ProtectedBalanceTables) {
            var m=((RetrieveEntityResponse)Client.Execute(new RetrieveEntityRequest {
                LogicalName=table,EntityFilters=EntityFilters.All
            })).EntityMetadata;
            if(m.IsOptimisticConcurrencyEnabled!=true || m.Keys.Length!=1 ||
               m.Keys[0].EntityKeyIndexStatus!=EntityKeyIndexStatus.Active)
                throw new Exception("Active balance key and concurrency required: "+table);
        }
        var assembly=Find("pluginassembly","name","Leave.BalanceGuards","publickeytoken").Single();
        var guard=Find("plugintype","typename","Leave.BalanceGuards.BalanceStagingGuard","pluginassemblyid").Single();
        if(guard.GetAttributeValue<EntityReference>("pluginassemblyid")?.Id!=assembly.Id)
            throw new Exception("Balance guard assembly differs.");
        var steps=Find("sdkmessageprocessingstep","eventhandler",guard.Id,
            "name","stage","mode","statecode","filteringattributes");
        if(steps.Length!=14 || steps.Any(x=>x.GetAttributeValue<OptionSetValue>("stage")?.Value!=20 ||
           x.GetAttributeValue<OptionSetValue>("mode")?.Value!=0 ||
           x.GetAttributeValue<OptionSetValue>("statecode")?.Value!=0 ||
           x.GetAttributeValue<string>("filteringattributes")!=null))
            throw new Exception("All fourteen balance write guards must be active.");
        var approval=Find("sdkmessageprocessingstep","name",AccountingStep,
            "stage","mode","statecode","filteringattributes").Single();
        if(approval.GetAttributeValue<OptionSetValue>("stage")?.Value!=40 ||
           approval.GetAttributeValue<OptionSetValue>("mode")?.Value!=0 ||
           approval.GetAttributeValue<OptionSetValue>("statecode")?.Value!=0 ||
           approval.GetAttributeValue<string>("filteringattributes")!="cr2c9_status")
            throw new Exception("Registered atomic approval accounting differs.");
        var images=Find("sdkmessageprocessingstepimage","sdkmessageprocessingstepid",approval.Id,"entityalias");
        if(images.Length!=1 || images[0].GetAttributeValue<string>("entityalias")!="BeforeApproval")
            throw new Exception("Approval pre-image differs.");
        var path=Root+"/server/Leave.BalanceGuards/bin/Release/net462/Leave.BalanceGuards.dll";
        var bytes=File.ReadAllBytes(path);
        if(assembly.GetAttributeValue<string>("publickeytoken")!=
           Convert.ToHexString(AssemblyName.GetAssemblyName(path).GetPublicKeyToken()!).ToLowerInvariant())
            throw new Exception("Signed balance assembly identity differs.");
        var privilege=((RetrieveEntityResponse)Client.Execute(new RetrieveEntityRequest {
            LogicalName=BalancePeriodTable,EntityFilters=EntityFilters.Privileges
        })).EntityMetadata.Privileges.Single(x=>x.PrivilegeType==PrivilegeType.Create).Name;
        if(privilege!="prvCreatecr2c9_leavebalanceperiod")throw new Exception("Unexpected execution privilege.");
        var existingApi=Find("customapi","uniquename",SickGrantApi,
            "isfunction","bindingtype","boundentitylogicalname","executeprivilegename");
        if(existingApi.Length>1)throw new Exception("Duplicate Sick grant action.");
        if(existingApi.Length==1) {
            var e=existingApi[0];
            if(e.GetAttributeValue<bool>("isfunction") ||
               e.GetAttributeValue<OptionSetValue>("bindingtype")?.Value!=1 ||
               e.GetAttributeValue<string>("boundentitylogicalname")!=Employee ||
               e.GetAttributeValue<string>("executeprivilegename")!=privilege)
                throw new Exception("Existing Sick grant action contract differs.");
        }
        // The old request guard still blocks Sick approval while this assembly
        // and action are installed. Approval is enabled only in a later step.
        Client.Update(new Entity("pluginassembly",assembly.Id) {
            ["content"]=Convert.ToBase64String(bytes),
            ["description"]="Protected Annual and Sick balance grants, atomic approval deductions and exact reversals."
        });
        const string typename="Leave.BalanceGuards.CalendarSickGrantOperation";
        var types=Find("plugintype","typename",typename,"pluginassemblyid");
        if(types.Length>1 || types.Length==1 &&
           types[0].GetAttributeValue<EntityReference>("pluginassemblyid")?.Id!=assembly.Id)
            throw new Exception("Sick grant plug-in identity differs.");
        var type=types.Length==1?types[0].Id:Client.Create(new Entity("plugintype") {
            ["pluginassemblyid"]=assembly.ToEntityReference(),["typename"]=typename,
            ["name"]=typename,["friendlyname"]=typename
        });
        Guid api;
        if(existingApi.Length==0) api=CreateInSolution(new Entity("customapi") {
            ["uniquename"]=SickGrantApi,["name"]=SickGrantApi,
            ["displayname"]="Grant current calendar Medical Leave",
            ["description"]="HR-only calendar-year outpatient Medical Leave grant. Adds only a verified increase after a service anniversary.",
            ["isfunction"]=false,["isprivate"]=false,
            ["bindingtype"]=new OptionSetValue(1),["boundentitylogicalname"]=Employee,
            ["allowedcustomprocessingsteptype"]=new OptionSetValue(0),
            ["workflowsdkstepenabled"]=true,["executeprivilegename"]=privilege,
            ["plugintypeid"]=new EntityReference("plugintype",type)
        });
        else {
            api=existingApi[0].Id;
            Client.Update(new Entity("customapi",api){["plugintypeid"]=new EntityReference("plugintype",type)});
        }
        foreach(var prop in new[]{("Status",10),("Reason",10),("PeriodStart",10),("PeriodEnd",10),("CreditedHours",7),("AvailableHours",7)})
            if(!Find("customapiresponseproperty","customapiid",api,"uniquename")
                .Any(x=>x.GetAttributeValue<string>("uniquename")==prop.Item1))
                CreateInSolution(new Entity("customapiresponseproperty") {
                    ["customapiid"]=new EntityReference("customapi",api),
                    ["uniquename"]=prop.Item1,["name"]=SickGrantApi+"."+prop.Item1,
                    ["displayname"]=prop.Item1,["type"]=new OptionSetValue(prop.Item2)
                });
        var after=Find("sdkmessageprocessingstep","eventhandler",guard.Id,"statecode");
        if(after.Length!=14 || after.Any(x=>x.GetAttributeValue<OptionSetValue>("statecode")?.Value!=0))
            throw new Exception("Balance write guard changed during deployment.");
        Console.WriteLine("PASS Sick grant action "+api+"; protected ledger guard steps remain active; assembly SHA256="+
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()+". Sick request approval remains blocked until separately enabled.");
    }

    static OrganizationResponse GrantSick(Guid employee)=>Client.Execute(new OrganizationRequest(SickGrantApi) {
        ["Target"]=new EntityReference(Employee,employee)
    });
    static void SickGrantFixtureTest() {
        var fixture=Client.Retrieve(Employee,FixtureEmployee,
            new ColumnSet("cr2c9_datejoined","cr2c9_employmenttype","cr2c9_isactive","cr2c9_leavingdate","statecode"));
        if(fixture.GetAttributeValue<OptionSetValue>("statecode")?.Value!=0)
            throw new Exception("Synthetic fixture is inactive; no test change.");
        var before=Find(BalancePeriodTable,"cr2c9_employee",FixtureEmployee,
            "cr2c9_leavetype","cr2c9_periodstart")
            .Where(x=>x.GetAttributeValue<EntityReference>("cr2c9_leavetype")?.Id==SickLeaveType).ToArray();
        if(before.Length>1)throw new Exception("Duplicate fixture Sick periods; no test change.");
        var original=new Entity(Employee,FixtureEmployee);
        foreach(var field in new[]{"cr2c9_datejoined","cr2c9_employmenttype","cr2c9_isactive","cr2c9_leavingdate"})
            original[field]=fixture.Contains(field)?fixture[field]:null;
        var today=DateTime.UtcNow.AddHours(8).Date;
        Client.Update(new Entity(Employee,FixtureEmployee) {
            ["cr2c9_datejoined"]=today.AddYears(-2),
            ["cr2c9_employmenttype"]=new OptionSetValue(290040000),
            ["cr2c9_isactive"]=true,["cr2c9_leavingdate"]=null
        });
        try {
            var first=GrantSick(FixtureEmployee);
            var second=GrantSick(FixtureEmployee);
            if((string)second["Status"]!="AlreadyGranted" || (int)second["CreditedHours"]!=0 ||
                (int)first["AvailableHours"]!=(int)second["AvailableHours"])
                throw new Exception("Sick fixture retry was not idempotent.");
            var periods=Find(BalancePeriodTable,"cr2c9_employee",FixtureEmployee,
                "cr2c9_leavetype","cr2c9_periodstart").Where(x=>
                x.GetAttributeValue<EntityReference>("cr2c9_leavetype")?.Id==SickLeaveType &&
                x.GetAttributeValue<DateTime>("cr2c9_periodstart").Year==today.Year).ToArray();
            if(periods.Length!=1)throw new Exception("Expected one current-year Sick fixture period.");
            var entries=Find(BalanceEntryTable,"cr2c9_balanceperiod",periods[0].Id,
                "cr2c9_hours","cr2c9_entrykind","cr2c9_operationkey");
            if(entries.Sum(x=>x.GetAttributeValue<int>("cr2c9_hours"))!=(int)second["AvailableHours"])
                throw new Exception("Sick fixture ledger does not match response.");
            Console.WriteLine("PASS synthetic Sick grant: first="+first["Status"]+
                ", first credited="+first["CreditedHours"]+", retry="+second["Status"]+
                ", available="+second["AvailableHours"]+", entries="+entries.Length+".");
        } finally { Client.Update(original); }
    }

    static void SickApprovalEnable() {
        var api=Find("customapi","uniquename",SickGrantApi,
            "plugintypeid","executeprivilegename").Single();
        if(api.GetAttributeValue<string>("executeprivilegename")!="prvCreatecr2c9_leavebalanceperiod" ||
           api.GetAttributeValue<EntityReference>("plugintypeid")?.Id==Guid.Empty)
            throw new Exception("Secured Sick grant action is missing.");
        var type=Client.Retrieve("cr2c9_leavetype",SickLeaveType,
            new ColumnSet("cr2c9_leavetypename","statecode"));
        if(!new[]{"Sick Leave","Medical Leave"}.Contains(type.GetAttributeValue<string>("cr2c9_leavetypename")) ||
           type.GetAttributeValue<OptionSetValue>("statecode")?.Value!=0)
            throw new Exception("Sick Leave category differs.");
        var approval=Find("sdkmessageprocessingstep","name",AccountingStep,
            "statecode","stage","mode","filteringattributes").Single();
        if(approval.GetAttributeValue<OptionSetValue>("statecode")?.Value!=0 ||
           approval.GetAttributeValue<OptionSetValue>("stage")?.Value!=40 ||
           approval.GetAttributeValue<OptionSetValue>("mode")?.Value!=0 ||
           approval.GetAttributeValue<string>("filteringattributes")!="cr2c9_status")
            throw new Exception("Atomic approval step is not active.");
        var guard=Find("plugintype","typename","Leave.BalanceGuards.BalanceStagingGuard").Single();
        var steps=Find("sdkmessageprocessingstep","eventhandler",guard.Id,"statecode");
        if(steps.Length!=14 || steps.Any(x=>x.GetAttributeValue<OptionSetValue>("statecode")?.Value!=0))
            throw new Exception("Protected ledger guards are not all active.");
        var period=Find(BalancePeriodTable,"cr2c9_employee",FixtureEmployee,
            "cr2c9_leavetype","cr2c9_periodstart").SingleOrDefault(x=>
                x.GetAttributeValue<EntityReference>("cr2c9_leavetype")?.Id==SickLeaveType &&
                x.GetAttributeValue<DateTime>("cr2c9_periodstart").Year==DateTime.UtcNow.AddHours(8).Year);
        if(period==null || Find(BalanceEntryTable,"cr2c9_balanceperiod",period.Id).Length==0)
            throw new Exception("Synthetic Sick grant has not passed live persistence verification.");
        var rows=Find("sdkmessageprocessingstep","name","Leave safeguard: "+Request+" Update",
            "statecode","stage","mode","filteringattributes","eventhandler");
        if(rows.Length!=1 || rows[0].GetAttributeValue<OptionSetValue>("statecode")?.Value!=0 ||
           rows[0].GetAttributeValue<OptionSetValue>("stage")?.Value!=20 ||
           rows[0].GetAttributeValue<OptionSetValue>("mode")?.Value!=0 ||
           !string.IsNullOrEmpty(rows[0].GetAttributeValue<string>("filteringattributes")))
            throw new Exception("Request decision guard differs.");
        var handler=Client.Retrieve("plugintype",rows[0].GetAttributeValue<EntityReference>("eventhandler").Id,
            new ColumnSet("typename","pluginassemblyid"));
        if(handler.GetAttributeValue<string>("typename")!="Leave.Plugins.LeaveRequestGuard")
            throw new Exception("Request guard handler differs.");
        var assembly=Client.Retrieve("pluginassembly",
            handler.GetAttributeValue<EntityReference>("pluginassemblyid").Id,
            new ColumnSet("name","publickeytoken"));
        var path=Root+"/server/Leave.Plugins/bin/Release/net462/Leave.Plugins.dll";
        var name=AssemblyName.GetAssemblyName(path);
        if(assembly.GetAttributeValue<string>("name")!=name.Name ||
           assembly.GetAttributeValue<string>("publickeytoken")!=
           Convert.ToHexString(name.GetPublicKeyToken()!).ToLowerInvariant())
            throw new Exception("Signed request guard identity differs.");
        var bytes=File.ReadAllBytes(path);
        Client.Update(new Entity("pluginassembly",assembly.Id) {
            ["content"]=Convert.ToBase64String(bytes)
        });
        Console.WriteLine("PASS enabled Sick Leave approval guard after secured grant and accounting checks; main SHA256="+
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()+". Live Sick decision test remains required.");
    }

    static void SickApprovalFixtureTest() {
        RequireFixtureNotificationExclusion();
        var reason="FICTIONAL Sick accounting test; uploaded synthetic evidence, not a real employee absence.";
        var employee=Client.Retrieve(Employee,AccountingEmployee,
            new ColumnSet("cr2c9_entrauserid","cr2c9_datejoined","cr2c9_employmenttype"));
        if(employee.GetAttributeValue<OptionSetValue>("cr2c9_employmenttype")?.Value!=290040000 ||
           !employee.Contains("cr2c9_datejoined"))
            throw new Exception("Fictional accounting employee is not eligible; no test change.");
        var grant=GrantSick(AccountingEmployee);
        if((string)grant["Status"]!="Granted" && (string)grant["Status"]!="AlreadyGranted")
            throw new Exception("Fictional Sick grant is held: "+grant["Status"]);
        var before=(int)grant["AvailableHours"];
        if(before<8)throw new Exception("Fictional Sick balance is insufficient for the test.");
        // Grant locks the employee and increments RowVersion. Read it again
        // before the identity-scoped fixture transaction.
        employee=Client.Retrieve(Employee,AccountingEmployee,
            new ColumnSet("cr2c9_entrauserid","cr2c9_datejoined","cr2c9_employmenttype"));
        var existing=Find(Request,"cr2c9_reason",reason,"cr2c9_status","cr2c9_employeename");
        if(existing.Length>1)throw new Exception("More than one fictional Sick request; no repeat test.");
        Guid id;
        if(existing.Length==0) {
            var ownId=Guid.Parse("6938b1f2-22c2-f111-aaad-000d3a826b69");
            var own=Client.Retrieve(Employee,ownId,new ColumnSet("cr2c9_entrauserid"));
            var actor=Client.Retrieve("systemuser",((WhoAmIResponse)Client.Execute(new WhoAmIRequest())).UserId,
                new ColumnSet("azureactivedirectoryobjectid"));
            var actorEntra=actor.GetAttributeValue<Guid>("azureactivedirectoryobjectid");
            if(!Guid.TryParse(own.GetAttributeValue<string>("cr2c9_entrauserid"),out var ownEntra) ||
               ownEntra!=actorEntra || !Guid.TryParse(employee.GetAttributeValue<string>("cr2c9_entrauserid"),out var fixtureEntra) ||
               fixtureEntra==actorEntra)
                throw new Exception("Development identity fixture differs; no test change.");
            var today=DateTime.UtcNow.AddHours(8).Date;
            var first=today.AddDays(4);
            while(first.DayOfWeek!=DayOfWeek.Monday)first=first.AddDays(1);
            if(first.Year!=today.Year)throw new Exception("Test date crosses year; no test change.");
            id=Guid.NewGuid();
            var request=new Entity(Request,id) {
                ["cr2c9_employeename"]=new EntityReference(Employee,AccountingEmployee),
                ["cr2c9_leavetypename"]=new EntityReference("cr2c9_leavetype",SickLeaveType),
                ["cr2c9_status"]=new OptionSetValue(0),
                ["cr2c9_startdate"]=first,["cr2c9_enddate"]=first,
                ["cr2c9_hoursperday"]=8m,["cr2c9_reason"]=reason
            };
            var operations=new OrganizationRequestCollection {
                new UpdateRequest {Target=new Entity(Employee,ownId) {
                    RowVersion=own.RowVersion,["cr2c9_entrauserid"]=Guid.NewGuid().ToString("D")
                },ConcurrencyBehavior=ConcurrencyBehavior.IfRowVersionMatches},
                new UpdateRequest {Target=new Entity(Employee,AccountingEmployee) {
                    RowVersion=employee.RowVersion,["cr2c9_entrauserid"]=actorEntra.ToString("D")
                },ConcurrencyBehavior=ConcurrencyBehavior.IfRowVersionMatches},
                new CreateRequest {Target=request},
                new UpdateRequest {Target=new Entity(Employee,AccountingEmployee) {
                    ["cr2c9_entrauserid"]=fixtureEntra.ToString("D")
                }},
                new UpdateRequest {Target=new Entity(Employee,ownId) {
                    ["cr2c9_entrauserid"]=actorEntra.ToString("D")
                }}
            };
            try {
                Client.Execute(operations[0]);
                Client.Execute(operations[1]);
                Client.Execute(operations[2]);
            } finally {
                // The request's date-generation plug-in needs the ordinary
                // single-message pipeline. Restore both identities even when
                // its nested validation rejects the fictional submission.
                var restore=new OrganizationRequestCollection {
                    operations[3],operations[4]
                };
                Client.Execute(new ExecuteTransactionRequest {Requests=restore,ReturnResponses=false});
            }
            var restored=Client.Retrieve(Employee,ownId,new ColumnSet("cr2c9_entrauserid"));
            if(restored.GetAttributeValue<string>("cr2c9_entrauserid")!=actorEntra.ToString("D"))
                throw new Exception("Development identity was not restored; stop test.");
            var rows=Find("cr2c9_leaverequestdate","cr2c9_leaverequest",id,
                "cr2c9_leavedate","cr2c9_duration");
            if(rows.Length!=1 || rows[0].GetAttributeValue<DateTime>("cr2c9_leavedate")!=first ||
               rows[0].GetAttributeValue<OptionSetValue>("cr2c9_duration")?.Value!=290040001)
                throw new Exception("Synthetic Sick request did not generate exactly one 8-hour weekday.");
            Console.WriteLine("PASS fictional timely Sick request "+id+" saved with one 8-hour date; Entra identities restored.");
        } else {
            id=existing[0].Id;
            if(existing[0].GetAttributeValue<EntityReference>("cr2c9_employeename")?.Id!=AccountingEmployee ||
               existing[0].GetAttributeValue<OptionSetValue>("cr2c9_status")?.Value!=0)
                throw new Exception("Existing fictional Sick fixture is no longer pending.");
        }
        Reject("Sick approval without certificate",()=>Client.Update(AccountingDecision(id)),"Upload a medical certificate");
        if(Find(BalanceEntryTable,"cr2c9_leaverequest",id).Length!=0 || Find(History,"cr2c9_leaverequest",id).Length!=0)
            throw new Exception("Rejected evidence check left a debit or decision.");
        var proof=System.Text.Encoding.UTF8.GetBytes("Fictional test evidence only; no medical information.\n");
        var notes=Find("annotation","objectid",id,"filename","filesize","isdocument");
        if(notes.Length==0)Client.Create(new Entity("annotation") {
            ["objectid"]=new EntityReference(Request,id),
            ["subject"]="Fictional Sick evidence test",
            ["filename"]="fictional-sick-evidence.txt",
            ["mimetype"]="text/plain",
            ["documentbody"]=Convert.ToBase64String(proof),
            ["isdocument"]=true
        });
        notes=Find("annotation","objectid",id,"filename","filesize","isdocument");
        if(notes.Length!=1 || !notes[0].GetAttributeValue<bool>("isdocument") ||
           notes[0].GetAttributeValue<int>("filesize")<=0)
            throw new Exception("Synthetic evidence did not persist as a Dataverse file Note.");
        Client.Update(AccountingDecision(id));
        var saved=Client.Retrieve(Request,id,new ColumnSet("cr2c9_status"));
        var debits=Find(BalanceEntryTable,"cr2c9_leaverequest",id,
            "cr2c9_hours","cr2c9_entrykind");
        var history=Find(History,"cr2c9_leaverequest",id);
        var after=GrantSick(AccountingEmployee);
        if(saved.GetAttributeValue<OptionSetValue>("cr2c9_status")?.Value!=1 ||
           debits.Length!=1 || debits[0].GetAttributeValue<int>("cr2c9_hours")!=-8 ||
           debits[0].GetAttributeValue<OptionSetValue>("cr2c9_entrykind")?.Value!=290040001 ||
           history.Length!=1 || (int)after["AvailableHours"]!=before-8 ||
           (int)after["CreditedHours"]!=0)
            throw new Exception("Sick approval, history, debit or grant retry did not reconcile.");
        Console.WriteLine("PASS fictional Sick approval: one decision, one -8h debit, balance "+
            before+" -> "+after["AvailableHours"]+"; repeat grant added zero hours. No real medical data used.");
    }
}
