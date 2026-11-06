using System.Text.Json.Nodes;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Crm.Sdk.Messages;

internal static partial class Development
{
    static Guid SuperiorFormFixture()
    {
        RequireFixtureNotificationExclusion();
        var flow=Client.Retrieve("workflow",HrNotifyFlow,new ColumnSet("clientdata","statecode"));
        var json=JsonNode.Parse(flow.GetAttributeValue<string>("clientdata"))!;
        var trigger=json["properties"]!["definition"]!["triggers"]!.AsObject().Single().Value!;
        var conditions=trigger["conditions"] as JsonArray ?? throw new Exception("Existing HR notification conditions are missing.");
        var exclusion="@not(equals(triggerOutputs()?['body/_cr2c9_employeename_value'], '"+FixtureEmployee+"'))";
        if (!conditions.Any(x=>x?["expression"]?.GetValue<string>()==exclusion)) {
            conditions.Add(new JsonObject{["expression"]=exclusion});
            Client.Update(new Entity("workflow",HrNotifyFlow){["statecode"]=new OptionSetValue(0)});
            Client.Update(new Entity("workflow",HrNotifyFlow){["clientdata"]=json.ToJsonString()});
            Client.Update(new Entity("workflow",HrNotifyFlow){["statecode"]=new OptionSetValue(1)});
        }
        flow=Client.Retrieve("workflow",HrNotifyFlow,new ColumnSet("clientdata","statecode"));
        var verified=JsonNode.Parse(flow.GetAttributeValue<string>("clientdata"))!["properties"]!["definition"]!["triggers"]!.AsObject().Single().Value!["conditions"] as JsonArray;
        if (flow.GetAttributeValue<OptionSetValue>("statecode")?.Value!=1 || verified==null ||
            !verified.Any(x=>x?["expression"]?.GetValue<string>()==exclusion))
            throw new Exception("Fictional form fixture must be excluded from HR email before submission.");
        var ownId=Guid.Parse("6938b1f2-22c2-f111-aaad-000d3a826b69");
        var who=((WhoAmIResponse)Client.Execute(new WhoAmIRequest())).UserId;
        var actor=Client.Retrieve("systemuser",who,new ColumnSet("azureactivedirectoryobjectid"));
        var identity=actor.GetAttributeValue<Guid>("azureactivedirectoryobjectid").ToString("D");
        var own=Client.Retrieve(Employee,ownId,new ColumnSet("cr2c9_entrauserid"));
        var fixture=Client.Retrieve(Employee,FixtureEmployee,new ColumnSet("cr2c9_entrauserid","cr2c9_superior","cr2c9_isactive"));
        if (own.GetAttributeValue<string>("cr2c9_entrauserid")!=identity ||
            fixture.GetAttributeValue<EntityReference>("cr2c9_superior")?.Id!=ownId ||
            !fixture.GetAttributeValue<bool>("cr2c9_isactive") ||
            !Guid.TryParse(fixture.GetAttributeValue<string>("cr2c9_entrauserid"),out var fixtureIdentity) || fixtureIdentity==Guid.Parse(identity))
            throw new Exception("The fictional fixture identity or assigned Superior differs; stop before swapping identities.");
        var balance=Find(BalancePeriodTable,"cr2c9_employee",FixtureEmployee,"cr2c9_employee","cr2c9_leavetype")
            .Where(x=>x.GetAttributeValue<EntityReference>("cr2c9_leavetype")?.Id==Guid.Parse("959c93de-1197-4de4-a660-2e2c3266f0fe")).ToArray();
        if (balance.Length!=1) throw new Exception("The fictional employee needs one granted calendar balance.");
        var available=Find(BalanceEntryTable,"cr2c9_balanceperiod",balance[0].Id,"cr2c9_hours").Sum(e=>e.GetAttributeValue<int>("cr2c9_hours"));
        if (available<8) throw new Exception("The fictional balance is too low for a form approval test.");
        var day=new DateTime(2026,11,30);
        var id=Guid.NewGuid();
        var operations=new OrganizationRequestCollection {
            new UpdateRequest{Target=new Entity(Employee,ownId){RowVersion=own.RowVersion,["cr2c9_entrauserid"]=Guid.NewGuid().ToString("D")},ConcurrencyBehavior=ConcurrencyBehavior.IfRowVersionMatches},
            new UpdateRequest{Target=new Entity(Employee,FixtureEmployee){RowVersion=fixture.RowVersion,["cr2c9_entrauserid"]=identity},ConcurrencyBehavior=ConcurrencyBehavior.IfRowVersionMatches},
            new CreateRequest{Target=new Entity(Request,id){["cr2c9_employeename"]=new EntityReference(Employee,FixtureEmployee),
                ["cr2c9_leavetypename"]=new EntityReference("cr2c9_leavetype",Guid.Parse("959c93de-1197-4de4-a660-2e2c3266f0fe")),
                ["cr2c9_startdate"]=day,["cr2c9_enddate"]=day,["cr2c9_status"]=new OptionSetValue(0),
                ["cr2c9_hoursperday"]=8m,
                ["cr2c9_reason"]="Fictional published Superior form verification; HR email excluded."}},
            new UpdateRequest{Target=new Entity(Employee,FixtureEmployee){["cr2c9_entrauserid"]=fixture["cr2c9_entrauserid"]}},
            new UpdateRequest{Target=new Entity(Employee,ownId){["cr2c9_entrauserid"]=identity}}
        };
        try {
            foreach(var operation in operations.Take(operations.Count-2)) Client.Execute(operation);
        } finally {
            // An individual SDK call retains the signed-in caller in the date
            // guard. Restore the fictional identity before the real profile.
            Client.Execute(new ExecuteTransactionRequest{Requests=new OrganizationRequestCollection {
                operations[operations.Count-2],operations[operations.Count-1]
            },ReturnResponses=false});
        }
        if (Client.Retrieve(Employee,ownId,new ColumnSet("cr2c9_entrauserid")).GetAttributeValue<string>("cr2c9_entrauserid")!=identity ||
            Client.Retrieve(Employee,FixtureEmployee,new ColumnSet("cr2c9_entrauserid")).GetAttributeValue<string>("cr2c9_entrauserid")!=fixture.GetAttributeValue<string>("cr2c9_entrauserid"))
            throw new Exception("Fictional submission did not restore both identities.");
        // The maker's Entra ID is temporarily replaced during submission, so
        // the sharing step cannot resolve that manager at Create time. Reconcile
        // after restoration, as the employee-sync manager update also does.
        Client.Update(new Entity(Employee,FixtureEmployee) {
            ["cr2c9_superior"]=new EntityReference(Employee,ownId)
        });
        Console.WriteLine("PASS form test fixture pending and Superior access reconciled, one future 8-hour date, available before="+available+"; request="+id+"; HR email excluded.");
        return id;
    }
    static void SuperiorFormVerify()
    {
        var id=Guid.Parse("f0ffcfcc-aed7-4407-9ba6-86a17c44708d");
        var request=Client.Retrieve(Request,id,new ColumnSet("cr2c9_status","cr2c9_employeename","cr2c9_decisionbyentraid","cr2c9_decisionon"));
        var actor=Client.Retrieve("systemuser",((WhoAmIResponse)Client.Execute(new WhoAmIRequest())).UserId,new ColumnSet("azureactivedirectoryobjectid"));
        if (request.GetAttributeValue<OptionSetValue>("cr2c9_status")?.Value!=1 ||
            request.GetAttributeValue<EntityReference>("cr2c9_employeename")?.Id!=FixtureEmployee ||
            request.GetAttributeValue<string>("cr2c9_decisionbyentraid")!=actor.GetAttributeValue<Guid>("azureactivedirectoryobjectid").ToString("D") ||
            !request.Contains("cr2c9_decisionon")) throw new Exception("Published form did not save the expected verified Superior approval.");
        var history=Find(History,"cr2c9_leaverequest",id,"cr2c9_outcome","cr2c9_operationkey");
        var ledger=Find(BalanceEntryTable,"cr2c9_leaverequest",id,"cr2c9_entrykind","cr2c9_hours","cr2c9_balanceperiod","cr2c9_operationkey");
        var period=Find(BalancePeriodTable,"cr2c9_employee",FixtureEmployee,"cr2c9_employee","cr2c9_leavetype")
            .Single(x=>x.GetAttributeValue<EntityReference>("cr2c9_leavetype")?.Id==Guid.Parse("959c93de-1197-4de4-a660-2e2c3266f0fe"));
        var available=Find(BalanceEntryTable,"cr2c9_balanceperiod",period.Id,"cr2c9_hours").Sum(e=>e.GetAttributeValue<int>("cr2c9_hours"));
        if (history.Length!=1 || history[0].GetAttributeValue<OptionSetValue>("cr2c9_outcome")?.Value!=1 ||
            history[0].GetAttributeValue<string>("cr2c9_operationkey")!=id.ToString("D")+":decision" ||
            ledger.Length!=1 || ledger[0].GetAttributeValue<OptionSetValue>("cr2c9_entrykind")?.Value!=290040001 ||
            ledger[0].GetAttributeValue<int>("cr2c9_hours")!=-8 ||
            ledger[0].GetAttributeValue<EntityReference>("cr2c9_balanceperiod")?.Id!=period.Id ||
            ledger[0].GetAttributeValue<string>("cr2c9_operationkey")!=id.ToString("D")+":approved:"+period.Id.ToString("D") || available!=120)
            throw new Exception("Published form approval history or exact eight-hour debit is missing.");
        Console.WriteLine("PASS published Superior form: Approved, verified decision identity/time, one history, one -8h debit; fictional balance 128→120.");
    }
}
