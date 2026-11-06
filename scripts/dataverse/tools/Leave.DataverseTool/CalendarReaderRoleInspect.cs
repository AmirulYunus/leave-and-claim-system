using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

internal static partial class Development
{
    static void CalendarReaderRoleInspect()
    {
        foreach (var role in Find("role", "name", "Leave Calendar Reader", "name", "businessunitid")) {
            Console.WriteLine("Role " + role.Id + " BU=" + role.GetAttributeValue<Microsoft.Xrm.Sdk.EntityReference>("businessunitid")?.Id);
            var granted = ((RetrieveRolePrivilegesRoleResponse)Client.Execute(
                new RetrieveRolePrivilegesRoleRequest { RoleId = role.Id })).RolePrivileges;
            foreach (var item in granted.OrderBy(p => p.PrivilegeId)) {
                var privilege = Client.Retrieve("privilege", item.PrivilegeId, new ColumnSet("name"));
                Console.WriteLine("  " + privilege.GetAttributeValue<string>("name") + " depth=" + item.Depth);
            }
        }
    }

}
