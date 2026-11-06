using System.ServiceModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Crm.Sdk.Messages;

internal static partial class Development {
    static string FixtureCondition => "@not(equals(triggerOutputs()?['body/_cr2c9_employeename_value'], '"+AccountingEmployee+"'))";
    static void ApprovalNotificationProtect(){
        var flow=Client.Retrieve("workflow",HrNotifyFlow,new ColumnSet("clientdata","statecode"));
        var json=JsonNode.Parse(flow.GetAttributeValue<string>("clientdata"))!;
        var trigger=json["properties"]!["definition"]!["triggers"]!.AsObject().Single().Value!;
        var conditions=trigger["conditions"] as JsonArray;
        if(conditions==null){conditions=new JsonArray();trigger["conditions"]=conditions;}
        if(!conditions.Any(x=>x?["expression"]?.GetValue<string>()==FixtureCondition))conditions.Add(new JsonObject{["expression"]=FixtureCondition});
        Client.Update(new Entity("workflow",HrNotifyFlow){["statecode"]=new OptionSetValue(0)});
        Client.Update(new Entity("workflow",HrNotifyFlow){["clientdata"]=json.ToJsonString()});
        Client.Update(new Entity("workflow",HrNotifyFlow){["statecode"]=new OptionSetValue(1)});
        RequireFixtureNotificationExclusion();
        Console.WriteLine("PASS HR notification flow remains On; dedicated fictional accounting employee excluded by trigger condition. HR recipients unchanged.");
    }
    static void RequireFixtureNotificationExclusion(){
        var flow=Client.Retrieve("workflow",HrNotifyFlow,new ColumnSet("clientdata","statecode"));
        var json=JsonNode.Parse(flow.GetAttributeValue<string>("clientdata"))!;
        var triggers=json["properties"]!["definition"]!["triggers"]!.AsObject();
        if(flow.GetAttributeValue<OptionSetValue>("statecode")?.Value!=1 || triggers.Count!=1 ||
            triggers.Single().Value?["conditions"] is not JsonArray c || !c.Any(x=>x?["expression"]?.GetValue<string>()==FixtureCondition))throw new Exception("Verified test notification exclusion is required before approvals.");
    }
    static void ApprovalFixturePrepare(bool individual = false){
        RequireFixtureNotificationExclusion();
        if(Find(Employee,Employee+"id",AccountingEmployee).Length!=0)throw new Exception("Accounting fixture already exists; inspect results before repeating.");
        var today=DateTime.UtcNow.AddHours(8).Date;
        var actor=Client.Retrieve("systemuser",((WhoAmIResponse)Client.Execute(new WhoAmIRequest())).UserId,new ColumnSet("azureactivedirectoryobjectid"));
        var identity=actor.GetAttributeValue<Guid>("azureactivedirectoryobjectid");var fictionalIdentity=Guid.NewGuid();
        var employee=new Entity(Employee,AccountingEmployee){["cr2c9_employeename"]="Annual accounting test employee (fictional)",["cr2c9_email"]="annual-accounting@example.invalid",
            ["cr2c9_entrauserid"]=identity.ToString(),["cr2c9_isactive"]=true,["cr2c9_superior"]=new EntityReference(Employee,Guid.Parse("6938b1f2-22c2-f111-aaad-000d3a826b69")),
            ["cr2c9_datejoined"]=today.AddYears(-5),["cr2c9_employeelevel"]=new OptionSetValue(290040000),["cr2c9_employmenttype"]=new OptionSetValue(290040000)};
        var ownId=Guid.Parse("6938b1f2-22c2-f111-aaad-000d3a826b69");
        var own=Client.Retrieve(Employee,ownId,new ColumnSet("cr2c9_entrauserid"));
        if(!Guid.TryParse(own.GetAttributeValue<string>("cr2c9_entrauserid"),out var ownIdentity) || ownIdentity!=identity)throw new Exception("Unexpected development Employee identity.");
        var operations=new OrganizationRequestCollection();
        var swap=new Entity(Employee,ownId){RowVersion=own.RowVersion,["cr2c9_entrauserid"]=Guid.NewGuid().ToString()};
        operations.Add(new UpdateRequest{Target=swap,ConcurrencyBehavior=ConcurrencyBehavior.IfRowVersionMatches});
        operations.Add(new CreateRequest{Target=employee});
        var requests=new Dictionary<string,Guid>();
        {
            // One client-side database transaction moves the unique development
            // identity onto the fictional profile only during guarded submissions,
            // then restores both profiles before commit. Any failure rolls back
            // the whole fixture; no sign-in account or guard is changed.
            Guid RequestWithDates(string label,DateTime[] days,int[] hours){
                var start=days.Length==0?new DateTime(today.Year,11,1):days.Min();var end=days.Length==0?start:days.Max();
                var id=Guid.NewGuid();operations.Add(new CreateRequest{Target=new Entity(Request,id){["cr2c9_employeename"]=employee.ToEntityReference(),["cr2c9_leavetypename"]=new EntityReference("cr2c9_leavetype",Guid.Parse("959c93de-1197-4de4-a660-2e2c3266f0fe")),
                    ["cr2c9_status"]=new OptionSetValue(0),["cr2c9_startdate"]=start,["cr2c9_enddate"]=end,["cr2c9_reason"]="Fictional annual accounting test: "+label}});
                for(int i=0;i<days.Length;i++)operations.Add(new CreateRequest{Target=new Entity("cr2c9_leaverequestdate"){["cr2c9_entryname"]="Fictional "+label,["cr2c9_leaverequest"]=new EntityReference(Request,id),["cr2c9_leavedate"]=days[i],["cr2c9_duration"]=new OptionSetValue(hours[i]==4?290040000:290040001)}});
                requests[label]=id;Console.WriteLine("Prepared "+label+" "+id);return id;
            }
            var day=new DateTime(today.Year,11,2);
            RequestWithDates("mixed",new[]{day,day.AddDays(2)},new[]{4,8});
            RequestWithDates("empty",Array.Empty<DateTime>(),Array.Empty<int>());
            RequestWithDates("insufficient",Enumerable.Range(0,17).Select(i=>day.AddDays(i)).ToArray(),Enumerable.Repeat(8,17).ToArray());
            RequestWithDates("rejected",new[]{day},new[]{8});
            RequestWithDates("cross-year",new[]{new DateTime(today.Year,12,31),new DateTime(today.Year+1,1,1)},new[]{4,8});
            foreach(var label in new[]{"race-a","race-b"})RequestWithDates(label,Enumerable.Range(0,10).Select(i=>day.AddDays(i)).ToArray(),Enumerable.Repeat(8,10).ToArray());
            RequestWithDates("exact",Enumerable.Range(0,5).Select(i=>day.AddDays(i)).ToArray(),new[]{8,8,8,8,4});
        }
        operations.Add(new UpdateRequest{Target=new Entity(Employee,AccountingEmployee){["cr2c9_entrauserid"]=fictionalIdentity.ToString()}});
        operations.Add(new UpdateRequest{Target=new Entity(Employee,ownId){["cr2c9_entrauserid"]=own["cr2c9_entrauserid"]}});
        if(!individual) Client.Execute(new ExecuteTransactionRequest{Requests=operations,ReturnResponses=false});
        else {
            try { foreach(var operation in operations.Take(operations.Count-2))Client.Execute(operation); }
            finally {
                // Restore the fictional profile first to release the unique key,
                // then restore the development profile even if setup failed.
                var restore=new OrganizationRequestCollection();
                if(Find(Employee,Employee+"id",AccountingEmployee).Length!=0)restore.Add(operations[operations.Count-2]);
                restore.Add(operations[operations.Count-1]);
                Client.Execute(new ExecuteTransactionRequest{Requests=restore,ReturnResponses=false});
            }
        }
        if(Client.Retrieve(Employee,ownId,new ColumnSet("cr2c9_entrauserid")).GetAttributeValue<string>("cr2c9_entrauserid")!=own.GetAttributeValue<string>("cr2c9_entrauserid"))throw new Exception("Development identity restoration failed.");
        File.WriteAllText(Root+"/annual-accounting-fixtures-2026-10-10.json",JsonSerializer.Serialize(new{employeeId=AccountingEmployee,requests},new JsonSerializerOptions{WriteIndented=true}));
        var grant=Grant(AccountingEmployee);
        if((int)grant["AvailableHours"]!=128)throw new Exception("Expected fictional 128-hour balance.");
        Console.WriteLine("PASS fixture submissions used active guards; fictional identity restored; 128h granted. No approvals yet.");
    }
    static Dictionary<string,Guid> AccountingRequests(){
        using var json=JsonDocument.Parse(File.ReadAllText(Root+"/annual-accounting-fixtures-2026-10-10.json"));
        return json.RootElement.GetProperty("requests").EnumerateObject().ToDictionary(x=>x.Name,x=>x.Value.GetGuid());
    }
    static Entity AccountingDecision(Guid id,int status=1){
        var r=Client.Retrieve(Request,id,new ColumnSet("cr2c9_status","cr2c9_employeename"));
        if(r.GetAttributeValue<EntityReference>("cr2c9_employeename")?.Id!=AccountingEmployee)throw new Exception("Only the fictional accounting fixture may be tested.");
        return new Entity(Request,id){["cr2c9_status"]=new OptionSetValue(status),["cr2c9_decisionexpectedversion"]=r.RowVersion,["cr2c9_decisioncomments"]="Fictional accounting integration test; HR email excluded."};
    }
    static int AccountingBalance()=>Find(BalanceEntryTable,"cr2c9_balanceperiod",Find(BalancePeriodTable,"cr2c9_employee",AccountingEmployee).Single().Id,"cr2c9_hours").Sum(e=>e.GetAttributeValue<int>("cr2c9_hours"));
    static void AssertAccounting(Guid id,int status,int debits,int? balance=null){
        var row=Client.Retrieve(Request,id,new ColumnSet("cr2c9_status","cr2c9_decisionon"));
        var ledger=Find(BalanceEntryTable,"cr2c9_leaverequest",id,"cr2c9_hours");var history=Find(History,"cr2c9_leaverequest",id);
        if(row.GetAttributeValue<OptionSetValue>("cr2c9_status")?.Value!=status || ledger.Length!=debits || history.Length!=(status==0?0:1) || (balance.HasValue && AccountingBalance()!=balance.Value))throw new Exception("Accounting assertion failed for "+id);
        if(status==0 && row.Contains("cr2c9_decisionon"))throw new Exception("Failed approval retained a decision stamp.");
    }
    static void ApprovalAccountingSmoke(){
        RequireFixtureNotificationExclusion();var r=AccountingRequests();
        if(AccountingBalance()!=128)throw new Exception("Smoke test expects untouched fixture grant.");
        foreach(var item in new[]{("empty","Complete per-date"),("insufficient","Insufficient annual"),("cross-year","has not been granted")}){
            var before=Client.Retrieve(Request,r[item.Item1],new ColumnSet(false)).RowVersion;
            Reject(item.Item1+" approval rollback",()=>Client.Update(AccountingDecision(r[item.Item1])),item.Item2);
            AssertAccounting(r[item.Item1],0,0,128);
            if(Client.Retrieve(Request,r[item.Item1],new ColumnSet(false)).RowVersion!=before)throw new Exception("Failed approval did not roll back request lock.");
        }
        Client.Update(AccountingDecision(r["mixed"]));AssertAccounting(r["mixed"],1,1,116);Console.WriteLine("PASS mixed 4/8-hour approval commits one history and -12h debit; balance116h.");
        Reject("repeat approval",()=>Client.Update(AccountingDecision(r["mixed"])),"Existing decisions cannot be repeated");AssertAccounting(r["mixed"],1,1,116);
        Client.Update(AccountingDecision(r["rejected"],3));AssertAccounting(r["rejected"],3,0,116);Console.WriteLine("PASS rejection adds history without deduction.");
        var grant=Grant(AccountingEmployee);if((int)grant["CreditedHours"]!=0 || (int)grant["AvailableHours"]!=116)throw new Exception("Grant retry restored consumed leave.");
        Console.WriteLine("PASS daily grant retry preserves consumed leave.");
    }
    static void ApprovalAccountingRace(){
        RequireFixtureNotificationExclusion();var r=AccountingRequests();if(AccountingBalance()!=116)throw new Exception("Race expects116h.");
        var targets=new[]{AccountingDecision(r["race-a"]),AccountingDecision(r["race-b"])};
        var gate=new ManualResetEventSlim(false);
        var tasks=targets.Select(target=>Task.Run(()=>{gate.Wait();try{Client.Update(target);return "approved";}catch(FaultException<OrganizationServiceFault> e){return e.Detail.Message;}})).ToArray();
        gate.Set();Task.WaitAll(tasks);
        if(tasks.Count(t=>t.Result=="approved")!=1 || AccountingBalance()!=36)throw new Exception("Concurrent approval balance/result mismatch: "+string.Join(" | ",tasks.Select(t=>t.Result)));
        foreach(var target in targets){var row=Client.Retrieve(Request,target.Id,new ColumnSet("cr2c9_status"));var approved=row.GetAttributeValue<OptionSetValue>("cr2c9_status")?.Value==1;AssertAccounting(target.Id,approved?1:0,approved?1:0,36);}
        Console.WriteLine("PASS concurrent80h requests: exactly one approved/debited; other pending without history; balance36h.");
        var pending=targets.Single(t=>Client.Retrieve(Request,t.Id,new ColumnSet("cr2c9_status")).GetAttributeValue<OptionSetValue>("cr2c9_status")?.Value==0);
        Reject("losing concurrent request retry",()=>Client.Update(AccountingDecision(pending.Id)),"Insufficient annual");
        Client.Update(AccountingDecision(r["exact"]));AssertAccounting(r["exact"],1,1,0);Console.WriteLine("PASS exact remaining36h approved; final balance0h.");
        Reject("external debit CreateMultiple",()=>Client.Execute(new OrganizationRequest("CreateMultiple"){["Targets"]=new EntityCollection(new[]{new Entity(BalanceEntryTable){["cr2c9_name"]="Rejected forged debit",["cr2c9_entrykind"]=new OptionSetValue(290040001),["cr2c9_hours"]=-8,["cr2c9_operationkey"]="rejected-forged-debit"}}){EntityName=BalanceEntryTable}}),"only by the authorized");
        var bulk=AccountingDecision(r["insufficient"]);
        Reject("bulk approval rejected by existing identity guard",()=>Client.Execute(new OrganizationRequest("UpdateMultiple"){["Targets"]=new EntityCollection(new[]{bulk}){EntityName=Request}}),"signed-in Entra identity could not be verified");
        AssertAccounting(r["insufficient"],0,0,0);
        Console.WriteLine("PASS ordinary approvals enforce balance accounting; bulk request writes are denied by the existing identity safeguard.");
    }
}
