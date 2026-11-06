using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using System.ServiceModel;

internal static partial class Development {
    // Limited development test role. Do not migrate this role as a production
    // permission design until all ordinary-user and API checks pass.
    static void EmployeeTestRoleDeploy() {
        const string email="Darwisyah.Fakhira@Asteriskdigital.com.my", name="Leave Employee (development test)";
        var employee=Find(Employee,"cr2c9_email",email,"cr2c9_entrauserid","cr2c9_isactive").Single();
        var user=Find("systemuser","internalemailaddress",email,"azureactivedirectoryobjectid","businessunitid","isdisabled").Single();
        var entra=Guid.Parse(employee.GetAttributeValue<string>("cr2c9_entrauserid"));
        if(!employee.GetAttributeValue<bool>("cr2c9_isactive") || user.GetAttributeValue<bool>("isdisabled") ||
            user.GetAttributeValue<Guid>("azureactivedirectoryobjectid")!=entra ||
            !UserRoleNames(user.Id).SequenceEqual(new[]{"Basic User"}))throw new Exception("Ordinary employee identity or role preflight failed.");
        var bu=user.GetAttributeValue<EntityReference>("businessunitid")?.Id ?? Guid.Empty;
        if(bu==Guid.Empty)throw new Exception("Candidate business unit missing.");
        var roles=new QueryExpression("role") {ColumnSet=new ColumnSet("name","businessunitid")};
        roles.Criteria.AddCondition("name",ConditionOperator.Equal,name);
        roles.Criteria.AddCondition("businessunitid",ConditionOperator.Equal,bu);
        var existing=Client.RetrieveMultiple(roles).Entities;
        if(existing.Count>1)throw new Exception("Duplicate test roles.");
        var roleId=existing.Count==1?existing[0].Id:Client.Create(new Entity("role") {
            ["name"]=name,
            ["businessunitid"]=new EntityReference("businessunit",bu)
        });
        var grants=new List<RolePrivilege>();
        void Grant(string table, PrivilegeDepth depth, params PrivilegeType[] types) {
            var meta=((RetrieveEntityResponse)Client.Execute(new RetrieveEntityRequest {
                LogicalName=table, EntityFilters=EntityFilters.Entity|EntityFilters.Privileges
            })).EntityMetadata;
            if(meta.OwnershipType==OwnershipTypes.OrganizationOwned && depth!=PrivilegeDepth.Global)
                throw new Exception("Unexpected ownership/depth for "+table);
            foreach(var type in types) {
                var p=meta.Privileges.Single(x=>x.PrivilegeType==type);
                grants.Add(new RolePrivilege((int)depth,p.PrivilegeId));
            }
        }
        Grant(Employee,PrivilegeDepth.Basic,PrivilegeType.Read,PrivilegeType.Append,PrivilegeType.AppendTo);
        Grant(Request,PrivilegeDepth.Basic,PrivilegeType.Create,PrivilegeType.Read,PrivilegeType.Write,PrivilegeType.Append,PrivilegeType.AppendTo);
        // The Canvas form stores its Attachments card as Dataverse Notes.
        // Keep the role definition aligned with the live attachment fix.
        Grant("annotation",PrivilegeDepth.Basic,PrivilegeType.Create,PrivilegeType.Read,PrivilegeType.Append,PrivilegeType.AppendTo);
        Grant("cr2c9_leaverequestdate",PrivilegeDepth.Basic,PrivilegeType.Create,PrivilegeType.Read,PrivilegeType.Write,PrivilegeType.Delete,PrivilegeType.Append,PrivilegeType.AppendTo);
        Grant("cr2c9_leavecancellation",PrivilegeDepth.Basic,PrivilegeType.Create,PrivilegeType.Read,PrivilegeType.Append,PrivilegeType.AppendTo);
        Grant("cr2c9_leavetype",PrivilegeDepth.Global,PrivilegeType.Read,PrivilegeType.AppendTo);
        Grant("cr2c9_leavebalanceperiod",PrivilegeDepth.Basic,PrivilegeType.Read);
        Grant("cr2c9_leavebalanceentry",PrivilegeDepth.Basic,PrivilegeType.Read);
        Grant("cr2c9_leavedecision",PrivilegeDepth.Basic,PrivilegeType.Read);
        var present=((RetrieveRolePrivilegesRoleResponse)Client.Execute(new RetrieveRolePrivilegesRoleRequest {RoleId=roleId})).RolePrivileges;
        var toAdd=grants.Where(g=>!present.Any(p=>p.PrivilegeId==g.PrivilegeId && p.Depth==g.Depth)).ToArray();
        if(toAdd.Length>0)Client.Execute(new AddPrivilegesRoleRequest {RoleId=roleId,Privileges=toAdd});
        var verified=((RetrieveRolePrivilegesRoleResponse)Client.Execute(new RetrieveRolePrivilegesRoleRequest {RoleId=roleId})).RolePrivileges;
        if(grants.Any(g=>!verified.Any(p=>p.PrivilegeId==g.PrivilegeId && p.Depth==g.Depth)))throw new Exception("Test role privilege verification failed; role was not assigned.");
        if(!UserRoleNames(user.Id).Contains(name))Client.Associate("systemuser",user.Id,new Relationship("systemuserroles_association"),
            new EntityReferenceCollection {new EntityReference("role",roleId)});
        var after=UserRoleNames(user.Id);
        if(after.Length!=2 || !after.Contains("Basic User") || !after.Contains(name))throw new Exception("Candidate role verification failed: "+string.Join(", ",after));
        Console.WriteLine("Assigned scoped development Employee test role to Darwisyah alongside Basic User. App share and row ownership remain unchanged; no HR, Superior or Maker privilege added.");
    }
    static void EmployeeTestRowAccess() {
        const string email="Darwisyah.Fakhira@Asteriskdigital.com.my";
        var employee=Find(Employee,"cr2c9_email",email,"cr2c9_entrauserid","ownerid","cr2c9_isactive").Single();
        var user=Find("systemuser","internalemailaddress",email,"azureactivedirectoryobjectid","isdisabled").Single();
        if(!employee.GetAttributeValue<bool>("cr2c9_isactive") || user.GetAttributeValue<bool>("isdisabled") ||
           Guid.Parse(employee.GetAttributeValue<string>("cr2c9_entrauserid"))!=user.GetAttributeValue<Guid>("azureactivedirectoryobjectid") ||
           !UserRoleNames(user.Id).SequenceEqual(new[]{"Basic User","Leave Employee (development test)"}))
            throw new Exception("Candidate identity or scoped roles changed.");
        var who=((WhoAmIResponse)Client.Execute(new WhoAmIRequest())).UserId;
        var owner=employee.GetAttributeValue<EntityReference>("ownerid");
        if(owner?.Id!=who && owner?.Id!=user.Id)throw new Exception("Unexpected Employee owner; not changing it.");
        if(owner.Id!=user.Id) {
            Client.Update(new Entity(Employee,employee.Id){["ownerid"]=user.ToEntityReference()});
            if(Client.Retrieve(Employee,employee.Id,new ColumnSet("ownerid")).GetAttributeValue<EntityReference>("ownerid")?.Id!=user.Id)
                throw new Exception("Candidate row assignment did not verify.");
        }
        var foreignBalance=Find(BalancePeriodTable,"cr2c9_employee",FixtureEmployee).FirstOrDefault()
            ?? throw new Exception("Foreign balance fixture missing; access test incomplete.");
        var foreignEntry=Find(BalanceEntryTable,"cr2c9_balanceperiod",foreignBalance.Id).FirstOrDefault()
            ?? throw new Exception("Foreign balance entry fixture missing; access test incomplete.");
        var foreignDateRequestId=Guid.Parse("f0ffcfcc-aed7-4407-9ba6-86a17c44708d");
        var foreignDateRequest=Client.Retrieve(Request,foreignDateRequestId,new ColumnSet("cr2c9_employeename"));
        if(foreignDateRequest.GetAttributeValue<EntityReference>("cr2c9_employeename")?.Id==employee.Id)
            throw new Exception("Date fixture unexpectedly belongs to candidate.");
        var foreignDate=Find("cr2c9_leaverequestdate","cr2c9_leaverequest",foreignDateRequestId).FirstOrDefault()
            ?? throw new Exception("Foreign request date fixture missing; access test incomplete.");
        var foreignDecision=Find(History,"cr2c9_leaverequest",FixtureRequest).FirstOrDefault()
            ?? throw new Exception("Foreign decision fixture missing; access test incomplete.");
        var foreignNote=Client.Retrieve("annotation",Guid.Parse("c407e48e-24c2-f111-aaad-000d3a826b69"),new ColumnSet("objectid"));
        if(foreignNote.GetAttributeValue<EntityReference>("objectid")?.Id==employee.Id)
            throw new Exception("Attachment fixture unexpectedly belongs to candidate.");
        var annualTypeId=Guid.Parse("959c93de-1197-4de4-a660-2e2c3266f0fe");
        Client.Retrieve("cr2c9_leavetype",annualTypeId,new ColumnSet(false));
        var ownRights=((RetrievePrincipalAccessResponse)Client.Execute(new RetrievePrincipalAccessRequest {
            Principal=user.ToEntityReference(),Target=employee.ToEntityReference()
        })).AccessRights;
        if((ownRights & (AccessRights.WriteAccess|AccessRights.DeleteAccess|AccessRights.AssignAccess|AccessRights.ShareAccess))!=0)
            throw new Exception("Employee has maintenance rights over own profile: "+ownRights);
        var previous=Client.CallerId;
        try {
            Client.CallerId=user.Id;
            Client.Retrieve(Employee,employee.Id,new ColumnSet("cr2c9_employeename"));
            Client.Retrieve("cr2c9_leavetype",annualTypeId,new ColumnSet(false));
            void ExpectDenied(string table,Guid id) {
                var denied=false;
                try { Client.Retrieve(table,id,new ColumnSet(false)); }
                catch(FaultException<OrganizationServiceFault> ex) when(ex.Detail.Message.Contains("access",StringComparison.OrdinalIgnoreCase) ||
                                                                        ex.Detail.Message.Contains("privilege",StringComparison.OrdinalIgnoreCase)) { denied=true; }
                if(!denied)throw new Exception("Employee role could read foreign "+table+" row.");
            }
            ExpectDenied(Employee,FixtureEmployee);
            ExpectDenied(Request,FixtureRequest);
            ExpectDenied("cr2c9_leaverequestdate",foreignDate.Id);
            ExpectDenied(BalancePeriodTable,foreignBalance.Id);
            ExpectDenied(BalanceEntryTable,foreignEntry.Id);
            ExpectDenied(History,foreignDecision.Id);
            ExpectDenied("annotation",foreignNote.Id);
            Console.WriteLine("PASS scoped Employee can read own Employee row and leave type; denied foreign Employee, request, date, balance period, ledger entry, decision and Note rows. No own-profile Write/Delete/Assign/Share rights. Candidate Employee row belongs to Darwisyah. Published-app own-sign-in and positive own-request paths still need checks.");
        } finally { Client.CallerId=previous; }
    }

