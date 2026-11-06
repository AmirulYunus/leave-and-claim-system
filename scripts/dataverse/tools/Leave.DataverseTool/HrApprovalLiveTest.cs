using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using System.ServiceModel;

internal static partial class Development {
    static Guid HrApprovalLiveTest() {
        RequireFixtureNotificationExclusion();
        var reviewer=Find("systemuser","internalemailaddress","Jennie.Cheong@Asteriskdigital.com.my",
            "fullname","isdisabled","azureactivedirectoryobjectid","businessunitid").Single();
        var reviewerEntra=reviewer.GetAttributeValue<Guid>("azureactivedirectoryobjectid");
        if(reviewer.GetAttributeValue<bool>("isdisabled") || reviewerEntra==Guid.Empty ||
           !UserRoleNames(reviewer.Id).Contains(HrRoleName))
            throw new Exception("Verified active Jennie HR identity and role are required.");
        var bu=reviewer.GetAttributeValue<EntityReference>("businessunitid")?.Id ?? Guid.Empty;
        var role=Find("role","name",HrRoleName,"businessunitid")
            .Single(x=>x.GetAttributeValue<EntityReference>("businessunitid")?.Id==bu);
        var privileges=((RetrieveRolePrivilegesRoleResponse)Client.Execute(
            new RetrieveRolePrivilegesRoleRequest{RoleId=role.Id})).RolePrivileges;
        var appMeta=((RetrieveEntityResponse)Client.Execute(new RetrieveEntityRequest {
            LogicalName="appmodule",EntityFilters=EntityFilters.Entity|EntityFilters.Privileges
        })).EntityMetadata;
        var read=appMeta.Privileges.Single(x=>x.PrivilegeType==PrivilegeType.Read).PrivilegeId;
        if(!privileges.Any(x=>x.PrivilegeId==read && x.Depth==PrivilegeDepth.Global))
            throw new Exception("Leave HR role is missing organization-wide App Module Read.");
        var linked=new QueryExpression("role") {ColumnSet=new ColumnSet(false)};
        linked.Criteria.AddCondition("roleid",ConditionOperator.Equal,role.Id);
        var appLink=linked.AddLink("appmoduleroles","roleid","roleid");
        appLink.LinkCriteria.AddCondition("appmoduleid",ConditionOperator.Equal,HrApp);
        if(Client.RetrieveMultiple(linked).Entities.Count!=1)
            throw new Exception("Leave HR role is not associated with the published HR app.");
        var priorCaller=Client.CallerId;
        try {
            Client.CallerId=reviewer.Id;
            var app=Client.Retrieve("appmodule",HrApp,new ColumnSet("name","statecode"));
            if(app.GetAttributeValue<string>("name")!="HR leave operations" ||
               app.GetAttributeValue<OptionSetValue>("statecode")?.Value!=0)
                throw new Exception("Jennie's HR app access preflight failed.");
        } finally {Client.CallerId=priorCaller;}

        var period=Find(BalancePeriodTable,"cr2c9_employee",FixtureEmployee,"cr2c9_periodstart","cr2c9_leavetype")
            .Single(x=>x.GetAttributeValue<DateTime>("cr2c9_periodstart").Year==DateTime.UtcNow.AddHours(8).Year &&
                x.GetAttributeValue<EntityReference>("cr2c9_leavetype")?.Id==Guid.Parse("959c93de-1197-4de4-a660-2e2c3266f0fe"));
        int Balance()=>Find(BalanceEntryTable,"cr2c9_balanceperiod",period.Id,"cr2c9_hours")
            .Sum(x=>x.GetAttributeValue<int>("cr2c9_hours"));
        var before=Balance();
        if(before<8)throw new Exception("Fictional fixture does not have eight earned hours; no test request created.");
        var requestId=SuperiorFormFixture();
        var pending=Client.Retrieve(Request,requestId,new ColumnSet("cr2c9_status"));
        if(pending.GetAttributeValue<OptionSetValue>("cr2c9_status")?.Value!=0 ||
           Find(BalanceEntryTable,"cr2c9_leaverequest",requestId).Length!=0)
            throw new Exception("Fictional request pre-approval state is wrong.");
        var decision=new Entity(Request,requestId) {
            ["cr2c9_status"]=new OptionSetValue(1),
            ["cr2c9_decisionexpectedversion"]=pending.RowVersion,
            ["cr2c9_decisioncomments"]="Fictional HR approval integration test; notification excluded."
        };
        try {
            Client.CallerId=reviewer.Id;
            Client.Retrieve(Request,requestId,new ColumnSet("cr2c9_status"));
            Client.Update(decision);
        } finally {Client.CallerId=priorCaller;}
        var approved=Client.Retrieve(Request,requestId,new ColumnSet("cr2c9_status","cr2c9_decisionbyentraid","cr2c9_decisionon"));
        var entries=Find(BalanceEntryTable,"cr2c9_leaverequest",requestId,"cr2c9_hours","cr2c9_balanceperiod");
        var history=Find(History,"cr2c9_leaverequest",requestId,"cr2c9_actorentraid","cr2c9_outcome");
        if(approved.GetAttributeValue<OptionSetValue>("cr2c9_status")?.Value!=1 ||
           approved.GetAttributeValue<string>("cr2c9_decisionbyentraid")!=reviewerEntra.ToString("D") ||
           !approved.Contains("cr2c9_decisionon") ||
           entries.Length!=1 || entries[0].GetAttributeValue<int>("cr2c9_hours")!=-8 ||
           entries[0].GetAttributeValue<EntityReference>("cr2c9_balanceperiod")?.Id!=period.Id ||
           history.Length!=1 || history[0].GetAttributeValue<string>("cr2c9_actorentraid")!=reviewerEntra.ToString("D") ||
           history[0].GetAttributeValue<OptionSetValue>("cr2c9_outcome")?.Value!=1 || Balance()!=before-8)
            throw new Exception("Fictional HR approval did not produce exactly one verified decision and eight-hour debit.");
        Console.WriteLine($"PASS Jennie HR role can read the HR app and approve fictional request {requestId}; one decision and one -8h ledger entry; balance {before}→{Balance()}h. App sign-in with her own session still requires her check.");
        return requestId;
    }

