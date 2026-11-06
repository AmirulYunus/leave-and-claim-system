using System.Reflection;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Crm.Sdk.Messages;

internal static partial class Development
{
    const string Cancellation="cr2c9_leavecancellation";
    static void CancellationSchema()
    {
        var all=((RetrieveAllEntitiesResponse)Client.Execute(new RetrieveAllEntitiesRequest{
            EntityFilters=EntityFilters.Entity,RetrieveAsIfPublished=true})).EntityMetadata;
        var existing=all.SingleOrDefault(e=>e.LogicalName==Cancellation);
        if (existing==null) {
            var created=(CreateEntityResponse)Client.Execute(new CreateEntityRequest{
                SolutionUniqueName=Solution,
                Entity=new EntityMetadata{SchemaName=Cancellation,DisplayName=L("Leave Cancellation"),
                    DisplayCollectionName=L("Leave Cancellations"),OwnershipType=OwnershipTypes.UserOwned,IsActivity=false,
                    Description=L("Approved leave cancellation and reasoned HR correction of past Emergency Leave, with exact balance reversal.")},
                PrimaryAttribute=Text("cr2c9_name","Cancellation Name",120,true),HasActivities=false,HasNotes=false});
            Console.WriteLine("Created Leave Cancellation table "+created.EntityId);
        } else if (existing.OwnershipType!=OwnershipTypes.UserOwned || existing.IsActivity==true)
            throw new Exception("Existing cancellation table contract differs.");
        if (!Meta(Cancellation).Attributes.Any(a=>a.LogicalName=="cr2c9_leaverequest")) {
            Client.Execute(new CreateOneToManyRequest{SolutionUniqueName=Solution,
                Lookup=new LookupAttributeMetadata{SchemaName="cr2c9_leaverequest",DisplayName=L("Leave Request"),
                    RequiredLevel=new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.ApplicationRequired)},
                OneToManyRelationship=new OneToManyRelationshipMetadata{SchemaName="cr2c9_leavecancellation_LeaveRequest",
                    ReferencedEntity=Request,ReferencingEntity=Cancellation,
                    CascadeConfiguration=new CascadeConfiguration{Assign=CascadeType.NoCascade,Delete=CascadeType.Restrict,
                        Merge=CascadeType.NoCascade,Reparent=CascadeType.NoCascade,Share=CascadeType.NoCascade,Unshare=CascadeType.NoCascade}}});
            Console.WriteLine("Created restrictive cancellation-request relationship.");
        }
        Attribute(Cancellation,new MemoAttributeMetadata{SchemaName="cr2c9_reason",DisplayName=L("Cancellation Reason"),MaxLength=2000,
            RequiredLevel=new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.ApplicationRequired)});
        Attribute(Cancellation,new PicklistAttributeMetadata{SchemaName="cr2c9_status",DisplayName=L("Cancellation Status"),
            OptionSet=new OptionSetMetadata{IsGlobal=false,OptionSetType=OptionSetType.Picklist,
                Options={new OptionMetadata(L("Pending Superior"),0),new OptionMetadata(L("Approved"),1),new OptionMetadata(L("Rejected"),3)}},
            RequiredLevel=new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.ApplicationRequired)});
        Attribute(Cancellation,new MemoAttributeMetadata{SchemaName="cr2c9_decisioncomments",DisplayName=L("Decision Comments"),MaxLength=2000,
            RequiredLevel=new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.None)});
        Attribute(Cancellation,Text("cr2c9_decisionbyentraid","Decision Maker Entra ID",36));
        Attribute(Cancellation,Text("cr2c9_decisionbyname","Decision Maker Name",200));
        Attribute(Cancellation,new DateTimeAttributeMetadata{SchemaName="cr2c9_decisionon",DisplayName=L("Decision On"),
            Format=DateTimeFormat.DateAndTime,DateTimeBehavior=DateTimeBehavior.UserLocal,
            RequiredLevel=new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.None)});
        Attribute(Cancellation,Text("cr2c9_expectedversion","Review Expected Version",30));
        Attribute(Cancellation,Text("cr2c9_locktoken","Server Cancellation Lock",36));
        Client.Execute(new PublishXmlRequest{ParameterXml="<importexportxml><entities><entity>"+Cancellation+"</entity></entities></importexportxml>"});
        var meta=Meta(Cancellation);
        if (meta.IsOptimisticConcurrencyEnabled!=true) throw new Exception("Cancellation table must support row-version concurrency.");
        Console.WriteLine("Published cancellation schema. No records or approvals changed.");
    }
    static Guid PluginType(Guid assemblyId,string typename)
    {
        var matches=Find("plugintype","typename",typename,"pluginassemblyid").Where(x=>x.GetAttributeValue<EntityReference>("pluginassemblyid")?.Id==assemblyId).ToArray();
        if(matches.Length>1)throw new Exception("Duplicate plug-in type "+typename);
        return matches.Length==1?matches[0].Id:Client.Create(new Entity("plugintype"){
            ["pluginassemblyid"]=new EntityReference("pluginassembly",assemblyId),["typename"]=typename,
            ["name"]=typename,["friendlyname"]=typename});
    }
    static Guid RegisterCancellationStep(Guid type,string name,string message,int stage,int rank,string? filtering=null)
    {
        var messageId=Find("sdkmessage","name",message).Single().Id;
        var query=new QueryExpression("sdkmessagefilter"){ColumnSet=new ColumnSet(false)};
        query.Criteria.AddCondition("sdkmessageid",ConditionOperator.Equal,messageId);
        query.Criteria.AddCondition("primaryobjecttypecode",ConditionOperator.Equal,Cancellation);
        var filter=Client.RetrieveMultiple(query).Entities.Single();
        var prior=Find("sdkmessageprocessingstep","name",name,"eventhandler");
        if(prior.Length>1)throw new Exception("Duplicate cancellation step "+name);
        var step=new Entity("sdkmessageprocessingstep"){
            ["name"]=name,["eventhandler"]=new EntityReference("plugintype",type),
            ["sdkmessageid"]=new EntityReference("sdkmessage",messageId),["sdkmessagefilterid"]=filter.ToEntityReference(),
            ["stage"]=new OptionSetValue(stage),["mode"]=new OptionSetValue(0),["rank"]=rank,
            ["supporteddeployment"]=new OptionSetValue(0),
            ["description"]="Synchronous approved-leave cancellation and HR Emergency correction safeguard with exact reversal."};
        if(filtering!=null)step["filteringattributes"]=filtering;
        Guid id;
        if(prior.Length==0)id=Client.Create(step);
        else{id=prior[0].Id;step.Id=id;Client.Update(step);}
        Add(id,92);
        return id;
    }
    static void CancellationDeploy()
    {
        if(Meta(Cancellation).IsOptimisticConcurrencyEnabled!=true)throw new Exception("Cancellation schema and concurrency must be ready.");
        var mainPath=Root+"/server/Leave.Plugins/bin/Release/net462/Leave.Plugins.dll";
        var balancePath=Root+"/server/Leave.BalanceGuards/bin/Release/net462/Leave.BalanceGuards.dll";
        foreach(var pair in new[]{("Leave.Plugins",mainPath),("Leave.BalanceGuards",balancePath)}){
            var assembly=Find("pluginassembly","name",pair.Item1,"publickeytoken").Single();
            var local=AssemblyName.GetAssemblyName(pair.Item2);
            if(assembly.GetAttributeValue<string>("publickeytoken")!=Convert.ToHexString(local.GetPublicKeyToken()!).ToLowerInvariant())
                throw new Exception("Signed assembly identity differs: "+pair.Item1);
        }
        var balanceGuard=Find("plugintype","typename","Leave.BalanceGuards.BalanceStagingGuard").Single();
        var balanceSteps=Find("sdkmessageprocessingstep","eventhandler",balanceGuard.Id,"statecode","stage","mode","filteringattributes");
        if(balanceSteps.Length!=14 || balanceSteps.Any(s=>s.GetAttributeValue<OptionSetValue>("statecode")?.Value!=0 ||
            s.GetAttributeValue<OptionSetValue>("stage")?.Value!=20 || s.GetAttributeValue<OptionSetValue>("mode")?.Value!=0 ||
            !string.IsNullOrEmpty(s.GetAttributeValue<string>("filteringattributes"))))
            throw new Exception("All existing balance-write guards must remain active.");
        var main=Find("pluginassembly","name","Leave.Plugins").Single();
        var balance=Find("pluginassembly","name","Leave.BalanceGuards").Single();
        Client.Update(new Entity("pluginassembly",main.Id){["content"]=Convert.ToBase64String(File.ReadAllBytes(mainPath))});
        Client.Update(new Entity("pluginassembly",balance.Id){["content"]=Convert.ToBase64String(File.ReadAllBytes(balancePath))});
        var guard=PluginType(balance.Id,"Leave.BalanceGuards.FutureCancellationGuard");
        var accounting=PluginType(balance.Id,"Leave.BalanceGuards.FutureCancellationAccounting");
        foreach(var message in new[]{"Create","Update","Delete"})
            RegisterCancellationStep(guard,"Leave cancellation safeguard: "+message,message,20,10);
        var accountingStep=RegisterCancellationStep(accounting,"Leave future cancellation accounting","Update",40,10,"cr2c9_status");
        var images=Find("sdkmessageprocessingstepimage","sdkmessageprocessingstepid",accountingStep,"name");
        var image=new Entity("sdkmessageprocessingstepimage"){
            ["sdkmessageprocessingstepid"]=new EntityReference("sdkmessageprocessingstep",accountingStep),
            ["name"]="BeforeCancellationDecision",["entityalias"]="BeforeCancellationDecision",
            ["imagetype"]=new OptionSetValue(0),["messagepropertyname"]="Target",["attributes"]="cr2c9_status"};
        if(images.Length==0)Client.Create(image);
        else{if(images.Length!=1)throw new Exception("Ambiguous cancellation preimage.");image.Id=images[0].Id;Client.Update(image);}
        foreach(var name in new[]{"Leave cancellation safeguard: Create","Leave cancellation safeguard: Update",
            "Leave cancellation safeguard: Delete","Leave future cancellation accounting"}){
            var step=Find("sdkmessageprocessingstep","name",name,"statecode","stage","mode").Single();
            if(step.GetAttributeValue<OptionSetValue>("statecode")?.Value!=0){
                Client.Update(new Entity("sdkmessageprocessingstep",step.Id){["statecode"]=new OptionSetValue(0),["statuscode"]=new OptionSetValue(1)});
            }
        }
        Add(main.Id,91);Add(balance.Id,91);
        Console.WriteLine("Deployed cancellation guard and synchronous accounting. Live behavior must be verified before use.");
    }
    static Guid CreateFictionalCancellation(Guid approvedRequest,string reason)
    {
        RequireFixtureNotificationExclusion();
        var request=Client.Retrieve(Request,approvedRequest,new ColumnSet("cr2c9_status","cr2c9_employeename"));
        var fixtureEmployee=request.GetAttributeValue<EntityReference>("cr2c9_employeename")?.Id;
        if(request.GetAttributeValue<OptionSetValue>("cr2c9_status")?.Value!=1 ||
            (fixtureEmployee!=AccountingEmployee && fixtureEmployee!=FixtureEmployee))
            throw new Exception("Only a fictional test employee's approved request may be cancelled in this test.");
        var ownId=Guid.Parse("6938b1f2-22c2-f111-aaad-000d3a826b69");
        var who=((WhoAmIResponse)Client.Execute(new WhoAmIRequest())).UserId;
        var identity=Client.Retrieve("systemuser",who,new ColumnSet("azureactivedirectoryobjectid"))
            .GetAttributeValue<Guid>("azureactivedirectoryobjectid").ToString("D");
        var own=Client.Retrieve(Employee,ownId,new ColumnSet("cr2c9_entrauserid"));
        var fixture=Client.Retrieve(Employee,fixtureEmployee.Value,new ColumnSet("cr2c9_entrauserid"));
        if(own.GetAttributeValue<string>("cr2c9_entrauserid")!=identity ||
            !Guid.TryParse(fixture.GetAttributeValue<string>("cr2c9_entrauserid"),out var fictionalIdentity) ||
            fictionalIdentity.ToString("D")==identity)
            throw new Exception("Identity swap preflight failed; no cancellation submitted.");
        Client.Execute(new UpdateRequest{Target=new Entity(Employee,ownId){RowVersion=own.RowVersion,
            ["cr2c9_entrauserid"]=Guid.NewGuid().ToString("D")},ConcurrencyBehavior=ConcurrencyBehavior.IfRowVersionMatches});
        try {
            Client.Execute(new UpdateRequest{Target=new Entity(Employee,fixtureEmployee.Value){RowVersion=fixture.RowVersion,
                ["cr2c9_entrauserid"]=identity},ConcurrencyBehavior=ConcurrencyBehavior.IfRowVersionMatches});
            var id=Client.Create(new Entity(Cancellation){["cr2c9_leaverequest"]=new EntityReference(Request,approvedRequest),
                ["cr2c9_status"]=new OptionSetValue(0),["cr2c9_reason"]=reason});
            return id;
        } finally {
            var restore=new OrganizationRequestCollection {
                new UpdateRequest{Target=new Entity(Employee,fixtureEmployee.Value){["cr2c9_entrauserid"]=fixture["cr2c9_entrauserid"]}},
                new UpdateRequest{Target=new Entity(Employee,ownId){["cr2c9_entrauserid"]=identity}}
            };
            Client.Execute(new ExecuteTransactionRequest{Requests=restore,ReturnResponses=false});
        }
    }
    static void CancellationTest()
    {
        var approved=AccountingRequests()["mixed"];
        var pending=Find(Cancellation,"cr2c9_leaverequest",approved,"cr2c9_status");
        if(pending.Length>1 || pending.Any(x=>x.GetAttributeValue<OptionSetValue>("cr2c9_status")?.Value!=0))
            throw new Exception("The mixed fixture already has decided or duplicate cancellation history.");
        var before=AccountingBalance();
        if(before!=0 || Client.Retrieve(Request,approved,new ColumnSet("cr2c9_status"))
            .GetAttributeValue<OptionSetValue>("cr2c9_status")?.Value!=1)
            throw new Exception("Expected fictional approved request with zero remaining balance.");
        var id=pending.Length==1?pending[0].Id:CreateFictionalCancellation(approved,"Fictional future cancellation and exact reversal test.");
        Console.WriteLine("Using pending future cancellation "+id);
        if(AccountingBalance()!=before || Find(BalanceEntryTable,"cr2c9_leaverequest",approved,"cr2c9_entrykind").Length!=1 ||
            Client.Retrieve(Request,approved,new ColumnSet("cr2c9_status")).GetAttributeValue<OptionSetValue>("cr2c9_status")?.Value!=1)
            throw new Exception("Pending cancellation changed the original approval or balance.");
        var row=Client.Retrieve(Cancellation,id,new ColumnSet("cr2c9_status"));
        Client.Update(new Entity(Cancellation,id){["cr2c9_status"]=new OptionSetValue(1),
            ["cr2c9_expectedversion"]=row.RowVersion,["cr2c9_decisioncomments"]="Fictional exact-reversal test."});
        row=Client.Retrieve(Cancellation,id,new ColumnSet("cr2c9_status","cr2c9_decisionbyentraid","cr2c9_decisionon"));
        var original=Client.Retrieve(Request,approved,new ColumnSet("cr2c9_status"));
        var ledger=Find(BalanceEntryTable,"cr2c9_leaverequest",approved,"cr2c9_entrykind","cr2c9_hours","cr2c9_reverses","cr2c9_operationkey");
        var debit=ledger.SingleOrDefault(e=>e.GetAttributeValue<OptionSetValue>("cr2c9_entrykind")?.Value==290040001);
        var reversal=ledger.SingleOrDefault(e=>e.GetAttributeValue<OptionSetValue>("cr2c9_entrykind")?.Value==290040002);
        if(row.GetAttributeValue<OptionSetValue>("cr2c9_status")?.Value!=1 || !row.Contains("cr2c9_decisionon") ||
            original.GetAttributeValue<OptionSetValue>("cr2c9_status")?.Value!=5 || ledger.Length!=2 ||
            debit==null || reversal==null || debit.GetAttributeValue<int>("cr2c9_hours")!=-12 ||
            reversal.GetAttributeValue<int>("cr2c9_hours")!=12 ||
            reversal.GetAttributeValue<EntityReference>("cr2c9_reverses")?.Id!=debit.Id ||
            AccountingBalance()!=12) throw new Exception("Exact cancellation reversal did not commit atomically.");
        Reject("cancellation retry",()=>Client.Update(new Entity(Cancellation,id){["cr2c9_status"]=new OptionSetValue(1),
            ["cr2c9_expectedversion"]=row.RowVersion}),"Only a pending cancellation");
        if(AccountingBalance()!=12 || Find(BalanceEntryTable,"cr2c9_leaverequest",approved,"cr2c9_entrykind").Length!=2)
            throw new Exception("Cancellation retry wrote a duplicate reversal.");
        Console.WriteLine("PASS future cancellation: pending preserves approval/0h, Superior decision cancels original and restores exactly 12h once.");
    }
}
