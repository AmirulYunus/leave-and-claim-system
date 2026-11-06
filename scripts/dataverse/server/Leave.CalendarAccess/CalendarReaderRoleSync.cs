using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

namespace Leave.CalendarAccess
{
    /// <summary>
    /// Gives a verified, active employee's Dataverse account read-only access
    /// to the public absence projection. No private request privilege is added.
    /// </summary>
    public sealed class CalendarReaderRoleSync : IPlugin
    {
        const string Employee = "cr2c9_employee";
        const string PublicAbsence = "cr2c9_publicabsence";
        const string ReaderRole = "Leave Calendar Reader";

        public void Execute(IServiceProvider provider)
        {
            var context = (IPluginExecutionContext)provider.GetService(typeof(IPluginExecutionContext));
            if (context == null || context.Stage != 40 || context.Mode != 0 ||
                (context.MessageName != "Create" && context.MessageName != "Update") ||
                context.PrimaryEntityName != Employee)
                throw new InvalidPluginExecutionException("Calendar reader synchronization was registered incorrectly.");
            var factory = (IOrganizationServiceFactory)provider.GetService(typeof(IOrganizationServiceFactory));
            var service = factory.CreateOrganizationService(null);
            var trace = (ITracingService)provider.GetService(typeof(ITracingService));

            var row = service.Retrieve(Employee, context.PrimaryEntityId,
                new ColumnSet("cr2c9_entrauserid", "cr2c9_isactive", "statecode"));
            var currentId = Identity(row.GetAttributeValue<string>("cr2c9_entrauserid"));
            var active = row.GetAttributeValue<bool>("cr2c9_isactive") &&
                row.GetAttributeValue<OptionSetValue>("statecode")?.Value == 0;
            var oldId = context.PreEntityImages.Contains("BeforeCalendarIdentity")
                ? Identity(context.PreEntityImages["BeforeCalendarIdentity"]
                    .GetAttributeValue<string>("cr2c9_entrauserid")) : Guid.Empty;
            if (oldId != Guid.Empty && (oldId != currentId || !active))
                Reconcile(service, oldId, trace);
            if (currentId != Guid.Empty) Reconcile(service, currentId, trace);
        }

        static Guid Identity(string value)
        {
            return Guid.TryParse(value, out var id) ? id : Guid.Empty;
        }

        static void Reconcile(IOrganizationService service, Guid identity, ITracingService trace)
        {
            var users = new QueryExpression("systemuser") {
                ColumnSet = new ColumnSet("businessunitid", "isdisabled", "azureactivedirectoryobjectid"),
                TopCount = 2
            };
            users.Criteria.AddCondition("azureactivedirectoryobjectid", ConditionOperator.Equal, identity);
            var matches = service.RetrieveMultiple(users).Entities;
            if (matches.Count == 0) {
                trace?.Trace("Calendar reader: no Dataverse account for the verified Entra ID yet.");
                return;
            }
            if (matches.Count != 1)
                throw new InvalidPluginExecutionException("Calendar reader: duplicate Dataverse identities.");
            var user = matches[0];
            var businessUnit = user.GetAttributeValue<EntityReference>("businessunitid")?.Id ?? Guid.Empty;
            if (businessUnit == Guid.Empty)
                throw new InvalidPluginExecutionException("Calendar reader: linked account has no business unit.");

            var employees = new QueryExpression(Employee) {
                ColumnSet = new ColumnSet("cr2c9_isactive", "statecode"), TopCount = 2
            };
            employees.Criteria.AddCondition("cr2c9_entrauserid", ConditionOperator.Equal, identity.ToString("D"));
            var linked = service.RetrieveMultiple(employees).Entities;
            if (linked.Count > 1)
                throw new InvalidPluginExecutionException("Calendar reader: duplicate Employee rows share one Entra identity.");
            var eligible = !user.GetAttributeValue<bool>("isdisabled") && linked.Count == 1 &&
                linked[0].GetAttributeValue<bool>("cr2c9_isactive") &&
                linked[0].GetAttributeValue<OptionSetValue>("statecode")?.Value == 0;

            var roleQuery = new QueryExpression("role") {
                ColumnSet = new ColumnSet("name", "businessunitid"), TopCount = 2
            };
            roleQuery.Criteria.AddCondition("name", ConditionOperator.Equal, ReaderRole);
            roleQuery.Criteria.AddCondition("businessunitid", ConditionOperator.Equal, businessUnit);
            var roles = service.RetrieveMultiple(roleQuery).Entities;
            if (roles.Count > 1)
                throw new InvalidPluginExecutionException("Calendar reader: duplicate roles in one business unit.");
            if (!eligible && roles.Count == 0) return;
            var roleId = roles.Count == 1 ? roles[0].Id : CreateRole(service, businessUnit);
            VerifyReadOnly(service, roleId);

            var membership = new QueryExpression("systemuserroles") {
                ColumnSet = new ColumnSet(false), TopCount = 1
            };
            membership.Criteria.AddCondition("systemuserid", ConditionOperator.Equal, user.Id);
            membership.Criteria.AddCondition("roleid", ConditionOperator.Equal, roleId);
            var assigned = service.RetrieveMultiple(membership).Entities.Count == 1;
            if (eligible && !assigned) {
                service.Associate("systemuser", user.Id, new Relationship("systemuserroles_association"),
                    new EntityReferenceCollection { new EntityReference("role", roleId) });
                trace?.Trace("Calendar reader role assigned to a verified active employee.");
            } else if (!eligible && assigned) {
                service.Disassociate("systemuser", user.Id, new Relationship("systemuserroles_association"),
                    new EntityReferenceCollection { new EntityReference("role", roleId) });
                trace?.Trace("Calendar reader role removed from an inactive or unlinked account.");
            }
        }

