using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Leave.Plugins
{
    internal static class UnpaidPolicy
    {
        internal const string TypeName = "Unpaid Leave";

        internal static bool IsUnpaid(IOrganizationService service, Entity request, bool requireActive = true)
        {
            var reference = request.GetAttributeValue<EntityReference>("cr2c9_leavetypename");
            if (reference?.LogicalName != "cr2c9_leavetype" || reference.Id == System.Guid.Empty) return false;
            Entity type;
            try {
                type = service.Retrieve(reference.LogicalName, reference.Id,
                    new ColumnSet("cr2c9_leavetypename", "statecode"));
            }
            catch (System.Exception) { return false; }
            return (!requireActive || type.GetAttributeValue<OptionSetValue>("statecode")?.Value == 0) &&
                type.GetAttributeValue<string>("cr2c9_leavetypename") == TypeName;
        }

        internal static void RequireWrittenRequest(Entity request)
        {
            if (string.IsNullOrWhiteSpace(request.GetAttributeValue<string>("cr2c9_reason")))
                throw Guard.Error("Enter a reason for Unpaid Leave before submitting.");
        }
    }
}
