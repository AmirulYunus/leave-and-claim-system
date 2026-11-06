using System.Reflection;
using Leave.Plugins;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

sealed class EntitlementApiChecks : IServiceProvider, IOrganizationServiceFactory, IOrganizationService
{
    readonly Guid caller = Guid.NewGuid();
    readonly IPluginExecutionContext context = DispatchProxy.Create<IPluginExecutionContext,ContextProxy>();
    readonly Entity employee = new("cr2c9_employee",Guid.NewGuid());
    Guid? serviceUser;
    bool denyRead;
    ParameterCollection output = new();
    EntitlementApiChecks() {
        var values=((ContextProxy)(object)context).Values;
        values["MessageName"]=EmployeeEntitlementPreview.ApiName;
        values["InitiatingUserId"]=caller; values["UserId"]=Guid.NewGuid();
        values["InputParameters"]=new ParameterCollection { {"Target",employee.ToEntityReference()} };
        values["OutputParameters"]=output;
    }
    public static void Run() {
        var f=new EntitlementApiChecks(); new EmployeeEntitlementPreview().Execute(f);
        if (f.serviceUser!=f.caller) throw new Exception("Preview elevated read privileges");
        if (!((string)f.output["PreviewJson"]).Contains("PendingHrDetails")) throw new Exception("Missing inputs failed");
        Console.WriteLine("PASS entitlement API: reads as initiating caller, never SYSTEM; returns null-input preview");
        f=new EntitlementApiChecks { denyRead=true };
        try { new EmployeeEntitlementPreview().Execute(f); throw new Exception("Read denial ignored"); }
        catch (InvalidPluginExecutionException) { if(f.output.Count!=0) throw new Exception("Denied read exposed preview"); }
        Console.WriteLine("PASS entitlement API: record-read denial prevents output");
        f=new EntitlementApiChecks(); f.context.InputParameters["Target"]=new EntityReference("contact",Guid.NewGuid());
        try { new EmployeeEntitlementPreview().Execute(f); throw new Exception("Wrong target accepted"); }
        catch (InvalidPluginExecutionException) { if(f.serviceUser.HasValue) throw new Exception("Invalid target caused a read"); }
        Console.WriteLine("PASS entitlement API: rejects foreign table before reading");
    }
    public object GetService(Type type)=>type==typeof(IPluginExecutionContext)?context:type==typeof(IOrganizationServiceFactory)?this:null;
    public IOrganizationService CreateOrganizationService(Guid? userId) { serviceUser=userId; return this; }
    public Entity Retrieve(string table,Guid id,ColumnSet columns) {
        if (denyRead) throw new InvalidPluginExecutionException("No read access");
        if (table!=employee.LogicalName || id!=employee.Id || columns.AllColumns || columns.Columns.Count!=3) throw new Exception("Unexpected read");
        return employee;
    }
    public Guid Create(Entity row)=>throw new Exception("Preview must not write");
    public void Update(Entity row)=>throw new Exception("Preview must not write");
    public void Delete(string table,Guid id)=>throw new Exception("Preview must not write");
    public OrganizationResponse Execute(OrganizationRequest request)=>throw new Exception("Unexpected request");
    public EntityCollection RetrieveMultiple(QueryBase query)=>throw new Exception("Unexpected query");
    public void Associate(string table,Guid id,Relationship relationship,EntityReferenceCollection related)=>throw new Exception("Preview must not write");
    public void Disassociate(string table,Guid id,Relationship relationship,EntityReferenceCollection related)=>throw new Exception("Preview must not write");
}
