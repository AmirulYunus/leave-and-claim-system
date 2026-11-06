using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

internal static partial class Development {
    const string SuperiorRoleName = "Leave Superior";
    static readonly Guid SuperiorApp = Guid.Parse("be2d17c3-06c2-f111-aaad-000d3a826b69");

    static void SuperiorRoleInspect() {
        var me=((WhoAmIResponse)Client.Execute(new WhoAmIRequest())).UserId;
        var bu=Client.Retrieve("systemuser",me,new ColumnSet("businessunitid"))
            .GetAttributeValue<EntityReference>("businessunitid")?.Id ?? Guid.Empty;
        if(bu==Guid.Empty)throw new Exception("Current business unit is missing.");
        var roles=Find("role","name",SuperiorRoleName,"name","businessunitid");
        foreach(var role in roles) {
            var privileges=((RetrieveRolePrivilegesRoleResponse)Client.Execute(
                new RetrieveRolePrivilegesRoleRequest{RoleId=role.Id})).RolePrivileges;
            Console.WriteLine($"Superior role {role.Id} in {role.GetAttributeValue<EntityReference>("businessunitid")?.Id}: {privileges.Length} privileges");
            foreach(var table in new[]{Employee,Request,"cr2c9_leavecancellation",SuperiorGrant,"cr2c9_leaveapproval"}) {
                var meta=((RetrieveEntityResponse)Client.Execute(new RetrieveEntityRequest{
                    LogicalName=table,EntityFilters=EntityFilters.Entity|EntityFilters.Privileges
                })).EntityMetadata;
                var actual=meta.Privileges.Where(p=>privileges.Any(x=>x.PrivilegeId==p.PrivilegeId))
                    .Select(p=>p.PrivilegeType+":"+privileges.Single(x=>x.PrivilegeId==p.PrivilegeId).Depth)
                    .ToArray();
                Console.WriteLine("  "+table+": "+(actual.Length==0?"none":string.Join(", ",actual)));
            }
        }
        if(roles.Length==0)Console.WriteLine("Leave Superior role has not been created.");
        var app=Client.Retrieve("appmodule",SuperiorApp,new ColumnSet("name","statecode"));
        Console.WriteLine("Superior app: "+app.GetAttributeValue<string>("name")+"; state "+
            app.GetAttributeValue<OptionSetValue>("statecode")?.Value);
        foreach(var role in roles) {
            var members=new QueryExpression("systemuserroles") {ColumnSet=new ColumnSet(false)};
            members.Criteria.AddCondition("roleid",ConditionOperator.Equal,role.Id);
            Console.WriteLine("  direct role assignments: "+Client.RetrieveMultiple(members).Entities.Count);
        }
        Console.WriteLine("No ordinary Superior account is assigned or verified by this inspection.");
    }

