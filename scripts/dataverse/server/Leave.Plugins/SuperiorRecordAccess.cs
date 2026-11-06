using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Leave.Plugins
{
    // A private grant row records only shares created by this plug-in.
    public sealed class SuperiorRecordAccess : IPlugin
    {
        const string Employee = "cr2c9_employee", Request = "cr2c9_leaverequest";
        const string Cancellation = "cr2c9_leavecancellation";
        const string Date = "cr2c9_leaverequestdate", Decision = "cr2c9_leavedecision";
        const string Grant = "cr2c9_superioraccessgrant";
        static readonly AccessRights Review = AccessRights.ReadAccess | AccessRights.WriteAccess;
        static readonly AccessRights Evidence = AccessRights.ReadAccess;

        static IEnumerable<Entity> Rows(IOrganizationService service, string table, string field, Guid id)
        {
            var query = new QueryExpression(table) { ColumnSet = new ColumnSet(false),
                PageInfo = new PagingInfo { Count = 500, PageNumber = 1 } };
            query.Criteria.AddCondition(field, ConditionOperator.Equal, id);
            while (true) {
                var page = service.RetrieveMultiple(query);
                foreach (var row in page.Entities) yield return row;
                if (!page.MoreRecords) yield break;
                query.PageInfo.PageNumber++;
                query.PageInfo.PagingCookie = page.PagingCookie;
            }
        }

        static Guid ManagerUser(IOrganizationService service, EntityReference manager, bool requireActive)
        {
            if (manager == null) return Guid.Empty;
            if (manager.LogicalName != Employee || manager.Id == Guid.Empty)
                throw new InvalidPluginExecutionException("The synced Superior lookup is invalid.");
            var employee = service.Retrieve(Employee, manager.Id,
                new ColumnSet("cr2c9_entrauserid", "cr2c9_isactive"));
            if (requireActive && !employee.GetAttributeValue<bool>("cr2c9_isactive"))
                throw new InvalidPluginExecutionException("The synced Superior is inactive; the previous assignment was preserved.");
            Guid identity;
            if (!Guid.TryParse(employee.GetAttributeValue<string>("cr2c9_entrauserid"), out identity))
                throw new InvalidPluginExecutionException("The synced Superior has no valid Entra identity; the previous assignment was preserved.");
            var users = new QueryExpression("systemuser") {
                ColumnSet = new ColumnSet("isdisabled"), TopCount = 2
            };
            users.Criteria.AddCondition("azureactivedirectoryobjectid", ConditionOperator.Equal, identity);
            var matches = service.RetrieveMultiple(users).Entities;
            if (matches.Count != 1 || (requireActive && matches[0].GetAttributeValue<bool>("isdisabled")))
                throw new InvalidPluginExecutionException("The synced Superior has no unique active Dataverse account; the previous assignment was preserved.");
            return matches[0].Id;
        }

        static AccessRights? DirectRights(IOrganizationService service, EntityReference record, Guid user)
        {
            var result = (RetrieveSharedPrincipalsAndAccessResponse)service.Execute(
                new RetrieveSharedPrincipalsAndAccessRequest { Target = record });
            var matches = result.PrincipalAccesses.Where(x => x.Principal.LogicalName == "systemuser" &&
                x.Principal.Id == user).ToArray();
            if (matches.Length > 1) throw new InvalidPluginExecutionException("Duplicate direct Superior shares.");
            return matches.Length == 1 ? matches[0].AccessMask : (AccessRights?)null;
        }

        static string GrantLookup(string table)
        {
            if (table == Request) return "cr2c9_leaverequest";
            if (table == Cancellation) return "cr2c9_leavecancellation";
            if (table == Date) return "cr2c9_leaverequestdate";
            if (table == Decision) return "cr2c9_leavedecision";
            throw new InvalidPluginExecutionException("Unsupported Superior grant target.");
        }

        static Entity ExistingGrant(IOrganizationService service, EntityReference record)
        {
            var grants = Rows(service, Grant, GrantLookup(record.LogicalName), record.Id).ToArray();
            if (grants.Length > 1)
                throw new InvalidPluginExecutionException("Duplicate Superior grant provenance; review before changing access.");
            return grants.Length == 0 ? null : service.Retrieve(Grant, grants[0].Id,
                new ColumnSet("cr2c9_grantee"));
        }

        static void Change(IOrganizationService service, EntityReference record, Guid newUser,
            AccessRights rights)
        {
            var grant = ExistingGrant(service, record);
            var oldRef = grant?.GetAttributeValue<EntityReference>("cr2c9_grantee");
            if (grant != null && (oldRef == null || oldRef.LogicalName != "systemuser" || oldRef.Id == Guid.Empty))
                throw new InvalidPluginExecutionException("Superior grant provenance has an invalid user.");
            var oldUser = oldRef?.Id ?? Guid.Empty;
            if (oldUser == newUser && oldUser == Guid.Empty) return;
            if (record.LogicalName == Decision && oldUser != Guid.Empty && oldUser != newUser) {
                var decision = service.Retrieve(Decision, record.Id,
                    new ColumnSet("ownerid", "cr2c9_leaverequest"));
                var owner = decision.GetAttributeValue<EntityReference>("ownerid");
                if (owner?.LogicalName == "systemuser" && owner.Id == oldUser) {
                    var request = decision.GetAttributeValue<EntityReference>("cr2c9_leaverequest");
                    if (request?.LogicalName != Request || request.Id == Guid.Empty)
                        throw new InvalidPluginExecutionException("Decision history has no verified request.");
                    var requestOwner = service.Retrieve(Request, request.Id,
                        new ColumnSet("ownerid")).GetAttributeValue<EntityReference>("ownerid");
                    if (requestOwner == null || requestOwner.Id == Guid.Empty ||
                        (requestOwner.LogicalName == "systemuser" &&
                         (requestOwner.Id == oldUser || requestOwner.Id == newUser)))
                        throw new InvalidPluginExecutionException("Decision history needs an independent owner before reassignment.");
                    service.Update(new Entity(Decision, record.Id) { ["ownerid"] = requestOwner });
                }
            }
            if (oldUser != Guid.Empty) {
                var previous = DirectRights(service, record, oldUser);
                if (previous != rights)
                    throw new InvalidPluginExecutionException("A recorded Superior share was changed outside the assignment process.");
                if (oldUser == newUser) return;
            }
            if (newUser != Guid.Empty) {
                var current = DirectRights(service, record, newUser);
                if (current.HasValue)
                    throw new InvalidPluginExecutionException("A manual share already exists for the new Superior; resolve it before assignment.");
            }
            if (oldUser != Guid.Empty) {
                service.Execute(new RevokeAccessRequest { Target = record,
                    Revokee = new EntityReference("systemuser", oldUser) });
                service.Delete(Grant, grant.Id);
            }
            if (newUser != Guid.Empty) {
                service.Execute(new GrantAccessRequest { Target = record,
                    PrincipalAccess = new PrincipalAccess {
                        Principal = new EntityReference("systemuser", newUser), AccessMask = rights
                    } });
                var marker = new Entity(Grant);
                marker["cr2c9_name"] = record.LogicalName + ":" + record.Id.ToString("D");
                marker[GrantLookup(record.LogicalName)] = record;
                marker["cr2c9_grantee"] = new EntityReference("systemuser", newUser);
                var markerId = service.Create(marker);
                service.Execute(new GrantAccessRequest {
                    Target = new EntityReference(Grant, markerId),
                    PrincipalAccess = new PrincipalAccess {
                        Principal = new EntityReference("systemuser", newUser),
                        AccessMask = Evidence
                    }
                });
            }
        }

        static EntityReference Superior(IOrganizationService service, Guid employeeId)
        {
            return service.Retrieve(Employee, employeeId, new ColumnSet("cr2c9_superior"))
                .GetAttributeValue<EntityReference>("cr2c9_superior");
        }

        static Guid EmployeeForRequest(IOrganizationService service, Guid requestId)
        {
            var person = service.Retrieve(Request, requestId, new ColumnSet("cr2c9_employeename"))
                .GetAttributeValue<EntityReference>("cr2c9_employeename");
            if (person == null || person.LogicalName != Employee || person.Id == Guid.Empty)
                throw new InvalidPluginExecutionException("The request's Employee lookup is missing.");
            return person.Id;
        }

        static void RequestTree(IOrganizationService service, Guid requestId, Guid newUser)
        {
            Change(service, new EntityReference(Request, requestId), newUser, Review);
            foreach (var row in Rows(service, Date, "cr2c9_leaverequest", requestId))
                Change(service, row.ToEntityReference(), newUser, Evidence);
            foreach (var row in Rows(service, Decision, "cr2c9_leaverequest", requestId))
                Change(service, row.ToEntityReference(), newUser, Evidence);
            // The Request -> annotation relationship cascades Share and Unshare
            // in this environment. Avoid a second direct note share that could
            // survive a later manager change.
            foreach (var row in Rows(service, Cancellation, "cr2c9_leaverequest", requestId))
                Change(service, row.ToEntityReference(), newUser, Review);
        }

        public void Execute(IServiceProvider provider)
        {
            var context = (IPluginExecutionContext)provider.GetService(typeof(IPluginExecutionContext));
            if (context.Mode != 0 || !context.IsInTransaction)
                throw new InvalidPluginExecutionException("Superior sharing must run synchronously in the transaction.");
            var service = ((IOrganizationServiceFactory)provider.GetService(typeof(IOrganizationServiceFactory)))
                .CreateOrganizationService(null);
            if (context.PrimaryEntityName == Date && context.MessageName == "Delete" && context.Stage == 20) {
                var target = context.InputParameters["Target"] as EntityReference;
                if (target == null || target.LogicalName != Date)
                    throw new InvalidPluginExecutionException("Invalid leave-date deletion target.");
                Change(service, target, Guid.Empty, Evidence);
                return;
            }
            if (context.Stage != 40)
                throw new InvalidPluginExecutionException("Superior sharing must run after save except for date cleanup.");
            if (context.PrimaryEntityName == Employee && context.MessageName == "Update") {
                var target = (Entity)context.InputParameters["Target"];
                if (!target.Contains("cr2c9_superior")) return;
                var newManager = Superior(service, context.PrimaryEntityId);
                var newUser = ManagerUser(service, newManager, true);
                foreach (var request in Rows(service, Request, "cr2c9_employeename", context.PrimaryEntityId))
                    RequestTree(service, request.Id, newUser);
                return;
            }
            if (context.PrimaryEntityName == Request && context.MessageName == "Create") {
                var requestId = context.PrimaryEntityId;
                var user = ManagerUser(service, Superior(service, EmployeeForRequest(service, requestId)), true);
                RequestTree(service, requestId, user);
                return;
            }
            if (context.PrimaryEntityName == Cancellation && context.MessageName == "Create") {
                var original = service.Retrieve(Cancellation, context.PrimaryEntityId,
                    new ColumnSet("cr2c9_leaverequest")).GetAttributeValue<EntityReference>("cr2c9_leaverequest");
                if (original == null || original.LogicalName != Request)
                    throw new InvalidPluginExecutionException("Cancellation request lookup is missing.");
                var user = ManagerUser(service, Superior(service, EmployeeForRequest(service, original.Id)), true);
                Change(service, new EntityReference(Cancellation, context.PrimaryEntityId), user, Review);
                return;
            }
            if (context.MessageName == "Create" &&
                (context.PrimaryEntityName == Date || context.PrimaryEntityName == Decision)) {
                var table = context.PrimaryEntityName;
                var field = "cr2c9_leaverequest";
                var parent = service.Retrieve(table, context.PrimaryEntityId, new ColumnSet(field))
                    .GetAttributeValue<EntityReference>(field);
                if (parent?.LogicalName != Request) return;
                var user = ManagerUser(service, Superior(service, EmployeeForRequest(service, parent.Id)), true);
                Change(service, new EntityReference(table, context.PrimaryEntityId), user, Evidence);
                return;
            }
            throw new InvalidPluginExecutionException("Unexpected Superior sharing step registration.");
        }
    }
}
