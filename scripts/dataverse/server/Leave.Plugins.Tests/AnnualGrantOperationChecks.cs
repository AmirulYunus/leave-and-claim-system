using System.Reflection;
using Leave.Plugins;
using Leave.BalanceGuards;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Crm.Sdk.Messages;

static class AnnualGrantOperationChecks {
    public static void Run() {
        int count=0;
        void Eq(object a, object b) { if(!Equals(a,b)) throw new Exception($"Expected {b}, got {a}"); }
        void Deny(Action action) { try{action();}catch(InvalidPluginExecutionException){return;} throw new Exception("Expected denial"); }
        void Check(string name, Action<GrantFixture> check) { try{check(new GrantFixture()); count++;}catch(Exception e){throw new Exception(name,e);} }
        Check("first grant creates owned calendar period and earned credit", f=>{var earned=f.Earned();f.Run();Eq(f.Result["Status"],"Granted");Eq(f.Result["CreditedHours"],earned);Eq(f.Result["AvailableHours"],earned);Eq(f.Entries.Count(),1);Eq(f.Periods.Single().GetAttributeValue<EntityReference>("ownerid").Id,f.Owner);});
        Check("repeat action does not double grant", f=>{f.Run();f.Run();Eq(f.Result["Status"],"AlreadyGranted");Eq(f.Result["CreditedHours"],0);Eq(f.Entries.Count(),1);});
        Check("null HR input preserves prior grant", f=>{var earned=f.Earned();f.Run();f.Employee["cr2c9_employeelevel"]=null;f.Run();Eq(f.Result["Status"],"PendingHrDetails");Eq(f.Result["AvailableHours"],earned);Eq(f.Entries.Count(),1);});
        Check("null HR input creates no empty period", f=>{f.Employee["cr2c9_employeelevel"]=null;f.Run();Eq(f.Result["Status"],"PendingHrDetails");Eq(f.Periods.Count(),0);Eq(f.Result["AvailableHours"],-1);});
        Check("first-year grant credits only completed months", f=>{f.Today=new DateTime(2026,11,3);f.Employee["cr2c9_datejoined"]=new DateTime(2026,2,20);var earned=f.Earned();Eq(earned,84);f.Run();Eq(f.Result["Status"],"Granted");Eq(f.Result["CreditedHours"],earned);Eq(f.Entries.Count(),1);});
        Check("last-year leaving date is held", f=>{f.Employee["cr2c9_leavingdate"]=f.Today;f.Run();Eq(f.Result["Status"],"PendingPolicy");Eq(f.Periods.Count(),0);});
        Check("inactive employee receives no new credit", f=>{f.Employee["cr2c9_isactive"]=false;f.Run();Eq(f.Result["Status"],"Inactive");Eq(f.Periods.Count(),0);});
        Check("deactivated record preserves existing credit", f=>{var earned=f.Earned();f.Run();f.Employee["statecode"]=new OptionSetValue(1);f.Run();Eq(f.Result["Status"],"Inactive");Eq(f.Result["AvailableHours"],earned);Eq(f.Entries.Count(),1);});
        Check("missing record state fails closed", f=>{f.Employee.Attributes.Remove("statecode");Deny(f.Run);Eq(f.Periods.Count(),0);});
        Check("eight-year reward is held", f=>{f.Employee["cr2c9_datejoined"]=f.Today.AddYears(-8);f.Run();Eq(f.Result["Status"],"PendingPolicy");});
        Check("anniversary rerun credits only a rounded cumulative increase", f=>{f.Employee["cr2c9_datejoined"]=f.Today.AddYears(-2).AddDays(1);var before=f.Earned();f.Run();Eq(f.Result["AvailableHours"],before);f.Employee["cr2c9_datejoined"]=f.Today.AddYears(-2);var after=f.Earned();f.Run();Eq(f.Result["CreditedHours"],Math.Max(0,after-before));Eq(f.Result["AvailableHours"],Math.Max(before,after));});
        Check("basic privilege does not authorize action", f=>{f.Depth=PrivilegeDepth.Basic;Deny(f.Run);Eq(f.Writes,0);});
        Check("missing privilege does not authorize action", f=>{f.HasPrivilege=false;Deny(f.Run);Eq(f.Writes,0);});
        Check("caller read access checked before employee lock", f=>{f.NoCallerRead=true;Deny(f.Run);Eq(f.Writes,0);});
        Check("disabled caller fails before writes", f=>{f.User["isdisabled"]=true;Deny(f.Run);Eq(f.Writes,0);});
        Check("Web API app telemetry does not change authorization or amount", f=>{var earned=f.Earned();f.Input["x-ms-app-name"]="untrusted";f.Run();Eq(f.Result["CreditedHours"],earned);f.Depth=PrivilegeDepth.Basic;Deny(f.Run);});
        Check("client supplied amount rejected", f=>{f.Input["Hours"]=999;Deny(f.Run);Eq(f.Writes,0);});
        Check("client supplied year rejected", f=>{f.Input["Year"]=2000;Deny(f.Run);Eq(f.Writes,0);});
        Check("nontransaction action fails closed", f=>{f.ContextValues["IsInTransaction"]=false;Deny(f.Run);Eq(f.Writes,0);});
        Check("employee CAS conflict stops period creation", f=>{f.Conflict=true;Deny(f.Run);Eq(f.Periods.Count(),0);});
        Check("inactive category blocks grants", f=>{f.Type["statecode"]=new OptionSetValue(1);Deny(f.Run);Eq(f.Entries.Count(),0);});
        Check("external ledger creation denied regardless of privilege", f=>Deny(()=>f.External("Create",new Entity(BalancePersistence.EntryTable))));
        foreach(var m in new[]{"Update","Delete","Assign","SetState","CreateMultiple","UpdateMultiple"})
            Check("external ledger "+m+" denied", f=>Deny(()=>f.External(m,new Entity(BalancePersistence.EntryTable,Guid.NewGuid()))));
        Check("foreign nested balance scope denied", f=>{var period=new Entity(BalancePersistence.PeriodTable){["cr2c9_employee"]=new EntityReference(Guard.Employee,Guid.NewGuid())};Deny(()=>f.Nested("Create",period));});
        Check("nested ledger modification denied", f=>Deny(()=>f.Nested("Update",new Entity(BalancePersistence.EntryTable,Guid.NewGuid()))));
        Check("narrower employee writer cannot alter HR inputs", f=>{f.Depth=PrivilegeDepth.Basic;Deny(()=>f.Maintain(new Entity(Guard.Employee,f.Employee.Id){["cr2c9_employeelevel"]=new OptionSetValue(290040003)}));});
        Check("narrower employee writer cannot alter synced identity", f=>{f.Depth=PrivilegeDepth.Basic;Deny(()=>f.Maintain(new Entity(Guard.Employee,f.Employee.Id){["cr2c9_entrauserid"]=Guid.NewGuid().ToString()}));});
        Check("narrower employee writer cannot alter synced Superior", f=>{f.Depth=PrivilegeDepth.Basic;Deny(()=>f.Maintain(new Entity(Guard.Employee,f.Employee.Id){["cr2c9_superior"]=new EntityReference(Guard.Employee,Guid.NewGuid())}));});
        Check("HR-like global writer can maintain policy input without create privilege", f=>{f.HasCreatePrivilege=false;f.Maintain(new Entity(Guard.Employee,f.Employee.Id){["cr2c9_employeelevel"]=new OptionSetValue(290040003)});});
        Check("HR-like global writer cannot change synced Entra identity", f=>{f.HasCreatePrivilege=false;Deny(()=>f.Maintain(new Entity(Guard.Employee,f.Employee.Id){["cr2c9_entrauserid"]=Guid.NewGuid().ToString()}));});
        Check("HR-like global writer cannot change synced Superior", f=>{f.HasCreatePrivilege=false;Deny(()=>f.Maintain(new Entity(Guard.Employee,f.Employee.Id){["cr2c9_superior"]=new EntityReference(Guard.Employee,Guid.NewGuid())}));});
        Check("failure after period creation rolls back period and employee lock", f=>{f.FailEntry=true;Deny(f.Run);Eq(f.Periods.Count(),0);Eq(f.Entries.Count(),0);Eq(f.Employee.RowVersion,"1");});
        Console.WriteLine($"{count} annual grant action and write-boundary checks passed.");
    }
}
sealed class GrantFixture : IServiceProvider,IOrganizationServiceFactory,IOrganizationService {
    public DateTime Today=EntitlementCalculator.MalaysiaDate(DateTime.UtcNow);
    public Guid Actor=Guid.NewGuid(),Owner=Guid.NewGuid(),CreatePrivilege=Guid.NewGuid(),WritePrivilege=Guid.NewGuid();
    public PrivilegeDepth Depth=PrivilegeDepth.Global;
    public bool HasPrivilege=true,HasCreatePrivilege=true,NoCallerRead,Conflict,FailEntry;
    public int Writes; Guid? serviceUser;
    Dictionary<(string,Guid),Entity> rows=new();
    IPluginExecutionContext context;
    readonly IPluginExecutionContext main;
    public Dictionary<string,object> ContextValues=>((ContextProxy)(object)main).Values;
    public ParameterCollection Input= new(),Result=new();
    public Entity Employee=>rows.Values.Single(x=>x.LogicalName==Guard.Employee);
    public Entity User=>rows[("systemuser",Actor)];
    public Entity Type=>rows[("cr2c9_leavetype",GrantContract.AnnualType)];
    public int Earned()=>CalendarAnnualGrant.ApprovalEarnedCap(
        Employee.GetAttributeValue<DateTime?>("cr2c9_datejoined"),
        Employee.GetAttributeValue<OptionSetValue>("cr2c9_employeelevel")?.Value,
        Employee.GetAttributeValue<OptionSetValue>("cr2c9_employmenttype")?.Value,
        Employee.GetAttributeValue<bool?>("cr2c9_isactive"),
        Employee.GetAttributeValue<DateTime?>("cr2c9_leavingdate"),new DateTime(Today.Year,1,1),Today);
    public IEnumerable<Entity> Periods=>rows.Values.Where(x=>x.LogicalName==BalancePersistence.PeriodTable);
    public IEnumerable<Entity> Entries=>rows.Values.Where(x=>x.LogicalName==BalancePersistence.EntryTable);
    public GrantFixture() {
        var emp=new Entity(Guard.Employee,Guid.NewGuid()){RowVersion="1"};emp["statecode"]=new OptionSetValue(0);emp["cr2c9_datejoined"]=Today.AddYears(-5);emp["cr2c9_employeelevel"]=new OptionSetValue(290040000);emp["cr2c9_employmenttype"]=new OptionSetValue(290040000);emp["cr2c9_isactive"]=true;emp["ownerid"]=new EntityReference("systemuser",Owner);rows[(emp.LogicalName,emp.Id)]=emp;
        rows[("systemuser",Actor)]=new Entity("systemuser",Actor);
        var type=new Entity("cr2c9_leavetype",GrantContract.AnnualType);type["statecode"]=new OptionSetValue(0);rows[(type.LogicalName,type.Id)]=type;
        Input["Target"]=emp.ToEntityReference();main=Ctx(GrantContract.Api,emp.ToEntityReference(),null,30);context=main;
        ContextValues["InputParameters"]=Input;ContextValues["OutputParameters"]=Result;
    }
    Entity Copy(Entity e) {var c=new Entity(e.LogicalName,e.Id){RowVersion=e.RowVersion};foreach(var a in e.Attributes)c[a.Key]=a.Value;return c;}
    IPluginExecutionContext Ctx(string message,object target,IPluginExecutionContext parent,int stage=20) {
        var c=DispatchProxy.Create<IPluginExecutionContext,ContextProxy>();var v=((ContextProxy)(object)c).Values;
        var e=target as Entity;var er=target as EntityReference;
        v["MessageName"]=message;v["Stage"]=stage;v["Mode"]=0;v["IsInTransaction"]=true;v["InitiatingUserId"]=Actor;v["UserId"]=Actor;
        v["PrimaryEntityName"]=e?.LogicalName??er.LogicalName;v["PrimaryEntityId"]=e?.Id??er.Id;v["ParentContext"]=parent;
        v["InputParameters"]=new ParameterCollection{{"Target",target}};return c;
    }
    public void Run() {var saved=rows.ToDictionary(x=>x.Key,x=>Copy(x.Value));context=main;try{new CalendarAnnualGrantOperation().Execute(this);}catch{rows=saved;throw;}}
    public void External(string m,Entity e){context=Ctx(m,e,null);try{new BalanceStagingGuard().Execute(this);}finally{context=main;}}
    public void Nested(string m,Entity e){
        var old=context;
        var core=Ctx(m,e,old,30);
        // Live standard-table single writes also trigger multiple-operation steps.
        var bulk=Ctx(m+"Multiple",e,core);
        var parameters=new ParameterCollection{{"Targets",new EntityCollection(new[]{e}){EntityName=e.LogicalName}}};
        ((ContextProxy)(object)bulk).Values["InputParameters"]=parameters;
        context=bulk;
        try{new BalanceStagingGuard().Execute(this);context=Ctx(m,e,core);new BalanceStagingGuard().Execute(this);}finally{context=old;}
    }
    public void Maintain(Entity e){var old=context;context=Ctx("Update",e,null);try{new EmploymentInputGuard().Execute(this);}finally{context=old;}}
    public object GetService(System.Type t)=>t==typeof(IPluginExecutionContext)?context:t==typeof(IOrganizationServiceFactory)?this:null;
    public IOrganizationService CreateOrganizationService(Guid? user){serviceUser=user;return this;}
    public Entity Retrieve(string t,Guid id,ColumnSet columns){if(t==Guard.Employee && serviceUser==Actor && NoCallerRead)throw new InvalidPluginExecutionException("No caller read");return Copy(rows[(t,id)]);}
    public EntityCollection RetrieveMultiple(QueryBase query) {
        var q=(QueryExpression)query;
        var found=rows.Values.Where(x=>x.LogicalName==q.EntityName);
        foreach(var condition in q.Criteria.Conditions) found=found.Where(x=>Match(x,condition));
        return new EntityCollection(found.Select(Copy).ToArray());
    }
    bool Match(Entity e,ConditionExpression c) {
        var value=e.GetAttributeValue<object>(c.AttributeName);if(value is EntityReference er)value=er.Id;
        if(c.Operator==ConditionOperator.Equal)return Equals(value,c.Values[0]);
        if(value is DateTime date && c.Values[0] is DateTime expected) return c.Operator==ConditionOperator.LessEqual ? date<=expected : date>=expected;
        throw new NotSupportedException(c.Operator.ToString());
    }
    EntityMetadata PrivilegeMetadata() {
        var metadata=new EntityMetadata();
        SecurityPrivilegeMetadata PrivilegeRow(PrivilegeType type) {
            var row=(SecurityPrivilegeMetadata)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(SecurityPrivilegeMetadata));
            typeof(SecurityPrivilegeMetadata).GetProperty("PrivilegeId").SetValue(row,type==PrivilegeType.Create?CreatePrivilege:WritePrivilege);
            typeof(SecurityPrivilegeMetadata).GetProperty("PrivilegeType").SetValue(row,type);
            return row;
        }
        typeof(EntityMetadata).GetProperty("Privileges").SetValue(metadata,new[]{PrivilegeRow(PrivilegeType.Create),PrivilegeRow(PrivilegeType.Write)});
        return metadata;
    }
    public OrganizationResponse Execute(OrganizationRequest request) {
        if(request is RetrieveEntityRequest) return new RetrieveEntityResponse { Results=new ParameterCollection{{"EntityMetadata",PrivilegeMetadata()}} };
        if(request is RetrieveUserPrivilegesRequest) return new RetrieveUserPrivilegesResponse {Results=new ParameterCollection{{"RolePrivileges",HasPrivilege?
            (HasCreatePrivilege?new[]{new RolePrivilege{PrivilegeId=CreatePrivilege,Depth=Depth},new RolePrivilege{PrivilegeId=WritePrivilege,Depth=Depth}}:
                new[]{new RolePrivilege{PrivilegeId=WritePrivilege,Depth=Depth}}):Array.Empty<RolePrivilege>()}}};
        if(request is UpdateRequest u) {
            var saved=rows[(u.Target.LogicalName,u.Target.Id)];if(Conflict || saved.RowVersion!=u.Target.RowVersion || u.ConcurrencyBehavior!=ConcurrencyBehavior.IfRowVersionMatches)throw new InvalidPluginExecutionException("CAS conflict");
            if(saved.LogicalName==BalancePersistence.PeriodTable)Nested("Update",u.Target);
            foreach(var a in u.Target.Attributes)saved[a.Key]=a.Value;saved.RowVersion=(int.Parse(saved.RowVersion)+1).ToString();Writes++;return new UpdateResponse();
        }
        throw new NotSupportedException(request.RequestName);
    }
    public Guid Create(Entity e) {Nested("Create",e);if(FailEntry && e.LogicalName==BalancePersistence.EntryTable)throw new InvalidPluginExecutionException("Late write fault");e.Id=Guid.NewGuid();e.RowVersion="1";rows[(e.LogicalName,e.Id)]=Copy(e);Writes++;return e.Id;}
    public void Update(Entity e)=>throw new NotSupportedException();public void Delete(string t,Guid id)=>throw new NotSupportedException();
    public void Associate(string t,Guid id,Relationship r,EntityReferenceCollection e)=>throw new NotSupportedException();public void Disassociate(string t,Guid id,Relationship r,EntityReferenceCollection e)=>throw new NotSupportedException();
}
