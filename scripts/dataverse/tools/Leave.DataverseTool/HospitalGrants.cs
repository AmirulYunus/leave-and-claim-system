using System.Reflection;
using System.Security.Cryptography;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Crm.Sdk.Messages;

internal static partial class Development
{
    const string HospitalGrantApi = "cr2c9_GrantCalendarHospitalisationLeave";
    static readonly Guid HospitalLeaveType = Guid.Parse("91f9367f-5bd7-4a06-a750-124c36d5729f");

    static void HospitalGrantDeploy() {
        var leaveType=Client.Retrieve("cr2c9_leavetype",HospitalLeaveType,
            new ColumnSet("cr2c9_leavetypename","statecode"));
        if (leaveType.GetAttributeValue<string>("cr2c9_leavetypename")!="Hospitalisation Leave" ||
            leaveType.GetAttributeValue<OptionSetValue>("statecode")?.Value!=0)
            throw new Exception("Configured Hospitalisation Leave category differs; no deployment.");
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
        var guardSteps=Find("sdkmessageprocessingstep","eventhandler",guard.Id,
            "stage","mode","statecode","filteringattributes");
        if(guardSteps.Length!=14 || guardSteps.Any(x=>x.GetAttributeValue<OptionSetValue>("stage")?.Value!=20 ||
           x.GetAttributeValue<OptionSetValue>("mode")?.Value!=0 ||
           x.GetAttributeValue<OptionSetValue>("statecode")?.Value!=0 ||
           x.GetAttributeValue<string>("filteringattributes")!=null))
            throw new Exception("Protected balance write guards differ.");
        var approval=Find("sdkmessageprocessingstep","name",AccountingStep,
            "stage","mode","statecode","filteringattributes").Single();
        if(approval.GetAttributeValue<OptionSetValue>("stage")?.Value!=40 ||
           approval.GetAttributeValue<OptionSetValue>("mode")?.Value!=0 ||
           approval.GetAttributeValue<OptionSetValue>("statecode")?.Value!=0 ||
           approval.GetAttributeValue<string>("filteringattributes")!="cr2c9_status")
            throw new Exception("Atomic approval accounting differs.");
        var images=Find("sdkmessageprocessingstepimage","sdkmessageprocessingstepid",approval.Id,"entityalias");
        if(images.Length!=1 || images[0].GetAttributeValue<string>("entityalias")!="BeforeApproval")
            throw new Exception("Approval pre-image differs.");
        var path=Root+"/server/Leave.BalanceGuards/bin/Release/net462/Leave.BalanceGuards.dll";
        var bytes=File.ReadAllBytes(path);
        if(assembly.GetAttributeValue<string>("publickeytoken")!=
           Convert.ToHexString(AssemblyName.GetAssemblyName(path).GetPublicKeyToken()!).ToLowerInvariant())
            throw new Exception("Signed balance assembly differs.");
        var privilege=((RetrieveEntityResponse)Client.Execute(new RetrieveEntityRequest {
            LogicalName=BalancePeriodTable,EntityFilters=EntityFilters.Privileges
        })).EntityMetadata.Privileges.Single(x=>x.PrivilegeType==PrivilegeType.Create).Name;
        if(privilege!="prvCreatecr2c9_leavebalanceperiod")throw new Exception("Unexpected grant privilege.");
        var existing=Find("customapi","uniquename",HospitalGrantApi,
            "isfunction","bindingtype","boundentitylogicalname","executeprivilegename");
        if(existing.Length>1)throw new Exception("Duplicate Hospitalisation grant action.");
        if(existing.Length==1 && (existing[0].GetAttributeValue<bool>("isfunction") ||
           existing[0].GetAttributeValue<OptionSetValue>("bindingtype")?.Value!=1 ||
           existing[0].GetAttributeValue<string>("boundentitylogicalname")!=Employee ||
           existing[0].GetAttributeValue<string>("executeprivilegename")!=privilege))
            throw new Exception("Existing Hospitalisation grant contract differs.");
        // The request guard still blocks approval until a later explicit enable step.
        Client.Update(new Entity("pluginassembly",assembly.Id) {
            ["content"]=Convert.ToBase64String(bytes),
            ["description"]="Protected Annual, Sick and Hospitalisation grants, atomic deductions and exact reversals."
        });
        const string typename="Leave.BalanceGuards.CalendarHospitalGrantOperation";
        var types=Find("plugintype","typename",typename,"pluginassemblyid");
        if(types.Length>1 || types.Length==1 &&
           types[0].GetAttributeValue<EntityReference>("pluginassemblyid")?.Id!=assembly.Id)
            throw new Exception("Hospitalisation grant plug-in identity differs.");
        var type=types.Length==1?types[0].Id:Client.Create(new Entity("plugintype") {
            ["pluginassemblyid"]=assembly.ToEntityReference(),["typename"]=typename,
            ["name"]=typename,["friendlyname"]=typename
        });
        Guid api;
        if(existing.Length==0) api=CreateInSolution(new Entity("customapi") {
            ["uniquename"]=HospitalGrantApi,["name"]=HospitalGrantApi,
            ["displayname"]="Grant current calendar Hospitalisation Leave",
            ["description"]="HR-only 60-day Hospitalisation grant; Medical use is included in the company combined balance reporting.",
            ["isfunction"]=false,["isprivate"]=false,
            ["bindingtype"]=new OptionSetValue(1),["boundentitylogicalname"]=Employee,
            ["allowedcustomprocessingsteptype"]=new OptionSetValue(0),
            ["workflowsdkstepenabled"]=true,["executeprivilegename"]=privilege,
            ["plugintypeid"]=new EntityReference("plugintype",type)
        });
        else {
            api=existing[0].Id;
            Client.Update(new Entity("customapi",api){["plugintypeid"]=new EntityReference("plugintype",type)});
        }
        foreach(var prop in new[]{("Status",10),("Reason",10),("PeriodStart",10),("PeriodEnd",10),("CreditedHours",7),("AvailableHours",7)})
            if(!Find("customapiresponseproperty","customapiid",api,"uniquename")
                .Any(x=>x.GetAttributeValue<string>("uniquename")==prop.Item1))
                CreateInSolution(new Entity("customapiresponseproperty") {
                    ["customapiid"]=new EntityReference("customapi",api),
                    ["uniquename"]=prop.Item1,["name"]=HospitalGrantApi+"."+prop.Item1,
                    ["displayname"]=prop.Item1,["type"]=new OptionSetValue(prop.Item2)
                });
        Console.WriteLine("PASS Hospitalisation grant action "+api+"; assembly SHA256="+
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()+". Approval guard remains blocked.");
    }

