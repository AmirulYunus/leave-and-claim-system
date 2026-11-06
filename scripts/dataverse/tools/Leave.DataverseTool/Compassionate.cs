using System.Xml.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

internal static partial class Development
{
    static readonly Guid CompassionateTypeId = Guid.Parse("e85a19e9-c65c-48a3-8352-63b301521efd");
    const string CompassionateRelationship = "cr2c9_compassionaterelationship";

    static void CompassionateSchemaDeploy()
    {
        var existing = Meta(Request).Attributes.SingleOrDefault(a => a.LogicalName == CompassionateRelationship);
        if (existing == null) {
            Client.Execute(new CreateAttributeRequest {
                EntityName = Request, SolutionUniqueName = Solution,
                Attribute = new StringAttributeMetadata {
                    SchemaName = CompassionateRelationship,
                    DisplayName = L("Compassionate Relationship"),
                    Description = L("Relationship to the family member for Compassionate Leave. Select an eligible relationship; the approver checks supporting evidence."),
                    MaxLength = 100,
                    RequiredLevel = new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.None)
                }
            });
        }
        else if (existing is not StringAttributeMetadata text || text.MaxLength < 100)
            throw new Exception("Compassionate relationship has an incompatible Dataverse type.");
        Client.Execute(new PublishXmlRequest { ParameterXml =
            "<importexportxml><entities><entity>" + Request + "</entity></entities></importexportxml>" });
        Console.WriteLine("Compassionate relationship column published; the leave type remains unavailable.");
    }

    static void CompassionateFormDeploy()
    {
        var formId = Guid.Parse("f297bf4c-1a1d-4560-b594-572612a10d5c");
        var form = Client.Retrieve("systemform", formId, new ColumnSet("formxml"));
        var xml = XDocument.Parse(form.GetAttributeValue<string>("formxml"));
        var controls = xml.Descendants("control").Where(c =>
            (string?)c.Attribute("datafieldname") == CompassionateRelationship).ToArray();
        if (controls.Length > 1) throw new Exception("Duplicate Compassionate relationship control.");
        if (controls.Length == 0) {
            var start = xml.Descendants("control").Single(c =>
                (string?)c.Attribute("datafieldname") == "cr2c9_startdate");
            var cell = new XElement(start.Ancestors("cell").First());
            foreach (var element in cell.DescendantsAndSelf().Where(e => e.Attribute("id") != null))
                element.SetAttributeValue("id", "{" + Guid.NewGuid().ToString("D") + "}");
            var control = cell.Descendants("control").Single();
            control.SetAttributeValue("id", CompassionateRelationship);
            control.SetAttributeValue("datafieldname", CompassionateRelationship);
            control.SetAttributeValue("disabled", "true");
            foreach (var label in cell.Descendants("label"))
                label.SetAttributeValue("description", "Relationship to family member");
            start.Ancestors("row").First().AddAfterSelf(new XElement("row", cell));
            Client.Update(new Entity("systemform", formId) {
                ["formxml"] = xml.ToString(SaveOptions.DisableFormatting)
            });
        }
        Client.Execute(new PublishXmlRequest { ParameterXml =
            "<importexportxml><entities><entity>" + Request + "</entity></entities></importexportxml>" });
        Add(formId, 60);
        Console.WriteLine("Compassionate relationship published read-only on Superior review form.");
    }

    static void CompassionateTypeDeploy()
    {
        var rows = Client.RetrieveMultiple(new QueryExpression("cr2c9_leavetype") {
            ColumnSet = new ColumnSet("cr2c9_leavetypename", "statecode")
        }).Entities;
        var matching = rows.Where(row => row.Id == CompassionateTypeId ||
            row.GetAttributeValue<string>("cr2c9_leavetypename") == "Compassionate Leave").ToArray();
        if (matching.Length > 1 || matching.Length == 1 &&
            (matching[0].Id != CompassionateTypeId ||
             matching[0].GetAttributeValue<string>("cr2c9_leavetypename") != "Compassionate Leave"))
            throw new Exception("An unexpected Compassionate Leave type exists; resolve before enabling.");
        if (matching.Length == 0) {
            Client.Create(new Entity("cr2c9_leavetype", CompassionateTypeId) {
                ["cr2c9_leavetypename"] = "Compassionate Leave",
                ["cr2c9_description"] = "Three consecutive calendar days for an eligible family relationship; supporting document required."
            });
            Console.WriteLine("Created Compassionate Leave type " + CompassionateTypeId);
        }
        else if (matching[0].GetAttributeValue<OptionSetValue>("statecode")?.Value != 0)
            throw new Exception("Compassionate Leave type is inactive; review the form before activation.");
        else Console.WriteLine("Verified active Compassionate Leave type " + CompassionateTypeId);
    }
}