    static void EmployeeTestSubmission() {
        const string email="Darwisyah.Fakhira@Asteriskdigital.com.my";
        const string reason="Fictional ordinary-employee submission test; pending and not for approval.";
        var employee=Find(Employee,"cr2c9_email",email,"cr2c9_entrauserid","cr2c9_isactive","ownerid").Single();
        var user=Find("systemuser","internalemailaddress",email,"azureactivedirectoryobjectid","isdisabled").Single();
        if(!employee.GetAttributeValue<bool>("cr2c9_isactive") || user.GetAttributeValue<bool>("isdisabled") ||
           employee.GetAttributeValue<EntityReference>("ownerid")?.Id!=user.Id ||
           Guid.Parse(employee.GetAttributeValue<string>("cr2c9_entrauserid"))!=user.GetAttributeValue<Guid>("azureactivedirectoryobjectid") ||
           !UserRoleNames(user.Id).SequenceEqual(new[]{"Basic User","Leave Employee (development test)"}))
            throw new Exception("Ordinary employee submission identity, owner or role preflight failed.");
        var annual=Find("cr2c9_leavetype",Meta("cr2c9_leavetype").PrimaryNameAttribute,"Annual Leave","statecode").Single();
        if(annual.GetAttributeValue<OptionSetValue>("statecode")?.Value!=0)throw new Exception("Annual Leave is inactive.");
        var previous=Client.CallerId;
        try {
            Client.CallerId=user.Id;
            var request=new Entity(Request) {
                ["cr2c9_employeename"]=employee.ToEntityReference(),
                ["cr2c9_leavetypename"]=annual.ToEntityReference(),
                ["cr2c9_status"]=new OptionSetValue(0),
                ["cr2c9_startdate"]=new DateTime(2026,11,23),
                ["cr2c9_enddate"]=new DateTime(2026,11,23),
                ["cr2c9_reason"]=reason
            };
            var id=Client.Create(request);
            var saved=Client.Retrieve(Request,id,new ColumnSet("cr2c9_employeename","cr2c9_status","ownerid"));
            if(saved.GetAttributeValue<EntityReference>("cr2c9_employeename")?.Id!=employee.Id ||
               saved.GetAttributeValue<OptionSetValue>("cr2c9_status")?.Value!=0 ||
               saved.GetAttributeValue<EntityReference>("ownerid")?.Id!=user.Id)
                throw new Exception("Ordinary employee request did not save with expected owner, profile and pending status: "+id);
            var dateId=Client.Create(new Entity("cr2c9_leaverequestdate") {
                ["cr2c9_entryname"]="Fictional ordinary-employee half day",
                ["cr2c9_leaverequest"]=new EntityReference(Request,id),
                ["cr2c9_leavedate"]=new DateTime(2026,11,23),
                ["cr2c9_duration"]=new OptionSetValue(290040000)
            });
            var date=Client.Retrieve("cr2c9_leaverequestdate",dateId,new ColumnSet("cr2c9_leaverequest","cr2c9_duration"));
            if(date.GetAttributeValue<EntityReference>("cr2c9_leaverequest")?.Id!=id ||
               date.GetAttributeValue<OptionSetValue>("cr2c9_duration")?.Value!=290040000)
                throw new Exception("Ordinary employee date entry verification failed: "+dateId);
            Console.WriteLine("PASS ordinary Employee created and read own fictional Pending Superior request "+id+
                " with one 4h date "+dateId+". No approval or balance deduction was attempted.");
        } finally { Client.CallerId=previous; }
    }