    static void HospitalApprovalEnable() {
        var api=Find("customapi","uniquename",HospitalGrantApi,"plugintypeid").SingleOrDefault();
        if(api==null || api.GetAttributeValue<EntityReference>("plugintypeid")?.Id==Guid.Empty)
            throw new Exception("Secured Hospitalisation grant action is missing.");
        var period=Find(BalancePeriodTable,"cr2c9_employee",FixtureEmployee,
            "cr2c9_leavetype","cr2c9_periodstart").SingleOrDefault(x=>
                x.GetAttributeValue<EntityReference>("cr2c9_leavetype")?.Id==HospitalLeaveType &&
                x.GetAttributeValue<DateTime>("cr2c9_periodstart").Year==DateTime.UtcNow.AddHours(8).Year);
        if(period==null || Find(BalanceEntryTable,"cr2c9_balanceperiod",period.Id).Length==0)
            throw new Exception("Fictional Hospitalisation grant has not passed live persistence verification.");
        var approval=Find("sdkmessageprocessingstep","name",AccountingStep,
            "statecode","stage","mode","filteringattributes").Single();
        if(approval.GetAttributeValue<OptionSetValue>("statecode")?.Value!=0 ||
           approval.GetAttributeValue<OptionSetValue>("stage")?.Value!=40 ||
           approval.GetAttributeValue<OptionSetValue>("mode")?.Value!=0 ||
           approval.GetAttributeValue<string>("filteringattributes")!="cr2c9_status")
            throw new Exception("Atomic approval step differs.");
        var guard=Find("plugintype","typename","Leave.BalanceGuards.BalanceStagingGuard").Single();
        var steps=Find("sdkmessageprocessingstep","eventhandler",guard.Id,"statecode");
        if(steps.Length!=14 || steps.Any(x=>x.GetAttributeValue<OptionSetValue>("statecode")?.Value!=0))
            throw new Exception("Protected ledger guards are not all active.");
        var row=Find("sdkmessageprocessingstep","name","Leave safeguard: "+Request+" Update",
            "statecode","stage","mode","filteringattributes","eventhandler").Single();
        if(row.GetAttributeValue<OptionSetValue>("statecode")?.Value!=0 ||
           row.GetAttributeValue<OptionSetValue>("stage")?.Value!=20 ||
           row.GetAttributeValue<OptionSetValue>("mode")?.Value!=0 ||
           !string.IsNullOrEmpty(row.GetAttributeValue<string>("filteringattributes")))
            throw new Exception("Request decision guard differs.");
        var handler=Client.Retrieve("plugintype",row.GetAttributeValue<EntityReference>("eventhandler").Id,
            new ColumnSet("typename","pluginassemblyid"));
        if(handler.GetAttributeValue<string>("typename")!="Leave.Plugins.LeaveRequestGuard")
            throw new Exception("Request guard handler differs.");
        var assembly=Client.Retrieve("pluginassembly",handler.GetAttributeValue<EntityReference>("pluginassemblyid").Id,
            new ColumnSet("name","publickeytoken"));
        var path=Root+"/server/Leave.Plugins/bin/Release/net462/Leave.Plugins.dll";
        var name=AssemblyName.GetAssemblyName(path);
        if(assembly.GetAttributeValue<string>("name")!=name.Name ||
           assembly.GetAttributeValue<string>("publickeytoken")!=
           Convert.ToHexString(name.GetPublicKeyToken()!).ToLowerInvariant())
            throw new Exception("Signed request guard identity differs.");
        var bytes=File.ReadAllBytes(path);
        Client.Update(new Entity("pluginassembly",assembly.Id){["content"]=Convert.ToBase64String(bytes)});
        Console.WriteLine("PASS Hospitalisation approval enabled; main assembly SHA256="+
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
    }

    static void HospitalGrantFixtureTest() {
        var first=Client.Execute(new OrganizationRequest(HospitalGrantApi){["Target"]=new EntityReference(Employee,FixtureEmployee)});
        var second=Client.Execute(new OrganizationRequest(HospitalGrantApi){["Target"]=new EntityReference(Employee,FixtureEmployee)});
        if((string)second["Status"]!="AlreadyGranted" || (int)second["CreditedHours"]!=0 ||
           (int)first["AvailableHours"]!=(int)second["AvailableHours"])
            throw new Exception("Hospitalisation fixture grant retry was not idempotent.");
        var periods=Find(BalancePeriodTable,"cr2c9_employee",FixtureEmployee,
            "cr2c9_leavetype","cr2c9_periodstart").Where(x=>
                x.GetAttributeValue<EntityReference>("cr2c9_leavetype")?.Id==HospitalLeaveType &&
                x.GetAttributeValue<DateTime>("cr2c9_periodstart").Year==DateTime.UtcNow.AddHours(8).Year).ToArray();
        if(periods.Length!=1)throw new Exception("Expected one fictional Hospitalisation period.");
        var entries=Find(BalanceEntryTable,"cr2c9_balanceperiod",periods[0].Id,
            "cr2c9_entrykind","cr2c9_hours","cr2c9_operationkey");
        if(entries.Count(x=>x.GetAttributeValue<OptionSetValue>("cr2c9_entrykind")?.Value==290040000)!=1 ||
           entries.Sum(x=>x.GetAttributeValue<int>("cr2c9_hours"))!=480)
            throw new Exception("Fictional Hospitalisation balance does not equal 480 hours.");
        Console.WriteLine("PASS fictional Hospitalisation grant idempotency: period="+periods[0].Id+
            " available="+second["AvailableHours"]+" hours.");
    }

    static void HospitalApprovalFixtureTest() {
        RequireFixtureNotificationExclusion();
        var reason="FICTIONAL Hospitalisation accounting test; uploaded synthetic evidence, not a real employee absence.";
        var employee=Client.Retrieve(Employee,AccountingEmployee,
            new ColumnSet("cr2c9_entrauserid","cr2c9_datejoined","cr2c9_employmenttype"));
        if(employee.GetAttributeValue<OptionSetValue>("cr2c9_employmenttype")?.Value!=290040000 ||
           !employee.Contains("cr2c9_datejoined"))
            throw new Exception("Fictional accounting employee is not eligible; no test change.");
        var grant=Client.Execute(new OrganizationRequest(HospitalGrantApi){["Target"]=new EntityReference(Employee,AccountingEmployee)});
        if((string)grant["Status"]!="Granted" && (string)grant["Status"]!="AlreadyGranted")
            throw new Exception("Fictional Hospitalisation grant is held: "+grant["Status"]);
        var before=(int)grant["AvailableHours"];
        if(before<8)throw new Exception("Fictional Hospitalisation balance is insufficient for the test.");
        // Grant locks the employee and increments RowVersion. Read it again
        // before the identity-scoped fixture transaction.
        employee=Client.Retrieve(Employee,AccountingEmployee,
            new ColumnSet("cr2c9_entrauserid","cr2c9_datejoined","cr2c9_employmenttype"));
        var existing=Find(Request,"cr2c9_reason",reason,"cr2c9_status","cr2c9_employeename");
        if(existing.Length>1)throw new Exception("More than one fictional Hospitalisation request; no repeat test.");
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
                ["cr2c9_leavetypename"]=new EntityReference("cr2c9_leavetype",HospitalLeaveType),
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
                throw new Exception("Synthetic Hospitalisation request did not generate exactly one 8-hour weekday.");
            Console.WriteLine("PASS fictional Hospitalisation request "+id+" saved with one 8-hour date; Entra identities restored.");
        } else {
            id=existing[0].Id;
            if(existing[0].GetAttributeValue<EntityReference>("cr2c9_employeename")?.Id!=AccountingEmployee ||
               existing[0].GetAttributeValue<OptionSetValue>("cr2c9_status")?.Value!=0)
                throw new Exception("Existing fictional Hospitalisation fixture is no longer pending.");
        }
        Reject("Hospitalisation approval without certificate",()=>Client.Update(AccountingDecision(id)),"Upload a medical certificate");
        if(Find(BalanceEntryTable,"cr2c9_leaverequest",id).Length!=0 || Find(History,"cr2c9_leaverequest",id).Length!=0)
            throw new Exception("Rejected evidence check left a debit or decision.");
        var proof=System.Text.Encoding.UTF8.GetBytes("Fictional test evidence only; no medical information.\n");
        var notes=Find("annotation","objectid",id,"filename","filesize","isdocument");
        if(notes.Length==0)Client.Create(new Entity("annotation") {
            ["objectid"]=new EntityReference(Request,id),
            ["subject"]="Fictional Hospitalisation evidence test",
            ["filename"]="fictional-hospitalisation-evidence.txt",
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
        var after=Client.Execute(new OrganizationRequest(HospitalGrantApi){["Target"]=new EntityReference(Employee,AccountingEmployee)});
        if(saved.GetAttributeValue<OptionSetValue>("cr2c9_status")?.Value!=1 ||
           debits.Length!=1 || debits[0].GetAttributeValue<int>("cr2c9_hours")!=-8 ||
           debits[0].GetAttributeValue<OptionSetValue>("cr2c9_entrykind")?.Value!=290040001 ||
           history.Length!=1 || (int)after["AvailableHours"]!=before-8 ||
           (int)after["CreditedHours"]!=0)
            throw new Exception("Hospitalisation approval, history, debit or grant retry did not reconcile.");
        Console.WriteLine("PASS fictional Hospitalisation approval: one decision, one -8h debit, balance "+
            before+" -> "+after["AvailableHours"]+"; repeat grant added zero hours. No real medical data used.");
    }

    static void HospitalCancellationFixtureTest() {
        RequireFixtureNotificationExclusion();
        var reason="FICTIONAL Hospitalisation accounting test; uploaded synthetic evidence, not a real employee absence.";
        var approved=Find(Request,"cr2c9_reason",reason,"cr2c9_status","cr2c9_employeename").Single();
        if(approved.GetAttributeValue<EntityReference>("cr2c9_employeename")?.Id!=AccountingEmployee ||
           approved.GetAttributeValue<OptionSetValue>("cr2c9_status")?.Value!=1)
            throw new Exception("Only the approved fictional Hospitalisation request may be cancelled.");
        var pending=Find(Cancellation,"cr2c9_leaverequest",approved.Id,"cr2c9_status");
        if(pending.Length>1 || pending.Any(x=>x.GetAttributeValue<OptionSetValue>("cr2c9_status")?.Value!=0))
            throw new Exception("Fictional Hospitalisation cancellation already decided or duplicated.");
        var period=Find(BalancePeriodTable,"cr2c9_employee",AccountingEmployee,
            "cr2c9_leavetype","cr2c9_periodstart").Single(x=>
                x.GetAttributeValue<EntityReference>("cr2c9_leavetype")?.Id==HospitalLeaveType &&
                x.GetAttributeValue<DateTime>("cr2c9_periodstart").Year==DateTime.UtcNow.AddHours(8).Year);
        int Available()=>Find(BalanceEntryTable,"cr2c9_balanceperiod",period.Id,"cr2c9_hours")
            .Sum(x=>x.GetAttributeValue<int>("cr2c9_hours"));
        var before=Available();
        if(before!=472)throw new Exception("Expected fictional Hospitalisation balance of 472 hours before cancellation.");
        var id=pending.Length==1?pending[0].Id:CreateFictionalCancellation(approved.Id,
            "Fictional future Hospitalisation cancellation and exact reversal test.");
        if(Available()!=before)throw new Exception("Pending cancellation changed the Hospitalisation balance.");
        var row=Client.Retrieve(Cancellation,id,new ColumnSet("cr2c9_status"));
        Client.Update(new Entity(Cancellation,id){["cr2c9_status"]=new OptionSetValue(1),
            ["cr2c9_expectedversion"]=row.RowVersion,
            ["cr2c9_decisioncomments"]="Fictional Hospitalisation exact-reversal test."});
        var after=Client.Retrieve(Cancellation,id,new ColumnSet("cr2c9_status"));
        var original=Client.Retrieve(Request,approved.Id,new ColumnSet("cr2c9_status"));
        var entries=Find(BalanceEntryTable,"cr2c9_leaverequest",approved.Id,
            "cr2c9_entrykind","cr2c9_hours","cr2c9_reverses");
        var debit=entries.SingleOrDefault(x=>x.GetAttributeValue<OptionSetValue>("cr2c9_entrykind")?.Value==290040001);
        var reversal=entries.SingleOrDefault(x=>x.GetAttributeValue<OptionSetValue>("cr2c9_entrykind")?.Value==290040002);
        if(after.GetAttributeValue<OptionSetValue>("cr2c9_status")?.Value!=1 ||
           original.GetAttributeValue<OptionSetValue>("cr2c9_status")?.Value!=5 || entries.Length!=2 ||
           debit?.GetAttributeValue<int>("cr2c9_hours")!=-8 || reversal?.GetAttributeValue<int>("cr2c9_hours")!=8 ||
           reversal.GetAttributeValue<EntityReference>("cr2c9_reverses")?.Id!=debit.Id || Available()!=480)
            throw new Exception("Hospitalisation cancellation did not restore exactly eight hours.");
        Reject("Hospitalisation cancellation retry",()=>Client.Update(new Entity(Cancellation,id){
            ["cr2c9_status"]=new OptionSetValue(1),["cr2c9_expectedversion"]=after.RowVersion}),
            "Only a pending cancellation");
        if(Available()!=480 || Find(BalanceEntryTable,"cr2c9_leaverequest",approved.Id).Length!=2)
            throw new Exception("Hospitalisation cancellation retry wrote another reversal.");
        Console.WriteLine("PASS fictional Hospitalisation cancellation: exact +8h once; 472 -> 480 hours.");
    }
}
