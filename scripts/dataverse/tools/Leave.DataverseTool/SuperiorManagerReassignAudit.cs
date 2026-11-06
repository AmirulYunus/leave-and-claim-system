using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

internal static partial class Development
{
    static void SuperiorManagerReassignAudit()
    {
        var employeeId = Guid.Parse("adae5971-d3c2-f111-a05a-000d3a826b69");
        var employee = Client.Retrieve(Employee, employeeId,
            new ColumnSet("cr2c9_superior", "ownerid", "cr2c9_employeename"));
        Console.WriteLine($"EMPLOYEE {employee.Id} superior={employee.GetAttributeValue<EntityReference>("cr2c9_superior")?.Id} owner={employee.GetAttributeValue<EntityReference>("ownerid")?.Id}");

        void Inspect(string table, string parent, Guid parentId, string? number = null)
        {
            var q = new QueryExpression(table) {
                ColumnSet = new ColumnSet("ownerid"), PageInfo = new PagingInfo { Count = 500, PageNumber = 1 }
            };
            q.Criteria.AddCondition(parent, ConditionOperator.Equal, parentId);
            while (true) {
                var page = Client.RetrieveMultiple(q);
                foreach (var row in page.Entities) {
                    var grantColumn = table switch {
                        "cr2c9_leaverequest" => "cr2c9_leaverequest",
                        "cr2c9_leaverequestdate" => "cr2c9_leaverequestdate",
                        "cr2c9_leavedecision" => "cr2c9_leavedecision",
                        "cr2c9_leavecancellation" => "cr2c9_leavecancellation",
                        _ => throw new Exception("Unsupported audit table")
                    };
                    var grants = Find("cr2c9_superioraccessgrant", grantColumn, row.Id, "cr2c9_grantee");
                    var owner = row.GetAttributeValue<EntityReference>("ownerid");
                    var grantee = grants.Length == 1 ? grants[0].GetAttributeValue<EntityReference>("cr2c9_grantee") : null;
                    Console.WriteLine($"ROW {table} {number} {row.Id} owner={owner?.LogicalName}:{owner?.Id} grantCount={grants.Length} grantee={grantee?.Id} granteeIsOwner={owner?.Id == grantee?.Id && grantee != null}");
                }
                if (!page.MoreRecords) break;
                q.PageInfo.PageNumber++;
                q.PageInfo.PagingCookie = page.PagingCookie;
            }
        }

        var requests = new QueryExpression(Request) {
            ColumnSet = new ColumnSet("cr2c9_leaverequestnumber"),
            PageInfo = new PagingInfo { Count = 500, PageNumber = 1 }
        };
        requests.Criteria.AddCondition("cr2c9_employeename", ConditionOperator.Equal, employeeId);
        while (true) {
            var page = Client.RetrieveMultiple(requests);
            foreach (var request in page.Entities) {
                var number = request.GetAttributeValue<string>("cr2c9_leaverequestnumber");
                Inspect(Request, "cr2c9_leaverequestid", request.Id, number);
                Inspect("cr2c9_leaverequestdate", "cr2c9_leaverequest", request.Id, number);
                Inspect("cr2c9_leavedecision", "cr2c9_leaverequest", request.Id, number);
                Inspect("cr2c9_leavecancellation", "cr2c9_leaverequest", request.Id, number);
            }
            if (!page.MoreRecords) break;
            requests.PageInfo.PageNumber++;
            requests.PageInfo.PagingCookie = page.PagingCookie;
        }
    }
}