    static void HrCancellationLiveTest() {
        RequireFixtureNotificationExclusion();
        var reviewer=Find("systemuser","internalemailaddress","Jennie.Cheong@Asteriskdigital.com.my",
            "isdisabled","azureactivedirectoryobjectid").Single();
        var reviewerEntra=reviewer.GetAttributeValue<Guid>("azureactivedirectoryobjectid");
        if(reviewer.GetAttributeValue<bool>("isdisabled") || reviewerEntra==Guid.Empty ||
           !UserRoleNames(reviewer.Id).Contains(HrRoleName))
            throw new Exception("Verified active Jennie HR identity and role are required.");
        var requestId=HrApprovalLiveTest();
        var period=Find(BalancePeriodTable,"cr2c9_employee",FixtureEmployee,"cr2c9_leavetype")
            .Single(x=>x.GetAttributeValue<EntityReference>("cr2c9_leavetype")?.Id==Guid.Parse("959c93de-1197-4de4-a660-2e2c3266f0fe"));
        int Balance()=>Find(BalanceEntryTable,"cr2c9_balanceperiod",period.Id,"cr2c9_hours")
            .Sum(x=>x.GetAttributeValue<int>("cr2c9_hours"));
        var before=Balance();
        var cancellationId=CreateFictionalCancellation(requestId,
            "Fictional HR cancellation integration test; notifications excluded.");
        var pending=Client.Retrieve(Cancellation,cancellationId,new ColumnSet("cr2c9_status"));
        if(pending.GetAttributeValue<OptionSetValue>("cr2c9_status")?.Value!=0 ||
           Client.Retrieve(Request,requestId,new ColumnSet("cr2c9_status"))
               .GetAttributeValue<OptionSetValue>("cr2c9_status")?.Value!=1 || Balance()!=before)
            throw new Exception("A pending fictional cancellation changed the approval or balance.");
        var priorCaller=Client.CallerId;
        try {
            Client.CallerId=reviewer.Id;
            Client.Update(new Entity(Cancellation,cancellationId) {
                ["cr2c9_status"]=new OptionSetValue(1),
                ["cr2c9_expectedversion"]=pending.RowVersion,
                ["cr2c9_decisioncomments"]="Fictional HR cancellation integration test."
            });
        } finally {Client.CallerId=priorCaller;}
        var decided=Client.Retrieve(Cancellation,cancellationId,
            new ColumnSet("cr2c9_status","cr2c9_decisionbyentraid","cr2c9_decisionon"));
        var original=Client.Retrieve(Request,requestId,new ColumnSet("cr2c9_status"));
        var entries=Find(BalanceEntryTable,"cr2c9_leaverequest",requestId,
            "cr2c9_entrykind","cr2c9_hours","cr2c9_reverses");
        var debit=entries.SingleOrDefault(x=>x.GetAttributeValue<OptionSetValue>("cr2c9_entrykind")?.Value==290040001);
        var reversal=entries.SingleOrDefault(x=>x.GetAttributeValue<OptionSetValue>("cr2c9_entrykind")?.Value==290040002);
        if(decided.GetAttributeValue<OptionSetValue>("cr2c9_status")?.Value!=1 ||
           decided.GetAttributeValue<string>("cr2c9_decisionbyentraid")!=reviewerEntra.ToString("D") ||
           !decided.Contains("cr2c9_decisionon") ||
           original.GetAttributeValue<OptionSetValue>("cr2c9_status")?.Value!=5 ||
           entries.Length!=2 || debit?.GetAttributeValue<int>("cr2c9_hours")!=-8 ||
           reversal?.GetAttributeValue<int>("cr2c9_hours")!=8 ||
           reversal.GetAttributeValue<EntityReference>("cr2c9_reverses")?.Id!=debit.Id ||
           Balance()!=before+8)
            throw new Exception("Fictional HR cancellation did not restore the exact eight-hour debit once.");
        var repeatRejected=false;
        try {
            Client.CallerId=reviewer.Id;
            Client.Update(new Entity(Cancellation,cancellationId) {
                ["cr2c9_status"]=new OptionSetValue(1),
                ["cr2c9_expectedversion"]=decided.RowVersion
            });
        } catch(FaultException<OrganizationServiceFault>) { repeatRejected=true; }
        finally {Client.CallerId=priorCaller;}
        if(!repeatRejected || Balance()!=before+8 ||
           Find(BalanceEntryTable,"cr2c9_leaverequest",requestId).Length!=2)
            throw new Exception("Repeat HR cancellation did not preserve the single reversal.");
        Console.WriteLine($"PASS Jennie HR cancellation {cancellationId}: pending preserved approval/balance, HR decision cancelled fictional request {requestId}, restored +8h exactly once, repeat rejected. Own-session UI still requires her sign-in check.");
    }