    // Creates a role without assigning it to anybody. Record-level sharing and
    // ordinary-account tests must be complete before assigning this role.
    static void SuperiorRoleDeploy() {
        var me=((WhoAmIResponse)Client.Execute(new WhoAmIRequest())).UserId;
        var bu=Client.Retrieve("systemuser",me,new ColumnSet("businessunitid"))
            .GetAttributeValue<EntityReference>("businessunitid")?.Id ?? Guid.Empty;
        if(bu==Guid.Empty)throw new Exception("Current business unit is missing.");
        var roles=Find("role","name",SuperiorRoleName,"name","businessunitid")
            .Where(r=>r.GetAttributeValue<EntityReference>("businessunitid")?.Id==bu).ToArray();
        if(roles.Length>1)throw new Exception("More than one Leave Superior role exists in this business unit.");
        var roleId=roles.Length==1?roles[0].Id:Client.Create(new Entity("role"){
            ["name"]=SuperiorRoleName,["businessunitid"]=new EntityReference("businessunit",bu)
        });
        var wanted=new List<RolePrivilege>();
        var forbidden=new List<Guid>();
        void Grant(string table,PrivilegeDepth depth,params PrivilegeType[] types) {
            var meta=((RetrieveEntityResponse)Client.Execute(new RetrieveEntityRequest{
                LogicalName=table,EntityFilters=EntityFilters.Entity|EntityFilters.Privileges
            })).EntityMetadata;
            foreach(var type in types)
                wanted.Add(new RolePrivilege((int)depth,meta.Privileges.Single(p=>p.PrivilegeType==type).PrivilegeId));
            if(table==Employee || table=="cr2c9_leaveapproval")
                forbidden.AddRange(meta.Privileges.Where(p=>table==Employee
                    ? p.PrivilegeType!=PrivilegeType.Read
                    : true).Select(p=>p.PrivilegeId));
        }
        Grant(Employee,PrivilegeDepth.Global,PrivilegeType.Read);
        Grant(Request,PrivilegeDepth.Basic,PrivilegeType.Read,PrivilegeType.Write,PrivilegeType.Append,PrivilegeType.AppendTo);
        Grant("cr2c9_leavecancellation",PrivilegeDepth.Basic,PrivilegeType.Read,PrivilegeType.Write,PrivilegeType.Append,PrivilegeType.AppendTo);
        Grant("cr2c9_leaverequestdate",PrivilegeDepth.Basic,PrivilegeType.Read);
        Grant("cr2c9_leavedecision",PrivilegeDepth.Basic,PrivilegeType.Read);
        Grant("cr2c9_leavebalanceperiod",PrivilegeDepth.Basic,PrivilegeType.Read);
        Grant("cr2c9_leavebalanceentry",PrivilegeDepth.Basic,PrivilegeType.Read);
        Grant("cr2c9_leavetype",PrivilegeDepth.Global,PrivilegeType.Read);
        Grant("annotation",PrivilegeDepth.Basic,PrivilegeType.Read);
        Grant("appmodule",PrivilegeDepth.Global,PrivilegeType.Read);
        wanted.Add(new RolePrivilege((int)PrivilegeDepth.Global,
            Find("privilege", "name", "prvReadEntity", "name").Single().Id));
        Grant(SuperiorGrant,PrivilegeDepth.Basic,PrivilegeType.Read);
        Grant("cr2c9_leaveapproval",PrivilegeDepth.Basic);
        var present=((RetrieveRolePrivilegesRoleResponse)Client.Execute(
            new RetrieveRolePrivilegesRoleRequest{RoleId=roleId})).RolePrivileges;
        if(present.Any(p=>forbidden.Contains(p.PrivilegeId)))
            throw new Exception("Existing Leave Superior role has forbidden Employee or legacy Leave Approval privileges; no grants were added.");
        var toAdd=wanted.Where(p=>!present.Any(x=>x.PrivilegeId==p.PrivilegeId && x.Depth==p.Depth)).ToArray();
        if(toAdd.Length!=0)Client.Execute(new AddPrivilegesRoleRequest{RoleId=roleId,Privileges=toAdd});
        var after=((RetrieveRolePrivilegesRoleResponse)Client.Execute(
            new RetrieveRolePrivilegesRoleRequest{RoleId=roleId})).RolePrivileges;
        if(wanted.Any(p=>!after.Any(x=>x.PrivilegeId==p.PrivilegeId && x.Depth==p.Depth)) ||
           after.Any(p=>forbidden.Contains(p.PrivilegeId)))
            throw new Exception("Leave Superior role privileges did not verify.");
        var linked=new QueryExpression("role") {ColumnSet=new ColumnSet(false)};
        linked.Criteria.AddCondition("roleid",ConditionOperator.Equal,roleId);
        var appLink=linked.AddLink("appmoduleroles","roleid","roleid");
        appLink.LinkCriteria.AddCondition("appmoduleid",ConditionOperator.Equal,SuperiorApp);
        if(Client.RetrieveMultiple(linked).Entities.Count==0)
            Client.Associate("appmodule",SuperiorApp,new Relationship("appmoduleroles_association"),
                new EntityReferenceCollection{new EntityReference("role",roleId)});
        if(Client.RetrieveMultiple(linked).Entities.Count!=1)
            throw new Exception("Superior app role association did not verify.");
        Add(roleId,20);
        Console.WriteLine("Leave Superior role prepared with organization-wide Employee Read and no Employee write; basic request/cancellation review rights only. Associated app and added role to solution. No user assigned. Assigned-record access is not yet active.");
    }
}
