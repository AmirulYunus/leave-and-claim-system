using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Crm.Sdk.Messages;

internal static partial class Development
{
    // Read-only diagnostic. Do not fetch or print attachment bodies.
    static void EmployeeRequestAudit()
    {
        var joined = (DateTimeAttributeMetadata)Meta(Employee).Attributes.Single(a => a.LogicalName == "cr2c9_datejoined");
        Console.WriteLine($"Date Joined metadata: behavior={joined.DateTimeBehavior?.Value}, format={joined.Format}, min={DateTimeAttributeMetadata.MinSupportedValue}, max={DateTimeAttributeMetadata.MaxSupportedValue}");
        var employees = Client.RetrieveMultiple(new QueryExpression(Employee) {
            ColumnSet = new ColumnSet("cr2c9_employeename", "cr2c9_email", "cr2c9_datejoined"),
            Criteria = new FilterExpression(LogicalOperator.And) {
                Conditions = { new ConditionExpression("cr2c9_email", ConditionOperator.Equal, "Darwisyah.Fakhira@Asteriskdigital.com.my") }
            }
        }).Entities;
        if (employees.Count != 1) throw new Exception("Expected exactly one Darwisyah profile; found " + employees.Count);
        var employee = employees.Single();
        Console.WriteLine("Employee: " + employee.GetAttributeValue<string>("cr2c9_employeename") + "; joined=" + employee.GetAttributeValue<DateTime?>("cr2c9_datejoined")?.ToString("yyyy-MM-dd"));
        var requests = Client.RetrieveMultiple(new QueryExpression(Request) {
            ColumnSet = new ColumnSet("cr2c9_leaverequestnumber", "cr2c9_startdate", "cr2c9_status", "cr2c9_attachment", "cr2c9_supportingdocument"),
            Criteria = new FilterExpression(LogicalOperator.And) {
                Conditions = { new ConditionExpression("cr2c9_employeename", ConditionOperator.Equal, employee.Id) }
            },
            Orders = { new OrderExpression("createdon", OrderType.Descending) },
            TopCount = 20
        }).Entities;
        foreach (var request in requests) {
            var notes = Client.RetrieveMultiple(new QueryExpression("annotation") {
                ColumnSet = new ColumnSet("filename", "filesize", "isdocument", "createdon", "objectid"),
                Criteria = new FilterExpression(LogicalOperator.And) {
                    Conditions = { new ConditionExpression("objectid", ConditionOperator.Equal, request.Id) }
                }
            }).Entities;
            Console.WriteLine($"Request {request.GetAttributeValue<string>("cr2c9_leaverequestnumber") ?? request.Id.ToString()}: notes={notes.Count}; legacyAttachment={(request.GetAttributeValue<string>("cr2c9_attachment") is string s && s.Length > 0 ? "present" : "empty")}; fileColumn={(request.Contains("cr2c9_supportingdocument") ? "present" : "empty")}");
            foreach (var note in notes)
                Console.WriteLine($"  Note {note.Id}: filename={note.GetAttributeValue<string>("filename")}, size={note.GetAttributeValue<int>("filesize")}, document={note.GetAttributeValue<bool>("isdocument")}");
        }
    }

    static void EmployeeNoteAccessFix()
    {
        const string email = "Darwisyah.Fakhira@Asteriskdigital.com.my";
        var user = Find("systemuser", "internalemailaddress", email, "businessunitid", "isdisabled").Single();
        if (user.GetAttributeValue<bool>("isdisabled")) throw new Exception("Employee account is disabled.");
        var unit = user.GetAttributeValue<EntityReference>("businessunitid")?.Id ?? Guid.Empty;
        var roleQuery = new QueryExpression("role") { ColumnSet = new ColumnSet("name", "businessunitid") };
        roleQuery.Criteria.AddCondition("name", ConditionOperator.Equal, "Leave Employee (development test)");
        roleQuery.Criteria.AddCondition("businessunitid", ConditionOperator.Equal, unit);
        var role = Client.RetrieveMultiple(roleQuery).Entities.Single();
        var assignedRoles = UserRoleNames(user.Id);
        if (!assignedRoles.Contains("Leave Employee (development test)"))
            throw new Exception("Employee test role is not assigned.");
        Console.WriteLine("Current user roles: " + string.Join(", ", assignedRoles));
        var noteMeta = ((RetrieveEntityResponse)Client.Execute(new RetrieveEntityRequest {
            LogicalName = "annotation", EntityFilters = EntityFilters.Entity | EntityFilters.Privileges
        })).EntityMetadata;
        var wanted = new[] { PrivilegeType.Create, PrivilegeType.Read, PrivilegeType.Append, PrivilegeType.AppendTo }
            .Select(type => new RolePrivilege((int)PrivilegeDepth.Basic,
                noteMeta.Privileges.Single(p => p.PrivilegeType == type).PrivilegeId)).ToArray();
        var current = ((RetrieveRolePrivilegesRoleResponse)Client.Execute(new RetrieveRolePrivilegesRoleRequest { RoleId = role.Id })).RolePrivileges;
        var missing = wanted.Where(p => !current.Any(c => c.PrivilegeId == p.PrivilegeId && c.Depth >= p.Depth)).ToArray();
        if (missing.Length != 0)
            Client.Execute(new AddPrivilegesRoleRequest { RoleId = role.Id, Privileges = missing });
        var verified = ((RetrieveRolePrivilegesRoleResponse)Client.Execute(new RetrieveRolePrivilegesRoleRequest { RoleId = role.Id })).RolePrivileges;
        if (wanted.Any(p => !verified.Any(c => c.PrivilegeId == p.PrivilegeId && c.Depth >= p.Depth)))
            throw new Exception("Note privileges did not verify.");

        Console.WriteLine("Verified Basic-depth Note Create/Read/Append/Append To on the development Employee role. No file was uploaded. Test the Canvas save path using an Employee-only account before production.");
    }
}
