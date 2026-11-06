using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

internal static partial class Development
{
    static void SuperiorAccessFixture()
    {
        var me = ((WhoAmIResponse)Client.Execute(new WhoAmIRequest())).UserId;
        var identity = Client.Retrieve("systemuser", me, new ColumnSet("azureactivedirectoryobjectid"))
            .GetAttributeValue<Guid>("azureactivedirectoryobjectid");
        var fixture = Client.Retrieve(Employee, FixtureEmployee,
            new ColumnSet("cr2c9_superior", "cr2c9_isactive", "cr2c9_employeename"));
        var superior = fixture.GetAttributeValue<EntityReference>("cr2c9_superior");
        if (!fixture.GetAttributeValue<bool>("cr2c9_isactive") || superior?.LogicalName != Employee)
            throw new Exception("Fictional Superior test employee is not active or assigned.");
        var manager = Client.Retrieve(Employee, superior.Id,
            new ColumnSet("cr2c9_entrauserid", "cr2c9_employeename"));
        if (manager.GetAttributeValue<string>("cr2c9_entrauserid") != identity.ToString("D"))
            throw new Exception("Fictional test Superior no longer matches the signed-in developer; no changes made.");
        var requests = Find(Request, "cr2c9_employeename", FixtureEmployee,
            "cr2c9_leaverequestnumber", "cr2c9_status");
        if (requests.Length == 0) throw new Exception("No fictional requests exist for access verification.");
        Console.WriteLine("Fixture request statuses: " + string.Join(", ", requests.Select(x =>
            x.GetAttributeValue<string>("cr2c9_leaverequestnumber") + "=" +
            x.GetAttributeValue<OptionSetValue>("cr2c9_status")?.Value)));

        // Same-value update is an explicit reconciliation operation; it does not
        // alter the synced assignment but exercises every existing child row.
        Client.Update(new Entity(Employee, FixtureEmployee) { ["cr2c9_superior"] = superior });
        int count = 0;
        void Verify(string table, Guid id, AccessRights expected)
        {
            var lookup = table;
            var markers = Find(SuperiorGrant, lookup, id, "cr2c9_grantee");
            if (markers.Length != 1 || markers[0].GetAttributeValue<EntityReference>("cr2c9_grantee")?.Id != me)
                throw new Exception("Missing or duplicate Superior grant provenance for " + table + "/" + id);
            var shared = (RetrieveSharedPrincipalsAndAccessResponse)Client.Execute(
                new RetrieveSharedPrincipalsAndAccessRequest { Target = new EntityReference(table, id) });
            var rights = shared.PrincipalAccesses.Where(x => x.Principal.LogicalName == "systemuser" &&
                x.Principal.Id == me).ToArray();
            if (rights.Length != 1 || rights[0].AccessMask != expected)
                throw new Exception("Superior direct share differs for " + table + "/" + id);
            count++;
        }
        foreach (var request in requests) {
            Verify(Request, request.Id, AccessRights.ReadAccess | AccessRights.WriteAccess);
            foreach (var date in Find("cr2c9_leaverequestdate", "cr2c9_leaverequest", request.Id))
                Verify("cr2c9_leaverequestdate", date.Id, AccessRights.ReadAccess);
            foreach (var decision in Find(History, "cr2c9_leaverequest", request.Id))
                Verify(History, decision.Id, AccessRights.ReadAccess);
            foreach (var cancellation in Find(Cancellation, "cr2c9_leaverequest", request.Id))
                Verify(Cancellation, cancellation.Id, AccessRights.ReadAccess | AccessRights.WriteAccess);
        }
        Client.Update(new Entity(Employee, FixtureEmployee) { ["cr2c9_superior"] = superior });
        foreach (var request in requests) {
            if (Find(SuperiorGrant, "cr2c9_leaverequest", request.Id).Length != 1)
                throw new Exception("Repeated reconciliation duplicated a request grant.");
        }
        Console.WriteLine("PASS fictional Superior share backfill and repeat reconciliation: " +
            requests.Length + " requests, " + count + " record grants. No role assigned to an ordinary user.");
    }
}