    static void HrApprovalPostVerify() {
        var id=Guid.Parse("7fbe43c8-6ea1-4aa6-97fa-d55746757eb6");
        var row=Client.Retrieve(Request,id,new ColumnSet("cr2c9_employeename","cr2c9_status"));
        if(row.GetAttributeValue<EntityReference>("cr2c9_employeename")?.Id!=FixtureEmployee ||
           row.GetAttributeValue<OptionSetValue>("cr2c9_status")?.Value!=1)
            throw new Exception("Only the approved fictional HR test request may be checked.");
        var period=Find(BalancePeriodTable,"cr2c9_employee",FixtureEmployee,"cr2c9_leavetype")
            .Single(x=>x.GetAttributeValue<EntityReference>("cr2c9_leavetype")?.Id==Guid.Parse("959c93de-1197-4de4-a660-2e2c3266f0fe"));
        int Balance()=>Find(BalanceEntryTable,"cr2c9_balanceperiod",period.Id,"cr2c9_hours")
            .Sum(x=>x.GetAttributeValue<int>("cr2c9_hours"));
        var before=Balance();
        var reviewer=Find("systemuser","internalemailaddress","Jennie.Cheong@Asteriskdigital.com.my",
            "azureactivedirectoryobjectid","isdisabled").Single();
        if(reviewer.GetAttributeValue<bool>("isdisabled") ||
           reviewer.GetAttributeValue<Guid>("azureactivedirectoryobjectid")==Guid.Empty)
            throw new Exception("Jennie reviewer identity is unavailable.");
        var previous=Client.CallerId;
        var rejected=false;
        try {
            Client.CallerId=reviewer.Id;
            try {
                Client.Update(new Entity(Request,id) {
                    ["cr2c9_status"]=new OptionSetValue(1),
                    ["cr2c9_decisionexpectedversion"]=row.RowVersion,
                    ["cr2c9_decisioncomments"]="Fictional repeat approval must be rejected."
                });
            } catch(FaultException<OrganizationServiceFault> ex) when(
                ex.Detail.Message.Contains("Existing decisions cannot be repeated",StringComparison.OrdinalIgnoreCase)) {
                rejected=true;
            }
        } finally {Client.CallerId=previous;}
        if(!rejected || Balance()!=before ||
           Find(BalanceEntryTable,"cr2c9_leaverequest",id).Length!=1 ||
           Find(History,"cr2c9_leaverequest",id).Length!=1)
            throw new Exception("Repeat approval did not safely preserve the one debit and one decision.");
        Console.WriteLine($"PASS repeat approval rejected; fictional request {id} still has one decision, one debit, and balance {before}h.");
    }

