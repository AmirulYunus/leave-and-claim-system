using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Leave.Plugins
{
    internal static class EmergencyApprovalRules
    {
        internal const string TypeName = "Emergency Leave";

        internal static bool IsEmergency(IOrganizationService service, Entity request,
            bool strict = false, bool requireActive = true)
        {
            var reference = request.GetAttributeValue<EntityReference>("cr2c9_leavetypename");
            if (reference == null || reference.LogicalName != "cr2c9_leavetype" || reference.Id == Guid.Empty)
                return false;
            Entity type;
            try {
                type = service.Retrieve("cr2c9_leavetype", reference.Id,
                    new ColumnSet("cr2c9_leavetypename", "statecode"));
            }
            catch (Exception) {
                // The request guard treats a missing type as unsupported. The
                // accounting caller uses strict mode so a lookup failure can
                // never silently skip a debit or reversal.
                if (strict) throw Guard.Error("The saved Leave Type could not be verified for accounting.");
                return false;
            }
            return (!requireActive || type.GetAttributeValue<OptionSetValue>("statecode")?.Value == 0) &&
                type.GetAttributeValue<string>("cr2c9_leavetypename") == TypeName;
        }

        internal static void RequireEvidence(IOrganizationService service, Guid requestId)
        {
            var query = new QueryExpression("annotation") {
                ColumnSet = new ColumnSet("isdocument", "filesize", "filename"), TopCount = 100
            };
            query.Criteria.AddCondition("objectid", ConditionOperator.Equal, requestId);
            var notes = service.RetrieveMultiple(query);
            if (notes.MoreRecords || !notes.Entities.Any(note =>
                note.GetAttributeValue<bool>("isdocument") &&
                note.GetAttributeValue<int>("filesize") > 0 &&
                !string.IsNullOrWhiteSpace(note.GetAttributeValue<string>("filename"))))
                throw Guard.Error("Upload supporting evidence before approving Emergency Leave. The approver must verify it.");
        }
    }
}
