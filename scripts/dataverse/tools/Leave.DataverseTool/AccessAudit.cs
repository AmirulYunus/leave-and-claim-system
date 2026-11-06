using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Crm.Sdk.Messages;

internal static partial class Development {
    static void AccessInspect() {
        var current=((WhoAmIResponse)Client.Execute(new WhoAmIRequest())).UserId;
        var activeUsers=new QueryExpression("systemuser") {ColumnSet=new ColumnSet("isdisabled","azureactivedirectoryobjectid")};
        activeUsers.Criteria.AddCondition("isdisabled",ConditionOperator.Equal,false);
        var users=Client.RetrieveMultiple(activeUsers).Entities;
        Console.WriteLine("Active Dataverse users: "+users.Count+"; with Entra object ID: "+users.Count(x=>x.GetAttributeValue<Guid>("azureactivedirectoryobjectid")!=Guid.Empty));
        foreach(var email in new[]{"KhorLord.Yong@Asteriskdigital.com.my","Jennie.Cheong@Asteriskdigital.com.my"}) {
            var q=new QueryExpression("systemuser") {ColumnSet=new ColumnSet("isdisabled","azureactivedirectoryobjectid")};
            q.Criteria.AddCondition("internalemailaddress",ConditionOperator.Equal,email);
            var found=Client.RetrieveMultiple(q).Entities;
            Console.WriteLine("Configured HR identity "+email+": matching users="+found.Count+", active="+found.Count(x=>!x.GetAttributeValue<bool>("isdisabled"))+", verified Entra="+found.Count(x=>x.GetAttributeValue<Guid>("azureactivedirectoryobjectid")!=Guid.Empty));
            foreach(var user in found)Console.WriteLine("  roles="+string.Join(", ",UserRoleNames(user.Id)));
        }
        Console.WriteLine("Current developer roles: "+string.Join(", ",UserRoleNames(current)));
        foreach(var employeeId in new[]{FixtureEmployee,AccountingEmployee}) {
            var row=Client.Retrieve(Employee,employeeId,new ColumnSet("cr2c9_entrauserid","cr2c9_superior","ownerid","cr2c9_isactive"));
            var manager=row.GetAttributeValue<EntityReference>("cr2c9_superior");
            Console.WriteLine("Fixture "+employeeId+": active="+row.GetAttributeValue<bool>("cr2c9_isactive")+", ownerIsCurrent="+(row.GetAttributeValue<EntityReference>("ownerid")?.Id==current)+", superior="+manager?.Id+", identityPresent="+Guid.TryParse(row.GetAttributeValue<string>("cr2c9_entrauserid"),out _));
        }
    }
    static string[] UserRoleNames(Guid userId) {
        var q=new QueryExpression("role") {ColumnSet=new ColumnSet("name")};
        var link=q.AddLink("systemuserroles","roleid","roleid");
        link.LinkCriteria.AddCondition("systemuserid",ConditionOperator.Equal,userId);
        return Client.RetrieveMultiple(q).Entities.Select(x=>x.GetAttributeValue<string>("name")).Where(x=>!string.IsNullOrEmpty(x)).OrderBy(x=>x).ToArray()!;
    }
    static void OrdinaryCandidateInspect() {
        var q=new QueryExpression(Employee) {ColumnSet=new ColumnSet("cr2c9_employeename","cr2c9_email","cr2c9_entrauserid","ownerid","cr2c9_isactive")};
        q.Criteria.AddCondition("cr2c9_employeename",ConditionOperator.Like,"%Darwisyah%Fakhira%");
        var matches=Client.RetrieveMultiple(q).Entities;
        Console.WriteLine("Darwisyah Fakhira Employee matches: "+matches.Count);
        foreach(var row in matches) {
            var email=row.GetAttributeValue<string>("cr2c9_email");
            var entra=Guid.TryParse(row.GetAttributeValue<string>("cr2c9_entrauserid"),out var parsed)?parsed:Guid.Empty;
            Console.WriteLine("Employee="+row.Id+"; name="+row.GetAttributeValue<string>("cr2c9_employeename")+"; email="+email+"; active="+row.GetAttributeValue<bool>("cr2c9_isactive")+"; Entra ID present="+(entra!=Guid.Empty));
            if(string.IsNullOrWhiteSpace(email))continue;
            var users=new QueryExpression("systemuser") {ColumnSet=new ColumnSet("fullname","isdisabled","azureactivedirectoryobjectid","internalemailaddress")};
            users.Criteria.AddCondition("internalemailaddress",ConditionOperator.Equal,email);
            var found=Client.RetrieveMultiple(users).Entities;
            Console.WriteLine("Matching Dataverse users="+found.Count);
            foreach(var user in found)Console.WriteLine("  user="+user.GetAttributeValue<string>("fullname")+"; enabled="+!user.GetAttributeValue<bool>("isdisabled")+"; Entra ID matches Employee="+(entra!=Guid.Empty&&user.GetAttributeValue<Guid>("azureactivedirectoryobjectid")==entra)+"; roles="+string.Join(", ",UserRoleNames(user.Id)));
        }
    }
    static void OrdinaryCandidateRemovePrivilegedRoles() {
        const string email="Darwisyah.Fakhira@Asteriskdigital.com.my";
        var employee=Find(Employee,"cr2c9_email",email,"cr2c9_employeename","cr2c9_entrauserid","cr2c9_isactive").Single();
        if(employee.GetAttributeValue<string>("cr2c9_employeename")!="Darwisyah Fakhira" ||
            !employee.GetAttributeValue<bool>("cr2c9_isactive") ||
            !Guid.TryParse(employee.GetAttributeValue<string>("cr2c9_entrauserid"),out var entra))
            throw new Exception("Darwisyah Employee identity preflight failed; no roles changed.");
        var user=Find("systemuser","internalemailaddress",email,
            "fullname","isdisabled","azureactivedirectoryobjectid","businessunitid").Single();
        if(user.GetAttributeValue<string>("fullname")!="Darwisyah Fakhira #" ||
            user.GetAttributeValue<bool>("isdisabled") ||
            user.GetAttributeValue<Guid>("azureactivedirectoryobjectid")!=entra)
            throw new Exception("Darwisyah Dataverse identity preflight failed; no roles changed.");
        var before=UserRoleNames(user.Id);
        if(!before.Contains("Leave Employee (development test)") ||
            before.Except(new[]{"Leave Employee (development test)","Leave HR","Leave Superior"}).Any())
            throw new Exception("Unexpected role assignment; no roles changed: "+string.Join(", ",before));
        var bu=user.GetAttributeValue<EntityReference>("businessunitid")?.Id ?? Guid.Empty;
        if(bu==Guid.Empty)throw new Exception("Darwisyah business unit missing; no roles changed.");
        foreach(var roleName in new[]{"Leave HR","Leave Superior"}) {
            if(!before.Contains(roleName))continue;
            var role=Find("role","name",roleName,"businessunitid")
                .Single(r=>r.GetAttributeValue<EntityReference>("businessunitid")?.Id==bu);
            Client.Disassociate("systemuser",user.Id,new Relationship("systemuserroles_association"),
                new EntityReferenceCollection{role.ToEntityReference()});
        }
        var after=UserRoleNames(user.Id);
        if(after.Length!=1 || after[0]!="Leave Employee (development test)")
            throw new Exception("Darwisyah role cleanup needs manual review: "+string.Join(", ",after));
        Console.WriteLine("PASS Darwisyah retains only Leave Employee (development test) as a direct Dataverse role.");
    }
    static void OrdinaryCandidateBasicRole() {
        const string email="Darwisyah.Fakhira@Asteriskdigital.com.my";
        var employee=Find(Employee,"cr2c9_email",email,"cr2c9_employeename","cr2c9_entrauserid","cr2c9_isactive").Single();
        if(employee.GetAttributeValue<string>("cr2c9_employeename")!="Darwisyah Fakhira" || !employee.GetAttributeValue<bool>("cr2c9_isactive") ||
            !Guid.TryParse(employee.GetAttributeValue<string>("cr2c9_entrauserid"),out var entra))throw new Exception("Ordinary candidate Employee identity preflight failed.");
        var user=Find("systemuser","internalemailaddress",email,"fullname","isdisabled","azureactivedirectoryobjectid","businessunitid").Single();
        if(user.GetAttributeValue<string>("fullname")!="Darwisyah Fakhira #" || user.GetAttributeValue<bool>("isdisabled") ||
            user.GetAttributeValue<Guid>("azureactivedirectoryobjectid")!=entra)throw new Exception("Ordinary candidate Dataverse identity preflight failed.");
        var bu=user.GetAttributeValue<EntityReference>("businessunitid")?.Id ?? Guid.Empty;
        if(bu==Guid.Empty)throw new Exception("Candidate business unit missing.");
        var current=UserRoleNames(user.Id);
        if(current.Except(new[]{"Basic User","Environment Maker"}).Any() || !current.Contains("Environment Maker"))
            throw new Exception("Unexpected candidate role assignment; no changes made: "+string.Join(", ",current));
        Entity Role(string name) {
            var q=new QueryExpression("role") {ColumnSet=new ColumnSet("name","businessunitid")};
            q.Criteria.AddCondition("name",ConditionOperator.Equal,name);
            q.Criteria.AddCondition("businessunitid",ConditionOperator.Equal,bu);
            return Client.RetrieveMultiple(q).Entities.Single();
        }
        var basic=Role("Basic User");
        var maker=Role("Environment Maker");
        var relation=new Relationship("systemuserroles_association");
        if(!current.Contains("Basic User"))Client.Associate("systemuser",user.Id,relation,new EntityReferenceCollection{basic.ToEntityReference()});
        if(!UserRoleNames(user.Id).Contains("Basic User"))throw new Exception("Basic User assignment did not verify; Maker retained.");
        Client.Disassociate("systemuser",user.Id,relation,new EntityReferenceCollection{maker.ToEntityReference()});
        var after=UserRoleNames(user.Id);
        if(after.Length!=1 || after[0]!="Basic User")throw new Exception("Unexpected direct roles after candidate transition: "+string.Join(", ",after));
        Console.WriteLine("Darwisyah Fakhira now has Basic User only as a direct Dataverse role. Employee-table permissions, app sharing, indirect/team roles and own-sign-in tests remain to verify.");
    }
}