    static void EmployeeProfileWriteTest() {
        const string email="Darwisyah.Fakhira@Asteriskdigital.com.my";
        var employee=Find(Employee,"cr2c9_email",email,
            "cr2c9_entrauserid","cr2c9_superior","cr2c9_employeelevel","ownerid").Single();
        var user=Find("systemuser","internalemailaddress",email,
            "azureactivedirectoryobjectid","isdisabled").Single();
        if(user.GetAttributeValue<bool>("isdisabled") ||
           employee.GetAttributeValue<EntityReference>("ownerid")?.Id!=user.Id ||
           Guid.Parse(employee.GetAttributeValue<string>("cr2c9_entrauserid"))!=
               user.GetAttributeValue<Guid>("azureactivedirectoryobjectid") ||
           !UserRoleNames(user.Id).SequenceEqual(new[]{"Basic User","Leave Employee (development test)"}))
            throw new Exception("Ordinary employee identity, ownership or role preflight failed.");
        var fields=new[]{"cr2c9_employeelevel","cr2c9_entrauserid","cr2c9_superior","cr2c9_email"};
        var before=Client.Retrieve(Employee,employee.Id,new ColumnSet(fields));
        var previous=Client.CallerId;
        try {
            Client.CallerId=user.Id;
            foreach(var field in fields) {
                var target=new Entity(Employee,employee.Id){[field]=before.Contains(field)?before[field]:null};
                try { Client.Update(target); }
                catch(FaultException<OrganizationServiceFault> ex) when(
                    ex.Detail.Message.Contains("access",StringComparison.OrdinalIgnoreCase) ||
                    ex.Detail.Message.Contains("privilege",StringComparison.OrdinalIgnoreCase) ||
                    ex.Detail.Message.Contains("organization-wide",StringComparison.OrdinalIgnoreCase)) {
                    Console.WriteLine("PASS ordinary Employee denied profile update through SDK: "+field);
                    continue;
                }
                throw new Exception("Ordinary Employee unexpectedly updated profile field "+field);
            }
        } finally { Client.CallerId=previous; }
        var after=Client.Retrieve(Employee,employee.Id,new ColumnSet(fields));
        static object? Value(Entity row,string field) => row.GetAttributeValue<object>(field) switch {
            EntityReference reference => (reference.LogicalName,reference.Id),
            OptionSetValue option => option.Value,
            object value => value,
            _ => null
        };
        foreach(var field in fields)
            if(!Equals(Value(before,field),Value(after,field)))
                throw new Exception("Employee profile changed during denied-write test: "+field);
        Console.WriteLine("PASS no tested Employee profile field changed. Superior-role read-only access remains a separate rollout test.");
    }
}
