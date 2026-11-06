using System.Xml.Linq;
using System.Reflection;
using System.Security.Cryptography;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

internal static partial class Development
{
    static void TrainingServerDeploy()
    {
        if (Meta(Request).Attributes.SingleOrDefault(a => a.LogicalName == "cr2c9_trainingcategory") is not StringAttributeMetadata)
            throw new Exception("Publish the Training Category text column before updating the server assemblies.");
        var components = new[] { "Leave.Plugins", "Leave.BalanceGuards" };
        foreach (var name in components) {
            var path = Root + "/server/" + name + "/bin/Release/net462/" + name + ".dll";
            var assembly = Find("pluginassembly", "name", name, "publickeytoken").Single();
            var token = Convert.ToHexString(AssemblyName.GetAssemblyName(path).GetPublicKeyToken()!).ToLowerInvariant();
            if (assembly.GetAttributeValue<string>("publickeytoken") != token)
                throw new Exception("Signed assembly identity differs: " + name);
        }
        var guard = Find("plugintype", "typename", "Leave.BalanceGuards.BalanceStagingGuard").Single();
        var guardSteps = Find("sdkmessageprocessingstep", "eventhandler", guard.Id, "statecode", "stage", "mode");
        if (guardSteps.Length != 14 || guardSteps.Any(step =>
            step.GetAttributeValue<OptionSetValue>("statecode")?.Value != 0 ||
            step.GetAttributeValue<OptionSetValue>("stage")?.Value != 20 ||
            step.GetAttributeValue<OptionSetValue>("mode")?.Value != 0))
            throw new Exception("Balance write guards must be active before Training deployment.");
        foreach (var name in components) {
            var path = Root + "/server/" + name + "/bin/Release/net462/" + name + ".dll";
            var bytes = File.ReadAllBytes(path);
            var assembly = Find("pluginassembly", "name", name, "content").Single();
            Client.Update(new Entity("pluginassembly", assembly.Id) { ["content"] = Convert.ToBase64String(bytes) });
            var saved = Client.Retrieve("pluginassembly", assembly.Id, new ColumnSet("content"));
            if (!SHA256.HashData(Convert.FromBase64String(saved.GetAttributeValue<string>("content")))
                    .SequenceEqual(SHA256.HashData(bytes)))
                throw new Exception("Training assembly readback differs: " + name);
            Console.WriteLine("Updated and verified " + name);
        }
        Console.WriteLine("Training server assemblies deployed; Training Leave type remains unavailable until the form is published.");
    }

    static void TrainingSchemaDeploy()
    {
        var existing = Meta(Request).Attributes.SingleOrDefault(a => a.LogicalName == "cr2c9_trainingcategory");
        if (existing == null) {
            Client.Execute(new CreateAttributeRequest {
                EntityName = Request, SolutionUniqueName = Solution,
                Attribute = new StringAttributeMetadata {
                    SchemaName = "cr2c9_trainingcategory",
                    DisplayName = L("Training Category"),
                    Description = L("Company Required or Employee-Requested External. Only the latter charges Annual Leave."),
                    MaxLength = 50,
                    RequiredLevel = new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.None)
                }
            });
        }
        else if (existing is not StringAttributeMetadata text || text.MaxLength < 50)
            throw new Exception("Training category has an incompatible Dataverse type.");
        Client.Execute(new PublishXmlRequest { ParameterXml =
            "<importexportxml><entities><entity>" + Request + "</entity></entities></importexportxml>" });
        Console.WriteLine("Training category column published; Training Leave is not activated by this command.");
    }

    static void TrainingReviewFormDeploy()
    {
        var formId = Guid.Parse("f297bf4c-1a1d-4560-b594-572612a10d5c");
        var form = Client.Retrieve("systemform", formId, new ColumnSet("formxml"));
        var xml = XDocument.Parse(form.GetAttributeValue<string>("formxml"));
        var controls = xml.Descendants("control").Where(c =>
            (string?)c.Attribute("datafieldname") == "cr2c9_trainingcategory").ToArray();
        if (controls.Length > 1) throw new Exception("Duplicate Training Category control.");
        if (controls.Length == 0) {
            var start = xml.Descendants("control").Single(c =>
                (string?)c.Attribute("datafieldname") == "cr2c9_startdate");
            var cell = new XElement(start.Ancestors("cell").First());
            foreach (var element in cell.DescendantsAndSelf().Where(e => e.Attribute("id") != null))
                element.SetAttributeValue("id", "{" + Guid.NewGuid().ToString("D") + "}");
            var control = cell.Descendants("control").Single();
            control.SetAttributeValue("id", "cr2c9_trainingcategory");
            control.SetAttributeValue("datafieldname", "cr2c9_trainingcategory");
            control.SetAttributeValue("disabled", "true");
            foreach (var label in cell.Descendants("label"))
                label.SetAttributeValue("description", "Training category");
            start.Ancestors("row").First().AddAfterSelf(new XElement("row", cell));
            Client.Update(new Entity("systemform", formId) {
                ["formxml"] = xml.ToString(SaveOptions.DisableFormatting)
            });
        }
        Client.Execute(new PublishXmlRequest { ParameterXml =
            "<importexportxml><entities><entity>" + Request + "</entity></entities></importexportxml>" });
        Add(formId, 60);
        Console.WriteLine("Training Category published read-only on Superior review form.");
    }
}