    static void HrSuperiorAppAccessAudit() {
        var previous=Client.CallerId;
        foreach(var email in new[]{"Jennie.Cheong@Asteriskdigital.com.my","KhorLord.Yong@Asteriskdigital.com.my"}) {
            var user=Find("systemuser","internalemailaddress",email,"isdisabled","azureactivedirectoryobjectid","businessunitid").Single();
            if(user.GetAttributeValue<bool>("isdisabled") || user.GetAttributeValue<Guid>("azureactivedirectoryobjectid")==Guid.Empty)
                throw new Exception("Reviewer is not an active verified Entra user: "+email);
            var bu=user.GetAttributeValue<EntityReference>("businessunitid")?.Id ?? Guid.Empty;
            foreach(var (roleName,appId) in new[]{(HrRoleName,HrApp),(SuperiorRoleName,SuperiorApp)}) {
                if(!UserRoleNames(user.Id).Contains(roleName))throw new Exception(email+" lacks "+roleName);
                var role=Find("role","name",roleName,"businessunitid")
                    .Single(x=>x.GetAttributeValue<EntityReference>("businessunitid")?.Id==bu);
                var privileges=((RetrieveRolePrivilegesRoleResponse)Client.Execute(
                    new RetrieveRolePrivilegesRoleRequest{RoleId=role.Id})).RolePrivileges;
                var metadata=((RetrieveEntityResponse)Client.Execute(new RetrieveEntityRequest {
                    LogicalName="appmodule",EntityFilters=EntityFilters.Entity|EntityFilters.Privileges
                })).EntityMetadata;
                var read=metadata.Privileges.Single(x=>x.PrivilegeType==PrivilegeType.Read).PrivilegeId;
                if(!privileges.Any(x=>x.PrivilegeId==read && x.Depth==PrivilegeDepth.Global))
                    throw new Exception(roleName+" lacks organization-wide App Module Read.");
                var linked=new QueryExpression("role") {ColumnSet=new ColumnSet(false)};
                linked.Criteria.AddCondition("roleid",ConditionOperator.Equal,role.Id);
                var appLink=linked.AddLink("appmoduleroles","roleid","roleid");
                appLink.LinkCriteria.AddCondition("appmoduleid",ConditionOperator.Equal,appId);
                if(Client.RetrieveMultiple(linked).Entities.Count!=1)
                    throw new Exception(roleName+" has no app association.");
                try {
                    Client.CallerId=user.Id;
                    var app=Client.Retrieve("appmodule",appId,new ColumnSet("name","statecode"));
                    if(app.GetAttributeValue<OptionSetValue>("statecode")?.Value!=0)
                        throw new Exception("App is not active: "+appId);
                } finally {Client.CallerId=previous;}
                Console.WriteLine("PASS "+email+" can read active "+roleName+" app with assigned role, App Module Read and app association.");
            }
        }
    }
}
