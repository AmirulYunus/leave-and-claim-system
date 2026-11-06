using System.Reflection;
using Leave.Plugins;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

internal static class SuperiorAccessChecks
{
    static readonly MethodInfo Change = typeof(SuperiorRecordAccess).GetMethod("Change",
        BindingFlags.NonPublic | BindingFlags.Static)!;
    static readonly MethodInfo ManagerUser = typeof(SuperiorRecordAccess).GetMethod("ManagerUser",
        BindingFlags.NonPublic | BindingFlags.Static)!;

    static void Apply(AccessFixture f, Guid user, EntityReference? record = null,
        AccessRights rights = AccessRights.ReadAccess | AccessRights.WriteAccess)
    {
        try { Change.Invoke(null, new object[] { f, record ?? f.Request, user, rights }); }
        catch (TargetInvocationException e) when (e.InnerException != null) {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw();
            throw;
        }
    }

    static void Expect(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    static void Denied(Action action)
    {
        try { action(); }
        catch (InvalidPluginExecutionException) { return; }
        throw new Exception("Unsafe share transition was accepted.");
    }

    public static void Run()
    {
        Guid Resolve(AccessFixture fixture, EntityReference? manager)
        {
            try { return (Guid)ManagerUser.Invoke(null, new object?[] { fixture, manager, true })!; }
            catch (TargetInvocationException e) when (e.InnerException != null) {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw();
                throw;
            }
        }
        var managerFixture = new AccessFixture();
        var manager = new EntityReference("cr2c9_employee", Guid.NewGuid());
        var identity = Guid.NewGuid();
        var account = Guid.NewGuid();
        managerFixture.Records[(manager.LogicalName, manager.Id)] = new Entity(manager.LogicalName, manager.Id) {
            ["cr2c9_entrauserid"] = identity.ToString("D"), ["cr2c9_isactive"] = true
        };
        managerFixture.Users.Add(new Entity("systemuser", account) {
            ["azureactivedirectoryobjectid"] = identity, ["isdisabled"] = false
        });
        Expect(Resolve(managerFixture, null) == Guid.Empty && Resolve(managerFixture, manager) == account,
            "A valid Superior did not resolve, or an intentionally blank manager resolved.");
        managerFixture.Records[(manager.LogicalName, manager.Id)]["cr2c9_isactive"] = false;
        Denied(() => Resolve(managerFixture, manager));
        managerFixture.Records[(manager.LogicalName, manager.Id)]["cr2c9_isactive"] = true;
        managerFixture.Users[0]["isdisabled"] = true;
        Denied(() => Resolve(managerFixture, manager));
        managerFixture.Users[0]["isdisabled"] = false;
        managerFixture.Records[(manager.LogicalName, manager.Id)]["cr2c9_entrauserid"] = "invalid";
        Denied(() => Resolve(managerFixture, manager));
        managerFixture.Records[(manager.LogicalName, manager.Id)]["cr2c9_entrauserid"] = identity.ToString("D");
        managerFixture.Users.Clear();
        Denied(() => Resolve(managerFixture, manager));

        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var f = new AccessFixture();
        Apply(f, first);
        Expect(f.Right(f.Request, first) == (AccessRights.ReadAccess | AccessRights.WriteAccess) &&
            f.Grants.Count == 1 && f.Grants.Single().GetAttributeValue<EntityReference>("cr2c9_grantee")?.Id == first,
            "New Superior share or provenance missing.");
        Apply(f, first);
        Expect(f.Grants.Count == 1, "Repeated reconciliation duplicated the grant.");
        Apply(f, second);
        Expect(f.Right(f.Request, first) == null && f.Right(f.Request, second) != null &&
            f.Grants.Count == 1 && f.Grants.Single().GetAttributeValue<EntityReference>("cr2c9_grantee")?.Id == second,
            "Reassignment retained the old Superior or lost the new one.");
        f.Shares[(f.Request.LogicalName, f.Request.Id, first)] = AccessRights.ReadAccess;
        Denied(() => Apply(f, first));
        Expect(f.Right(f.Request, second) != null && f.Grants.Single().GetAttributeValue<EntityReference>("cr2c9_grantee")?.Id == second,
            "Manual-share collision altered the recorded grant.");
        Apply(f, Guid.Empty);
        Expect(f.Right(f.Request, second) == null && f.Grants.Count == 0 && f.Right(f.Request, first) == AccessRights.ReadAccess,
            "Unassignment revoked an unrelated manual share.");

        var manual = new AccessFixture();
        manual.Shares[(manual.Request.LogicalName, manual.Request.Id, first)] = AccessRights.ReadAccess | AccessRights.WriteAccess;
        Denied(() => Apply(manual, first));
        Expect(manual.Grants.Count == 0 && manual.Right(manual.Request, first) != null,
            "A pre-existing share was incorrectly adopted as an automated grant.");

        var ownership = new AccessFixture();
        var decision = ownership.AddDecision(first);
        Apply(ownership, first, decision, AccessRights.ReadAccess);
        Apply(ownership, second, decision, AccessRights.ReadAccess);
        Expect(ownership.Records[(decision.LogicalName, decision.Id)]
                .GetAttributeValue<EntityReference>("ownerid")?.Id == ownership.RequestOwner.Id &&
            ownership.Right(decision, first) == null &&
            ownership.Right(decision, second) == AccessRights.ReadAccess,
            "A decision owned by the former Superior was not moved before revocation.");
        Console.WriteLine("PASS Superior grant provenance: manager resolution, idempotent reassignment, unassignment, and manual-share collision.");
    }
}

internal sealed class AccessFixture : IOrganizationService
{
    public EntityReference Request { get; } = new("cr2c9_leaverequest", Guid.NewGuid());
    public EntityReference RequestOwner { get; } = new("systemuser", Guid.NewGuid());
    public List<Entity> Grants { get; } = new();
    public List<Entity> Users { get; } = new();
    public Dictionary<(string, Guid, Guid), AccessRights> Shares { get; } = new();
    public Dictionary<(string, Guid), Entity> Records { get; } = new();