        static Guid CreateRole(IOrganizationService service, Guid businessUnit)
        {
            return service.Create(new Entity("role") {
                ["name"] = ReaderRole,
                ["businessunitid"] = new EntityReference("businessunit", businessUnit)
            });
        }

        static void VerifyReadOnly(IOrganizationService service, Guid roleId)
        {
            var metadata = ((RetrieveEntityResponse)service.Execute(new RetrieveEntityRequest {
                LogicalName = PublicAbsence,
                EntityFilters = EntityFilters.Entity | EntityFilters.Privileges
            })).EntityMetadata;
            if (metadata.OwnershipType != OwnershipTypes.OrganizationOwned)
                throw new InvalidPluginExecutionException("Calendar projection is not organization-owned.");
            var read = metadata.Privileges.Single(p => p.PrivilegeType == PrivilegeType.Read).PrivilegeId;
            var forbidden = metadata.Privileges.Where(p =>
                p.PrivilegeType == PrivilegeType.Create || p.PrivilegeType == PrivilegeType.Write ||
                p.PrivilegeType == PrivilegeType.Delete || p.PrivilegeType == PrivilegeType.Assign ||
                p.PrivilegeType == PrivilegeType.Share).Select(p => p.PrivilegeId).ToArray();
            var privileges = ((RetrieveRolePrivilegesRoleResponse)service.Execute(
                new RetrieveRolePrivilegesRoleRequest { RoleId = roleId })).RolePrivileges;
            if (privileges.Any(p => forbidden.Contains(p.PrivilegeId)))
                throw new InvalidPluginExecutionException("Calendar reader role has unexpected write privileges.");
            var initialDefaults = new HashSet<string> {
                "prvReadSdkMessageProcessingStepImage", "prvCreateSharePointData",
                "prvReadPluginType", "prvReadSdkMessage", "prvWriteSharePointData",
                "prvReadSharePointDocument", "prvReadSdkMessageProcessingStep",
                "prvReadPluginAssembly", "prvReadSharePointData"
            };
            var platformDefaults = new HashSet<string> {
                "prvCreateSharePointData", "prvWriteSharePointData",
                "prvReadSharePointDocument", "prvReadSharePointData"
            };
            string PrivilegeName(Guid id) => service.Retrieve("privilege", id,
                new ColumnSet("name")).GetAttributeValue<string>("name");
            var nonRead = privileges.Where(p => p.PrivilegeId != read).ToArray();
            var names = nonRead.Select(p => PrivilegeName(p.PrivilegeId)).ToArray();
            foreach (var name in names) {
                if (!initialDefaults.Contains(name))
                    throw new InvalidPluginExecutionException(
                        "Calendar reader role has an unexpected privilege: " + name);
            }
            if (names.Any(n => !platformDefaults.Contains(n)) ||
                !privileges.Any(p => p.PrivilegeId == read && p.Depth == PrivilegeDepth.Global)) {
                service.Execute(new ReplacePrivilegesRoleRequest {
                    RoleId = roleId,
                    Privileges = new[] { new RolePrivilege((int)PrivilegeDepth.Global, read) }
                });
                var after = ((RetrieveRolePrivilegesRoleResponse)service.Execute(
                    new RetrieveRolePrivilegesRoleRequest { RoleId = roleId })).RolePrivileges;
                if (!after.Any(p => p.PrivilegeId == read && p.Depth == PrivilegeDepth.Global) ||
                    after.Where(p => p.PrivilegeId != read)
                        .Any(p => !platformDefaults.Contains(PrivilegeName(p.PrivilegeId))))
                    throw new InvalidPluginExecutionException(
                        "Calendar reader role exceeds the platform defaults and public absence Read.");
            }
        }
    }
}
