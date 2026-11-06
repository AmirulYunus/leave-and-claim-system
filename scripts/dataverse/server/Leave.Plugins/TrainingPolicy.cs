using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Leave.Plugins
{
    internal static class TrainingPolicy
    {
        internal const string TypeName = "Training Leave";
        internal const string Category = "cr2c9_trainingcategory";
        internal const string CompanyRequired = "Company Required";
        internal const string EmployeeRequestedExternal = "Employee-Requested External";

        internal static bool IsTraining(IOrganizationService service, Entity request, bool requireActive = true)
        {
            var reference = request.GetAttributeValue<EntityReference>("cr2c9_leavetypename");
            if (reference?.LogicalName != "cr2c9_leavetype" || reference.Id == Guid.Empty) return false;
            Entity type;
            try {
                type = service.Retrieve(reference.LogicalName, reference.Id,
                    new ColumnSet("cr2c9_leavetypename", "statecode"));
            }
            catch (Exception) { return false; }
            return (!requireActive || type.GetAttributeValue<OptionSetValue>("statecode")?.Value == 0) &&
                type.GetAttributeValue<string>("cr2c9_leavetypename") == TypeName;
        }

        internal static bool ChargesAnnual(Entity request)
        {
            var category = request.GetAttributeValue<string>(Category);
            if (category == EmployeeRequestedExternal) return true;
            if (category == CompanyRequired) return false;
            throw Guard.Error("Choose whether Training Leave is company-required or employee-requested external training.");
        }
    }
}
