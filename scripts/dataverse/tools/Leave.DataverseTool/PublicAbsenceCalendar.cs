using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

internal static partial class Development
{
    const string PublicAbsenceTable = "cr2c9_publicabsence";

    static void PublicAbsenceSchemaDeploy()
    {
        var existing = ((RetrieveAllEntitiesResponse)Client.Execute(new RetrieveAllEntitiesRequest {
            EntityFilters = EntityFilters.Entity, RetrieveAsIfPublished = true
        })).EntityMetadata.SingleOrDefault(e => e.LogicalName == PublicAbsenceTable);
        if (existing == null) {
            Client.Execute(new CreateEntityRequest {
                SolutionUniqueName = Solution,
                Entity = new EntityMetadata {
                    SchemaName = PublicAbsenceTable,
                    DisplayName = L("Public Leave Absence"),
                    DisplayCollectionName = L("Public Leave Absences"),
                    Description = L("Approved absence calendar: name, dates and daily duration only. No private leave request details."),
                    OwnershipType = OwnershipTypes.OrganizationOwned,
                    IsActivity = false
                },
                PrimaryAttribute = Text("cr2c9_name", "Employee Display Name", 200, true),
                HasActivities = false, HasNotes = false
            });
        } else if (existing.OwnershipType != OwnershipTypes.OrganizationOwned || existing.IsActivity == true)
            throw new Exception("Public absence table has the wrong ownership contract.");

        foreach (var field in new[] { ("cr2c9_startdate", "Start Date"), ("cr2c9_enddate", "End Date") })
            Attribute(PublicAbsenceTable, new DateTimeAttributeMetadata {
                SchemaName = field.Item1, DisplayName = L(field.Item2),
                DateTimeBehavior = DateTimeBehavior.DateOnly,
                Format = DateTimeFormat.DateOnly,
                RequiredLevel = new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.ApplicationRequired)
            });
        Attribute(PublicAbsenceTable, new IntegerAttributeMetadata {
            SchemaName = "cr2c9_hoursperday", DisplayName = L("Hours Per Day"),
            MinValue = 4, MaxValue = 8, Format = IntegerFormat.None,
            RequiredLevel = new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.ApplicationRequired)
        });
        var meta = Meta(PublicAbsenceTable);
        if (meta.OwnershipType != OwnershipTypes.OrganizationOwned ||
            meta.Attributes.Single(a => a.LogicalName == "cr2c9_startdate") is not DateTimeAttributeMetadata start ||
            start.DateTimeBehavior?.Value != DateTimeBehavior.DateOnly.Value ||
            meta.Attributes.Single(a => a.LogicalName == "cr2c9_enddate") is not DateTimeAttributeMetadata end ||
            end.DateTimeBehavior?.Value != DateTimeBehavior.DateOnly.Value ||
            meta.Attributes.Single(a => a.LogicalName == "cr2c9_hoursperday") is not IntegerAttributeMetadata)
            throw new Exception("Public absence schema verification failed.");
        Client.Execute(new PublishXmlRequest { ParameterXml =
            "<importexportxml><entities><entity>" + PublicAbsenceTable + "</entity></entities></importexportxml>" });
        Console.WriteLine("Public absence schema published. No private request data is exposed by this table.");
    }

    static void PublicAbsenceReadRolesDeploy()
    {
        var metadata = ((RetrieveEntityResponse)Client.Execute(new RetrieveEntityRequest {
            LogicalName = PublicAbsenceTable, EntityFilters = EntityFilters.Entity | EntityFilters.Privileges
        })).EntityMetadata;
        if (metadata.OwnershipType != OwnershipTypes.OrganizationOwned)
            throw new Exception("Public absence table must be organization-owned.");
        var read = metadata.Privileges.Single(p => p.PrivilegeType == PrivilegeType.Read).PrivilegeId;
        var forbidden = metadata.Privileges.Where(p => p.PrivilegeType == PrivilegeType.Create ||
            p.PrivilegeType == PrivilegeType.Write || p.PrivilegeType == PrivilegeType.Delete ||
            p.PrivilegeType == PrivilegeType.Assign || p.PrivilegeType == PrivilegeType.Share)
            .Select(p => p.PrivilegeId).ToHashSet();
        foreach (var roleName in new[] { "Leave Employee (development test)", "Leave HR", "Leave Superior" }) {
            var roles = Find("role", "name", roleName, "name");
            if (roles.Length == 0) throw new Exception("Missing leave role: " + roleName);
            foreach (var role in roles) {
                var before = ((RetrieveRolePrivilegesRoleResponse)Client.Execute(
                    new RetrieveRolePrivilegesRoleRequest { RoleId = role.Id })).RolePrivileges;
                if (before.Any(p => forbidden.Contains(p.PrivilegeId)))
                    throw new Exception(roleName + " already has public calendar write rights; inspect before proceeding.");
                if (!before.Any(p => p.PrivilegeId == read && p.Depth == PrivilegeDepth.Global))
                    Client.Execute(new AddPrivilegesRoleRequest { RoleId = role.Id,
                        Privileges = new[] { new RolePrivilege((int)PrivilegeDepth.Global, read) } });
                var after = ((RetrieveRolePrivilegesRoleResponse)Client.Execute(
                    new RetrieveRolePrivilegesRoleRequest { RoleId = role.Id })).RolePrivileges;
                if (!after.Any(p => p.PrivilegeId == read && p.Depth == PrivilegeDepth.Global) ||
                    after.Any(p => forbidden.Contains(p.PrivilegeId)))
                    throw new Exception("Public absence read-only role verification failed: " + roleName);
            }
            Console.WriteLine("Verified public absence read-only access for " + roleName);
        }
        // Basic User is a managed Microsoft role and cannot be customized.
        // Use a dedicated read-only role for ordinary employees instead.
        const string calendarRoleName = "Leave Calendar Reader";
        var users = new QueryExpression("systemuser") { ColumnSet = new ColumnSet("businessunitid", "azureactivedirectoryobjectid", "isdisabled") };
        users.Criteria.AddCondition("isdisabled", ConditionOperator.Equal, false);
        var enabledUsers = Client.RetrieveMultiple(users).Entities;
        var employees = new QueryExpression(Employee) { ColumnSet = new ColumnSet("cr2c9_entrauserid", "cr2c9_isactive") };
        employees.Criteria.AddCondition("cr2c9_isactive", ConditionOperator.Equal, true);
        var activeEntraIds = Client.RetrieveMultiple(employees).Entities
            .Select(e => Guid.TryParse(e.GetAttributeValue<string>("cr2c9_entrauserid"), out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty).ToHashSet();
        var linkedUsers = enabledUsers.Where(u => activeEntraIds.Contains(u.GetAttributeValue<Guid>("azureactivedirectoryobjectid"))).ToArray();
        var assigned = 0;
        foreach (var group in linkedUsers.GroupBy(u => u.GetAttributeValue<EntityReference>("businessunitid")?.Id ?? Guid.Empty)) {
            if (group.Key == Guid.Empty) throw new Exception("An employee user has no business unit.");
            var roleQuery = new QueryExpression("role") { ColumnSet = new ColumnSet("name", "businessunitid") };
            roleQuery.Criteria.AddCondition("name", ConditionOperator.Equal, calendarRoleName);
            roleQuery.Criteria.AddCondition("businessunitid", ConditionOperator.Equal, group.Key);
            var found = Client.RetrieveMultiple(roleQuery).Entities;
            if (found.Count > 1) throw new Exception("Duplicate calendar reader roles in one business unit.");
            var roleId = found.Count == 1 ? found[0].Id : Client.Create(new Entity("role") {
                ["name"] = calendarRoleName,
                ["businessunitid"] = new EntityReference("businessunit", group.Key)
            });
            var privileges = ((RetrieveRolePrivilegesRoleResponse)Client.Execute(
                new RetrieveRolePrivilegesRoleRequest { RoleId = roleId })).RolePrivileges;
            if (privileges.Any(p => forbidden.Contains(p.PrivilegeId)))
                throw new Exception("Calendar reader role has write rights; refusing assignment.");
            if (!privileges.Any(p => p.PrivilegeId == read && p.Depth == PrivilegeDepth.Global))
                Client.Execute(new AddPrivilegesRoleRequest { RoleId = roleId,
                    Privileges = new[] { new RolePrivilege((int)PrivilegeDepth.Global, read) } });
            foreach (var user in group) {
                var memberships = new QueryExpression("systemuserroles") { ColumnSet = new ColumnSet(false) };
                memberships.Criteria.AddCondition("systemuserid", ConditionOperator.Equal, user.Id);
                memberships.Criteria.AddCondition("roleid", ConditionOperator.Equal, roleId);
                if (Client.RetrieveMultiple(memberships).Entities.Count == 0)
                    Client.Associate("systemuser", user.Id, new Relationship("systemuserroles_association"),
                        new EntityReferenceCollection { new EntityReference("role", roleId) });
                assigned++;
            }
        }
        Console.WriteLine("Calendar reader role verified for " + assigned + " linked active employees. The active Employee sync plug-in maintains future membership.");
    }

    static void PublicAbsenceAudit()
    {
        var rows = Client.RetrieveMultiple(new QueryExpression(PublicAbsenceTable) {
            ColumnSet = new ColumnSet("cr2c9_name", "cr2c9_startdate", "cr2c9_enddate", "cr2c9_hoursperday")
        }).Entities;
        foreach (var row in rows) {
            var request = Client.Retrieve(Request, row.Id, new ColumnSet("cr2c9_status", "cr2c9_startdate", "cr2c9_enddate", "cr2c9_hoursperday"));
            if (request.GetAttributeValue<OptionSetValue>("cr2c9_status")?.Value != 1 ||
                request.GetAttributeValue<DateTime>("cr2c9_startdate") != row.GetAttributeValue<DateTime>("cr2c9_startdate") ||
                request.GetAttributeValue<DateTime>("cr2c9_enddate") != row.GetAttributeValue<DateTime>("cr2c9_enddate") ||
                Convert.ToInt32(request["cr2c9_hoursperday"]) != row.GetAttributeValue<int>("cr2c9_hoursperday"))
                throw new Exception("Public absence differs from the approved private request: " + row.Id);
        }
        Console.WriteLine("Audited " + rows.Count + " approved public absences; private request fields were not queried for calendar readers.");
    }
}
