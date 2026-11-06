using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Leave.Plugins
{
    internal static class DecisionAuthority
    {
        internal const string HrRole = "Leave HR";

        internal static bool IsHr(IOrganizationService service, Guid userId)
        {
            if (userId == Guid.Empty) throw Guard.Error("A signed-in reviewer is required.");
            // HR notification email addresses do not grant approval rights.
            // Only a direct, administrator-assigned Dataverse role does.
            var memberships = new QueryExpression("systemuserroles")
                { ColumnSet = new ColumnSet("roleid"), TopCount = 5000 };
            memberships.Criteria.AddCondition("systemuserid", ConditionOperator.Equal, userId);
            var rows = service.RetrieveMultiple(memberships);
            if (rows.MoreRecords) throw Guard.Error("HR role membership could not be fully verified.");
            foreach (var row in rows.Entities)
            {
                var roleId = row.GetAttributeValue<Guid>("roleid");
                if (roleId == Guid.Empty) throw Guard.Error("HR role membership is invalid.");
                var role = service.Retrieve("role", roleId, new ColumnSet("name"));
                if (role.GetAttributeValue<string>("name") == HrRole) return true;
            }
            return false;
        }
    }
}
