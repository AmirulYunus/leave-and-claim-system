using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

internal static partial class Development {
    // These hand-built roles predate the model-driven App Opener baseline.
    // Keep the leave-table permissions separate from Microsoft's runtime set.
    static void AppRuntimeBaselineAudit() => AppRuntimeBaseline(false);
    static void AppRuntimeEntityFix() => AppRuntimeBaseline(true);

    static void AppRuntimeBaseline(bool apply) {
        var misha = Client.Retrieve("systemuser", Guid.Parse("ba8d05a5-04c2-f111-aaad-000d3a826b69"),
            new ColumnSet("fullname", "internalemailaddress", "isdisabled", "azureactivedirectoryobjectid", "businessunitid"));
        if (misha.GetAttributeValue<bool>("isdisabled") ||
            misha.GetAttributeValue<Guid>("azureactivedirectoryobjectid") == Guid.Empty)
            throw new Exception("The reported user is not an active Entra-backed Dataverse user.");
        var bu = misha.GetAttributeValue<EntityReference>("businessunitid")?.Id ?? Guid.Empty;
        if (bu == Guid.Empty) throw new Exception("Reported user's business unit is missing.");
        Console.WriteLine("Reported user: " + misha.GetAttributeValue<string>("fullname") +
            " <" + misha.GetAttributeValue<string>("internalemailaddress") + ">; BU=" + bu +
            "; roles=" + string.Join(", ", UserRoleNames(misha.Id)));

        Entity Role(string name) => Find("role", "name", name, "name", "businessunitid")
            .Single(r => r.GetAttributeValue<EntityReference>("businessunitid")?.Id == bu);
        var opener = Role("App Opener");
        var baseline = ((RetrieveRolePrivilegesRoleResponse)Client.Execute(
            new RetrieveRolePrivilegesRoleRequest { RoleId = opener.Id })).RolePrivileges;
        var readEntity = Find("privilege", "name", "prvReadEntity", "name").Single().Id;
        if (!baseline.Any(p => p.PrivilegeId == readEntity && p.Depth >= PrivilegeDepth.Basic))
            throw new Exception("App Opener does not include the reported missing prvReadEntity privilege.");
        var names = Client.RetrieveMultiple(new QueryExpression("privilege") {
            ColumnSet = new ColumnSet("name")
        }).Entities.ToDictionary(x => x.Id, x => x.GetAttributeValue<string>("name") ?? x.Id.ToString());
        foreach (var roleName in new[] { HrRoleName, SuperiorRoleName }) {
            var role = Role(roleName);
            var present = ((RetrieveRolePrivilegesRoleResponse)Client.Execute(
                new RetrieveRolePrivilegesRoleRequest { RoleId = role.Id })).RolePrivileges;
            var missing = baseline.Where(p => !present.Any(x => x.PrivilegeId == p.PrivilegeId && x.Depth >= p.Depth)).ToArray();
            Console.WriteLine(roleName + ": missing " + missing.Length + "/" + baseline.Length +
                " App Opener privileges: " + string.Join(", ", missing.Select(p =>
                    names.GetValueOrDefault(p.PrivilegeId, p.PrivilegeId.ToString()) + ":" + p.Depth)));
            if (!apply) continue;
            if (!UserRoleNames(misha.Id).Contains(roleName))
                throw new Exception("Reported user does not have " + roleName + "; no privileges changed.");
            var entityRead = present.SingleOrDefault(p => p.PrivilegeId == readEntity);
            if (entityRead != null && entityRead.Depth < PrivilegeDepth.Global)
                throw new Exception(roleName + " has an unexpected weaker Entity Read privilege.");
            if (entityRead == null) Client.Execute(new AddPrivilegesRoleRequest {
                RoleId = role.Id,
                Privileges = new[] { new RolePrivilege((int)PrivilegeDepth.Global, readEntity) }
            });
            var after = ((RetrieveRolePrivilegesRoleResponse)Client.Execute(
                new RetrieveRolePrivilegesRoleRequest { RoleId = role.Id })).RolePrivileges;
            if (!after.Any(p => p.PrivilegeId == readEntity && p.Depth >= PrivilegeDepth.Global))
                throw new Exception("Entity Read did not verify on " + roleName);
            Console.WriteLine("PASS " + roleName + " has required global Entity metadata Read; no other App Opener privileges copied.");
        }
    }
}
