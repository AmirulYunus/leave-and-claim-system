using System.Xml.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

internal static partial class Development
{
    static readonly Guid MarriageTypeId = Guid.Parse("75aa666a-2b4e-49e5-8766-0dc59e6fc25f");
    const string MarriageRegisteredOn = "cr2c9_marriageregisteredon";
    const string FirstLegalMarriage = "cr2c9_firstlegalmarriage";

    static void MarriageSchemaDeploy()
    {
        var attributes = Meta(Request).Attributes;
        if (attributes.All(a => a.LogicalName != MarriageRegisteredOn)) {
            Client.Execute(new CreateAttributeRequest {
                EntityName = Request, SolutionUniqueName = Solution,
                Attribute = new DateTimeAttributeMetadata {
                    SchemaName = MarriageRegisteredOn, DisplayName = L("Marriage Registration Date"),
                    Description = L("Official legal marriage registration date supplied by the employee."),
                    DateTimeBehavior = DateTimeBehavior.DateOnly, Format = DateTimeFormat.DateOnly,
                    RequiredLevel = new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.None)
                }
            });
            Console.WriteLine("Created " + MarriageRegisteredOn);
        }
        else if (attributes.Single(a => a.LogicalName == MarriageRegisteredOn) is not DateTimeAttributeMetadata date ||
                 date.DateTimeBehavior?.Value != DateTimeBehavior.DateOnly.Value || date.Format != DateTimeFormat.DateOnly)
            throw new Exception("Marriage registration date has the wrong Dataverse type.");
        if (attributes.All(a => a.LogicalName != FirstLegalMarriage)) {
            Client.Execute(new CreateAttributeRequest {
                EntityName = Request, SolutionUniqueName = Solution,
                Attribute = new BooleanAttributeMetadata {
                    SchemaName = FirstLegalMarriage, DisplayName = L("First Legal Marriage Declaration"),
                    Description = L("Employee declares this request relates to their first legal marriage; the approver checks the certificate."),
                    OptionSet = new BooleanOptionSetMetadata {
                        TrueOption = new OptionMetadata(L("Yes"), 1),
                        FalseOption = new OptionMetadata(L("No"), 0)
                    },
                    RequiredLevel = new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.None)
                }
            });
            Console.WriteLine("Created " + FirstLegalMarriage);
        }
        else if (attributes.Single(a => a.LogicalName == FirstLegalMarriage) is not BooleanAttributeMetadata)
            throw new Exception("First-marriage declaration has the wrong Dataverse type.");
        Client.Execute(new PublishXmlRequest { ParameterXml =
            "<importexportxml><entities><entity>" + Request + "</entity></entities></importexportxml>" });
        Console.WriteLine("Marriage request columns published. Leave Type is not enabled by this command.");
    }

    static void MarriageFormDeploy()
    {
        var formId = Guid.Parse("f297bf4c-1a1d-4560-b594-572612a10d5c");
        var form = Client.Retrieve("systemform", formId, new ColumnSet("formxml"));
        var xml = XDocument.Parse(form.GetAttributeValue<string>("formxml"));
        var start = xml.Descendants("control").Single(c =>
            (string?)c.Attribute("datafieldname") == "cr2c9_startdate");
        var anchor = start.Ancestors("row").First();
        var template = start.Ancestors("cell").First();
        foreach (var field in new[] {
            (MarriageRegisteredOn, "Official marriage registration date"),
            (FirstLegalMarriage, "First legal marriage declaration")
        }) {
            var existing = xml.Descendants("control").Where(c =>
                (string?)c.Attribute("datafieldname") == field.Item1).ToArray();
            if (existing.Length > 1) throw new Exception("Duplicate marriage control: " + field.Item1);
            if (existing.Length == 1) continue;
            var cell = new XElement(template);
            foreach (var element in cell.DescendantsAndSelf().Where(e => e.Attribute("id") != null))
                element.SetAttributeValue("id", "{" + Guid.NewGuid().ToString("D") + "}");
            var control = cell.Descendants("control").Single();
            control.SetAttributeValue("id", field.Item1);
            control.SetAttributeValue("datafieldname", field.Item1);
            control.SetAttributeValue("disabled", "true");
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
        Console.WriteLine("Marriage details published read-only on the review form.");
    }

    static void MarriageTypeDeploy()
    {
        var rows = Client.RetrieveMultiple(new QueryExpression("cr2c9_leavetype") {
            ColumnSet = new ColumnSet("cr2c9_leavetypename", "statecode")
        }).Entities;
        var matching = rows.Where(row => row.Id == MarriageTypeId ||
            row.GetAttributeValue<string>("cr2c9_leavetypename") == "Marriage Leave").ToArray();
        if (matching.Length > 1 || matching.Length == 1 &&
            (matching[0].Id != MarriageTypeId ||
             matching[0].GetAttributeValue<string>("cr2c9_leavetypename") != "Marriage Leave"))
            throw new Exception("An unexpected Marriage Leave record exists; resolve it before enabling the category.");
        if (matching.Length == 0) {
            var row = new Entity("cr2c9_leavetype", MarriageTypeId) {
                ["cr2c9_leavetypename"] = "Marriage Leave",
                ["cr2c9_description"] = "First legal marriage, three consecutive calendar days after probation; certificate required."
            };
            Client.Create(row);
            Console.WriteLine("Created Marriage Leave type " + MarriageTypeId);
        }
        else if (matching[0].GetAttributeValue<OptionSetValue>("statecode")?.Value != 0)
            throw new Exception("Marriage Leave type is inactive; do not activate it without reviewing the form.");
        else Console.WriteLine("Verified active Marriage Leave type " + MarriageTypeId);
    }
}
