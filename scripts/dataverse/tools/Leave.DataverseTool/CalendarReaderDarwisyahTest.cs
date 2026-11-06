using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

internal static partial class Development
{
    static void CalendarReaderDarwisyahTest()
    {
        const string email = "Darwisyah.Fakhira@Asteriskdigital.com.my";
        var employee = Find(Employee, "cr2c9_email", email,
            "cr2c9_entrauserid", "cr2c9_isactive", "statecode").Single();
        var identity = Guid.Parse(employee.GetAttributeValue<string>("cr2c9_entrauserid"));
        if (!employee.GetAttributeValue<bool>("cr2c9_isactive") ||
            employee.GetAttributeValue<OptionSetValue>("statecode")?.Value != 0)
            throw new Exception("Darwisyah's Employee record is not active.");
        var user = Find("systemuser", "azureactivedirectoryobjectid", identity,
            "businessunitid", "isdisabled").Single();
        if (user.GetAttributeValue<bool>("isdisabled"))
            throw new Exception("Darwisyah's Dataverse account is disabled.");
        var bu = user.GetAttributeValue<EntityReference>("businessunitid")?.Id ?? Guid.Empty;
        var roles = new QueryExpression("role") { ColumnSet = new ColumnSet("name"), TopCount = 2 };
        roles.Criteria.AddCondition("name", ConditionOperator.Equal, "Leave Calendar Reader");
        roles.Criteria.AddCondition("businessunitid", ConditionOperator.Equal, bu);
        var role = Client.RetrieveMultiple(roles).Entities.Single();
        foreach (var message in new[] { "Create", "Update" }) {
            var step = Find("sdkmessageprocessingstep", "name",
                "Leave calendar reader sync: " + message, "statecode", "stage", "mode").Single();
            if (step.GetAttributeValue<OptionSetValue>("statecode")?.Value != 0 ||
                step.GetAttributeValue<OptionSetValue>("stage")?.Value != 40 ||
                step.GetAttributeValue<OptionSetValue>("mode")?.Value != 0)
                throw new Exception("Calendar reader plug-in step is not active: " + message);
        }
        bool Assigned()
        {
            var q = new QueryExpression("systemuserroles") {
                ColumnSet = new ColumnSet(false), TopCount = 1
            };
            q.Criteria.AddCondition("systemuserid", ConditionOperator.Equal, user.Id);
            q.Criteria.AddCondition("roleid", ConditionOperator.Equal, role.Id);
            return Client.RetrieveMultiple(q).Entities.Count == 1;
        }
        if (!Assigned()) throw new Exception("Darwisyah's existing reader role is missing; repair before this test.");
        var relationship = new Relationship("systemuserroles_association");
        var references = new EntityReferenceCollection { role.ToEntityReference() };
        try {
            Client.Disassociate("systemuser", user.Id, relationship, references);
            if (Assigned()) throw new Exception("The reader role could not be removed for the test.");
            Client.Update(new Entity(Employee, employee.Id) {
                ["cr2c9_entrauserid"] = identity.ToString("D")
            });
            if (!Assigned()) throw new Exception("Employee update did not restore the calendar reader role.");
            Console.WriteLine("PASS Darwisyah's verified Employee update restored only the dedicated calendar reader role.");
        }
        finally {
            if (!Assigned())
                Client.Associate("systemuser", user.Id, relationship, references);
        }
    }
}
