using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

internal static partial class Development {
    const string HrRoleName = "Leave HR";
    static readonly Guid HrApp = Guid.Parse("08f2f659-16c2-f111-aaad-000d3a826b69");

    // Prepares the role in the development solution. It never assigns users.
    // The Employee safeguard requires a stronger global Create privilege for
    // Entra identity and synced Superior changes, which this role does not get.
    static void HrRoleDeploy() {
        var app=Client.Retrieve("appmodule",HrApp,new ColumnSet("name","statecode"));
        if(app.GetAttributeValue<string>("name")!="HR leave operations" ||
            app.GetAttributeValue<OptionSetValue>("statecode")?.Value!=0)
            throw new Exception("HR app identity or state did not verify; no role was created.");
        var me=((WhoAmIResponse)Client.Execute(new WhoAmIRequest())).UserId;
        var bu=Client.Retrieve("systemuser",me,new ColumnSet("businessunitid"))
            .GetAttributeValue<EntityReference>("businessunitid")?.Id ?? Guid.Empty;
        if(bu==Guid.Empty)throw new Exception("Current business unit is missing.");
        var roles=Find("role","name",HrRoleName,"name","businessunitid")
            .Where(r=>r.GetAttributeValue<EntityReference>("businessunitid")?.Id==bu).ToArray();
        if(roles.Length>1)throw new Exception("Duplicate Leave HR roles in this business unit.");
        var roleId=roles.Length==1?roles[0].Id:Client.Create(new Entity("role"){
            ["name"]=HrRoleName,["businessunitid"]=new EntityReference("businessunit",bu)
        });
        var wanted=new List<RolePrivilege>();
        var forbidden=new List<Guid>();
        void Grant(string table,params PrivilegeType[] types) {
            var meta=((RetrieveEntityResponse)Client.Execute(new RetrieveEntityRequest{
                LogicalName=table,EntityFilters=EntityFilters.Entity|EntityFilters.Privileges
            })).EntityMetadata;
            foreach(var type in types)
                wanted.Add(new RolePrivilege((int)PrivilegeDepth.Global,
                    meta.Privileges.Single(p=>p.PrivilegeType==type).PrivilegeId));
            if(table==Employee)
                forbidden.AddRange(meta.Privileges.Where(p=>p.PrivilegeType==PrivilegeType.Create ||
                    p.PrivilegeType==PrivilegeType.Delete || p.PrivilegeType==PrivilegeType.Assign ||
                    p.PrivilegeType==PrivilegeType.Share).Select(p=>p.PrivilegeId));
            if(table==CarryForwardReviewTable || table==CarryForwardCreditTable || table==CarryForwardUsageTable || table==CarryForwardExceptionTable)
                forbidden.AddRange(meta.Privileges.Where(p=>p.PrivilegeType!=PrivilegeType.Read)
                    .Select(p=>p.PrivilegeId));
        }
        Grant(Employee,PrivilegeType.Read,PrivilegeType.Write,PrivilegeType.Append,PrivilegeType.AppendTo);
        Grant(Request,PrivilegeType.Read,PrivilegeType.Write);
        Grant("cr2c9_leavecancellation",PrivilegeType.Create,PrivilegeType.Read,
            PrivilegeType.Write,PrivilegeType.Append,PrivilegeType.AppendTo);
        Grant("cr2c9_leaverequestdate",PrivilegeType.Read);
        Grant("cr2c9_leavedecision",PrivilegeType.Read);
        Grant("cr2c9_leavebalanceperiod",PrivilegeType.Create,PrivilegeType.Read);
        Grant("cr2c9_leavebalanceentry",PrivilegeType.Read);
        Grant(CarryForwardReviewTable,PrivilegeType.Read);
        Grant(CarryForwardCreditTable,PrivilegeType.Read);
        Grant(CarryForwardUsageTable,PrivilegeType.Read);
        Grant(CarryForwardExceptionTable,PrivilegeType.Read);
        Grant("cr2c9_leavetype",PrivilegeType.Read);
        Grant("annotation",PrivilegeType.Read);
        // Model-driven app visibility requires Read on App Module in addition
        // to the role-to-app association and the leave-table permissions.
        Grant("appmodule",PrivilegeType.Read);
        // The model-driven runtime retrieves entity metadata before it opens
        // any app page. Custom roles made through this SDK do not inherit the
        // built-in App Opener baseline automatically.
        wanted.Add(new RolePrivilege((int)PrivilegeDepth.Global,
            Find("privilege", "name", "prvReadEntity", "name").Single().Id));
        var present=((RetrieveRolePrivilegesRoleResponse)Client.Execute(
            new RetrieveRolePrivilegesRoleRequest{RoleId=roleId})).RolePrivileges;
        if(present.Any(p=>forbidden.Contains(p.PrivilegeId)))
            throw new Exception("Existing Leave HR role has forbidden Employee privileges; no grants added.");
        var toAdd=wanted.Where(p=>!present.Any(x=>x.PrivilegeId==p.PrivilegeId && x.Depth==p.Depth)).ToArray();
        if(toAdd.Length!=0)Client.Execute(new AddPrivilegesRoleRequest{RoleId=roleId,Privileges=toAdd});
        var after=((RetrieveRolePrivilegesRoleResponse)Client.Execute(
            new RetrieveRolePrivilegesRoleRequest{RoleId=roleId})).RolePrivileges;
        if(wanted.Any(p=>!after.Any(x=>x.PrivilegeId==p.PrivilegeId && x.Depth==p.Depth)) ||
            after.Any(p=>forbidden.Contains(p.PrivilegeId)))
            throw new Exception("Leave HR privileges did not verify.");
        var linked=new QueryExpression("role") {ColumnSet=new ColumnSet(false)};
        linked.Criteria.AddCondition("roleid",ConditionOperator.Equal,roleId);
        var appLink=linked.AddLink("appmoduleroles","roleid","roleid");
        appLink.LinkCriteria.AddCondition("appmoduleid",ConditionOperator.Equal,HrApp);
        if(Client.RetrieveMultiple(linked).Entities.Count==0)
            Client.Associate("appmodule",HrApp,new Relationship("appmoduleroles_association"),
                new EntityReferenceCollection{new EntityReference("role",roleId)});
        if(Client.RetrieveMultiple(linked).Entities.Count!=1)
            throw new Exception("HR app role association did not verify.");
        Add(roleId,20);
        Console.WriteLine("Leave HR role prepared and associated with HR app. No user was assigned or notified.");
    }
}