    public AccessFixture()
    {
        Records[(Request.LogicalName, Request.Id)] = new Entity(Request.LogicalName, Request.Id) {
            ["ownerid"] = RequestOwner
        };
    }

    public EntityReference AddDecision(Guid owner)
    {
        var decision = new EntityReference("cr2c9_leavedecision", Guid.NewGuid());
        Records[(decision.LogicalName, decision.Id)] = new Entity(decision.LogicalName, decision.Id) {
            ["ownerid"] = new EntityReference("systemuser", owner),
            ["cr2c9_leaverequest"] = Request
        };
        return decision;
    }

    public AccessRights? Right(EntityReference record, Guid user) =>
        Shares.TryGetValue((record.LogicalName, record.Id, user), out var value) ? value : null;

    public EntityCollection RetrieveMultiple(QueryBase query)
    {
        var q = (QueryExpression)query;
        if (q.EntityName == "systemuser") {
            var match = (Guid)q.Criteria.Conditions.Single().Values.Single();
            return new EntityCollection(Users.Where(u =>
                u.GetAttributeValue<Guid>("azureactivedirectoryobjectid") == match).ToList());
        }
        if (q.EntityName != "cr2c9_superioraccessgrant") throw new NotSupportedException(q.EntityName);
        var condition = q.Criteria.Conditions.Single();
        var target = (Guid)condition.Values.Single();
        return new EntityCollection(Grants.Where(x =>
            x.GetAttributeValue<EntityReference>(condition.AttributeName)?.Id == target).ToList());
    }

    public Entity Retrieve(string table, Guid id, ColumnSet columns) =>
        table == "cr2c9_superioraccessgrant" ? Grants.Single(x => x.Id == id) : Records[(table, id)];

    public Guid Create(Entity entity)
    {
        entity.Id = Guid.NewGuid();
        Grants.Add(entity);
        return entity.Id;
    }

    public void Delete(string table, Guid id)
    {
        var grant = Grants.Single(x => x.LogicalName == table && x.Id == id);
        Grants.Remove(grant);
        foreach (var key in Shares.Keys.Where(x => x.Item1 == table && x.Item2 == id).ToArray()) Shares.Remove(key);
    }

    public OrganizationResponse Execute(OrganizationRequest request)
    {
        if (request is RetrieveSharedPrincipalsAndAccessRequest read) {
            var response = new RetrieveSharedPrincipalsAndAccessResponse();
            response.Results["PrincipalAccesses"] = Shares.Where(x => x.Key.Item1 == read.Target.LogicalName &&
                x.Key.Item2 == read.Target.Id).Select(x => new PrincipalAccess {
                    Principal = new EntityReference("systemuser", x.Key.Item3), AccessMask = x.Value
                }).ToArray();
            return response;
        }
        if (request is GrantAccessRequest grant) {
            Shares[(grant.Target.LogicalName, grant.Target.Id, grant.PrincipalAccess.Principal.Id)] =
                grant.PrincipalAccess.AccessMask;
            return new GrantAccessResponse();
        }
        if (request is RevokeAccessRequest revoke) {
            Shares.Remove((revoke.Target.LogicalName, revoke.Target.Id, revoke.Revokee.Id));
            return new RevokeAccessResponse();
        }
        throw new NotSupportedException(request.RequestName);
    }

    public void Update(Entity entity)
    {
        var stored = Records[(entity.LogicalName, entity.Id)];
        foreach (var attribute in entity.Attributes) stored[attribute.Key] = attribute.Value;
    }
    public void Associate(string table, Guid id, Relationship relationship, EntityReferenceCollection related) => throw new NotSupportedException();
    public void Disassociate(string table, Guid id, Relationship relationship, EntityReferenceCollection related) => throw new NotSupportedException();
}
