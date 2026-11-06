using System.Xml.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

internal static partial class Development
{
    const string ExpectedConfinement = "cr2c9_expectedconfinementdate";
    const string FormalNotice = "cr2c9_maternityformalnoticedate";
    const string HrVerified = "cr2c9_maternityhrverifiedon";

    static void MaternitySchemaDeploy()
    {
        var fields = new (string Name, string Label, DateTimeBehavior Behavior, DateTimeFormat Format)[] {
            (ExpectedConfinement, "Expected Confinement Date", DateTimeBehavior.DateOnly, DateTimeFormat.DateOnly),
            (FormalNotice, "Maternity Formal Notice Date", DateTimeBehavior.DateOnly, DateTimeFormat.DateOnly),
            (HrVerified, "Maternity HR Verified On", DateTimeBehavior.UserLocal, DateTimeFormat.DateAndTime)
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
                        Description = L(field.Name == HrVerified ?
                            "Server-stamped HR verification before maternity approval." :
                            "Employee-supplied maternity notice date for HR verification."),
                        DateTimeBehavior = field.Behavior, Format = field.Format,
                        RequiredLevel = new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.None)
                    }
                });
                Console.WriteLine("Created " + field.Name);
            }
            else if (existing is not DateTimeAttributeMetadata date ||
                     date.DateTimeBehavior?.Value != field.Behavior.Value || date.Format != field.Format)
                throw new Exception("Maternity column definition differs: " + field.Name);
            else Console.WriteLine("Verified " + field.Name);
        }
        Client.Execute(new PublishXmlRequest { ParameterXml =
            "<importexportxml><entities><entity>" + Request + "</entity></entities></importexportxml>" });
        Console.WriteLine("Maternity request fields published; approval DLL remains unchanged.");
    }

    static void MaternityFormInspect()
    {
        var rows = Client.RetrieveMultiple(new QueryExpression("systemform") {
            ColumnSet = new ColumnSet("name", "type", "formxml", "objecttypecode")
        }).Entities.Where(x => x.GetAttributeValue<string>("objecttypecode") == Request).ToArray();
        foreach (var form in rows)
        {
            var xml = XDocument.Parse(form.GetAttributeValue<string>("formxml"));
            Console.WriteLine("FORM " + form.Id + " " + form.GetAttributeValue<string>("name") +
                " type=" + form.GetAttributeValue<OptionSetValue>("type")?.Value);
            foreach (var cell in xml.Descendants("cell").Where(c => c.Descendants("control")
                         .Any(k => new[] { "cr2c9_startdate", "cr2c9_enddate", "cr2c9_leavetypename", HrVerified }
                             .Contains((string?)k.Attribute("datafieldname")))))
                Console.WriteLine(cell.ToString(SaveOptions.DisableFormatting));
        }
    }

    static void MaternityFormDeploy()
    {
        var formId = Guid.Parse("f297bf4c-1a1d-4560-b594-572612a10d5c");
        var form = Client.Retrieve("systemform", formId, new ColumnSet("formxml"));
        var xml = XDocument.Parse(form.GetAttributeValue<string>("formxml"));
        var start = xml.Descendants("control").Single(c =>
            (string?)c.Attribute("datafieldname") == "cr2c9_startdate");
        var anchor = start.Ancestors("row").First();
        var template = start.Ancestors("cell").First();
        foreach (var field in new[] {
            (ExpectedConfinement, "Expected confinement date", true),
            (FormalNotice, "Formal maternity notice date", true),
            (HrVerified, "HR maternity verification", false)
        })
        {
            var existing = xml.Descendants("control").Where(c =>
                (string?)c.Attribute("datafieldname") == field.Item1).ToArray();
            if (existing.Length > 1) throw new Exception("Duplicate maternity control: " + field.Item1);
            if (existing.Length == 1) continue;
            var cell = new XElement(template);
            foreach (var e in cell.DescendantsAndSelf().Where(e => e.Attribute("id") != null))
                e.SetAttributeValue("id", "{" + Guid.NewGuid().ToString("D") + "}");
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
        Console.WriteLine("Published read-only maternity dates and HR verification control on the request review form.");
    }
}
