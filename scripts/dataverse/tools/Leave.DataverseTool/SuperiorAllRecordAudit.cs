using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

internal static partial class Development
{
    static void SuperiorAllRecordAudit()
    {
        static Entity[] All(string table, params string[] columns)
        {
            var q = new QueryExpression(table) {
                ColumnSet = new ColumnSet(columns),
                PageInfo = new PagingInfo { Count = 500, PageNumber = 1 }
            };
            var rows = new List<Entity>();
            while (true) {
                var page = Client.RetrieveMultiple(q);
                rows.AddRange(page.Entities);
                if (!page.MoreRecords) return rows.ToArray();
                q.PageInfo.PageNumber++;
                q.PageInfo.PagingCookie = page.PagingCookie;
            }
        }
        var employees = All(Employee, "cr2c9_employeename", "cr2c9_superior",
            "cr2c9_entrauserid", "cr2c9_isactive").ToDictionary(x => x.Id);
        var requests = All(Request, "cr2c9_employeename", "cr2c9_leaverequestnumber", "cr2c9_status", "ownerid")
            .ToDictionary(x => x.Id);
        var tables = new[] {
            (Name: Request, Link: "cr2c9_leaverequestid", Grant: Request,
                Rights: AccessRights.ReadAccess | AccessRights.WriteAccess),
            (Name: "cr2c9_leaverequestdate", Link: "cr2c9_leaverequest", Grant: "cr2c9_leaverequestdate",
                Rights: AccessRights.ReadAccess),
            (Name: History, Link: "cr2c9_leaverequest", Grant: History,
                Rights: AccessRights.ReadAccess),
            (Name: "cr2c9_leavecancellation", Link: "cr2c9_leaverequest", Grant: "cr2c9_leavecancellation",
                Rights: AccessRights.ReadAccess | AccessRights.WriteAccess)
        };
        var grants = All("cr2c9_superioraccessgrant", "cr2c9_grantee", Request,
            "cr2c9_leaverequestdate", History, "cr2c9_leavecancellation");
        var managerUsers = new Dictionary<Guid, Guid>();
        var issues = new List<string>();
        var issueCounts = new Dictionary<string, int>();
        void Issue(string kind, string detail)
        {
            issues.Add(kind + " " + detail);
            issueCounts[kind] = issueCounts.GetValueOrDefault(kind) + 1;
        }
        foreach (var employee in employees.Values) {
            var manager = employee.GetAttributeValue<EntityReference>("cr2c9_superior");
            if (manager == null) continue;
            if (!employees.TryGetValue(manager.Id, out var superior)) {
                Issue("missing-manager-record", "Employee " + employee.Id + " points to " + manager.Id);
                continue;
            }
            if (!superior.GetAttributeValue<bool>("cr2c9_isactive") ||
                !Guid.TryParse(superior.GetAttributeValue<string>("cr2c9_entrauserid"), out var entra)) {
                Issue("unresolvable-manager", "Employee " + employee.Id + " Superior " + manager.Id);
                continue;
            }
            if (!managerUsers.ContainsKey(manager.Id)) {
                var user = Find("systemuser", "azureactivedirectoryobjectid", entra, "isdisabled");
                if (user.Length != 1 || user[0].GetAttributeValue<bool>("isdisabled"))
                    Issue("missing-manager-account", "Employee " +
                        employee.GetAttributeValue<string>("cr2c9_employeename") + " (" + employee.Id +
                        ") has Superior " + superior.GetAttributeValue<string>("cr2c9_employeename") +
                        " (" + manager.Id + ") with " + user.Length + " matching Dataverse account(s)");
                else managerUsers[manager.Id] = user[0].Id;
            }
        }
        var checkedRows = 0;
        var checkedShares = 0;
        foreach (var table in tables) {
            var rows = table.Name == Request ? requests.Values.ToArray() :
                All(table.Name, table.Link, "ownerid");
            foreach (var row in rows) {
                checkedRows++;
                var requestId = table.Name == Request ? row.Id :
                    row.GetAttributeValue<EntityReference>(table.Link)?.Id ?? Guid.Empty;
                if (!requests.TryGetValue(requestId, out var request)) {
                    Issue("orphan-child", table.Name + " " + row.Id + " has no request");
                    continue;
                }
                var employeeId = request.GetAttributeValue<EntityReference>("cr2c9_employeename")?.Id ?? Guid.Empty;
                if (!employees.TryGetValue(employeeId, out var employee)) {
                    Issue("orphan-request", "Request " + request.Id + " has no Employee");
                    continue;
                }
                var managerId = employee.GetAttributeValue<EntityReference>("cr2c9_superior")?.Id ?? Guid.Empty;
                var expected = managerUsers.GetValueOrDefault(managerId);
                var marked = grants.Where(g => g.GetAttributeValue<EntityReference>(table.Grant)?.Id == row.Id).ToArray();
                var label = table.Name + " " + (request.GetAttributeValue<string>("cr2c9_leaverequestnumber") ?? request.Id.ToString()) +
                    " status=" + request.GetAttributeValue<OptionSetValue>("cr2c9_status")?.Value +
                    " employee=" + employee.GetAttributeValue<string>("cr2c9_employeename") +
                    " record=" + row.Id;
                if (marked.Length != (expected == Guid.Empty ? 0 : 1)) {
                    Issue("grant-count", label + " has " + marked.Length +
                        " grant markers; expected " + (expected == Guid.Empty ? 0 : 1));
                    continue;
                }
                if (expected == Guid.Empty) continue;
                var actual = marked[0].GetAttributeValue<EntityReference>("cr2c9_grantee")?.Id ?? Guid.Empty;
                if (actual != expected) {
                    Issue("wrong-grantee", label + " grant targets " + actual + " instead of " + expected);
                    continue;
                }
                var owner = row.GetAttributeValue<EntityReference>("ownerid");
                if (owner?.LogicalName == "systemuser" && owner.Id == actual)
                    Issue("superior-owner", label + " is owned by its Superior grantee");
                var rights = (RetrieveSharedPrincipalsAndAccessResponse)Client.Execute(
                    new RetrieveSharedPrincipalsAndAccessRequest { Target = row.ToEntityReference() });
                var direct = rights.PrincipalAccesses.Where(p => p.Principal.LogicalName == "systemuser" &&
                    p.Principal.Id == actual).ToArray();
                if (direct.Length != 1 || direct[0].AccessMask != table.Rights)
                    Issue("wrong-share", label + " has missing or altered direct Superior access");
                checkedShares++;
            }
            Console.WriteLine("AUDIT " + table.Name + " rows=" + rows.Length);
        }
        Console.WriteLine("AUDIT employees=" + employees.Count + " requests=" + requests.Count +
            " markers=" + grants.Length + " checkedRows=" + checkedRows +
            " checkedShares=" + checkedShares + " issues=" + issues.Count);
        foreach (var count in issueCounts.OrderBy(x => x.Key))
            Console.WriteLine("COUNT " + count.Key + "=" + count.Value);
        foreach (var issue in issues.Take(120)) Console.WriteLine("ISSUE " + issue);
        if (issues.Count > 120) Console.WriteLine("... " + (issues.Count - 120) + " additional issues");
    }
}
