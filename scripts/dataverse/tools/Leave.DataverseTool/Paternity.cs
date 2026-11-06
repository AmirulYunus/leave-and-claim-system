using System.Xml.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

internal static partial class Development
{
    static void PaternitySchemaDeploy()
    {
        var fields = new (string Name, string Label, DateTimeBehavior Behavior, DateTimeFormat Format)[] {
            ("cr2c9_paternityexpecteddate", "Expected Confinement Date", DateTimeBehavior.DateOnly, DateTimeFormat.DateOnly),
            ("cr2c9_paternitybirthdate", "Actual Birth Date", DateTimeBehavior.DateOnly, DateTimeFormat.DateOnly),
            ("cr2c9_paternitynoticedate", "Paternity Notice Date", DateTimeBehavior.DateOnly, DateTimeFormat.DateOnly),
            ("cr2c9_paternityhrverifiedon", "Paternity HR Verified On", DateTimeBehavior.UserLocal, DateTimeFormat.DateAndTime)
        };
        foreach (var field in fields)
        {
            var existing = Meta(Request).Attributes.SingleOrDefault(a => a.LogicalName == field.Name);
            if (existing is null)
            {
                Client.Execute(new CreateAttributeRequest {
                    EntityName = Request, SolutionUniqueName = Solution,
                    Attribute = new DateTimeAttributeMetadata {
                        SchemaName = field.Name, DisplayName = L(field.Label),
                        Description = L(field.Name == "cr2c9_paternityhrverifiedon" ?
                            "Server-stamped HR check of marriage, service, birth, notice and prior confinements." :
                            "Paternity birth or notice date supplied with the employee request."),
                        DateTimeBehavior = field.Behavior, Format = field.Format,
                        RequiredLevel = new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.None)
                    }
                });
                Console.WriteLine("Created " + field.Name);
            }
            else if (existing is not DateTimeAttributeMetadata date ||
                     date.DateTimeBehavior?.Value != field.Behavior.Value || date.Format != field.Format)
                throw new Exception("Paternity column definition differs: " + field.Name);
            else Console.WriteLine("Verified " + field.Name);
        }
        Client.Execute(new PublishXmlRequest { ParameterXml =
            "<importexportxml><entities><entity>" + Request + "</entity></entities></importexportxml>" });
        Console.WriteLine("Paternity request fields published; approval safeguards must be deployed separately.");
    }

    static void PaternityFormDeploy()
    {
        var formId = Guid.Parse("f297bf4c-1a1d-4560-b594-572612a10d5c");
        var form = Client.Retrieve("systemform", formId, new ColumnSet("formxml"));
        var xml = XDocument.Parse(form.GetAttributeValue<string>("formxml"));
        var start = xml.Descendants("control").Single(c =>
            (string?)c.Attribute("datafieldname") == "cr2c9_startdate");
        var anchor = start.Ancestors("row").First();
        var template = start.Ancestors("cell").First();
        foreach (var field in new[] {
            ("cr2c9_paternityexpecteddate", "Expected confinement date", true),
            ("cr2c9_paternitybirthdate", "Actual birth date", true),
            ("cr2c9_paternitynoticedate", "Paternity notice date", true),
            ("cr2c9_paternityhrverifiedon", "HR paternity verification", false)
        })
        {
            var existing = xml.Descendants("control").Where(c =>
                (string?)c.Attribute("datafieldname") == field.Item1).ToArray();
            if (existing.Length > 1) throw new Exception("Duplicate paternity control: " + field.Item1);
            if (existing.Length == 1) continue;
            var cell = new XElement(template);
            foreach (var element in cell.DescendantsAndSelf().Where(e => e.Attribute("id") != null))
                element.SetAttributeValue("id", "{" + Guid.NewGuid().ToString("D") + "}");
            var control = cell.Descendants("control").Single();
            control.SetAttributeValue("id", field.Item1);
            control.SetAttributeValue("datafieldname", field.Item1);
            if (field.Item3) control.SetAttributeValue("disabled", "true");
            else control.Attribute("disabled")?.Remove();
            foreach (var label in cell.Descendants("label"))
                label.SetAttributeValue("description", field.Item2);
            var row = new XElement("row", cell);
            anchor.AddAfterSelf(row);
            anchor = row;
        }
        Client.Update(new Entity("systemform", formId) {
            ["formxml"] = xml.ToString(SaveOptions.DisableFormatting)
        });
        Client.Execute(new PublishXmlRequest { ParameterXml =
            "<importexportxml><entities><entity>" + Request + "</entity></entities></importexportxml>" });
        Add(formId, 60);
        Console.WriteLine("Published paternity dates and HR verification on the review form.");
    }
}
