using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Crm.Sdk.Messages;

internal static partial class Development
{
    const string PreviewApi = "cr2c9_GetEntitlementPreview";
    const string PreviewResource = "cr2c9_employeeEntitlement.js";
    static readonly Guid EmployeeForm = Guid.Parse("ae8d487a-1898-43fd-b9ac-b7b980d2628c");
    static void EntitlementInspect()
    {
        var meta = Meta(Employee);
        foreach (var a in meta.Attributes.Where(a => a.LogicalName.StartsWith("cr2c9_")))
            Console.WriteLine($"{a.LogicalName} {a.AttributeType} {a.DisplayName?.UserLocalizedLabel?.Label}");
        var xml = XDocument.Parse(Client.Retrieve("systemform", EmployeeForm, new ColumnSet("formxml")).GetAttributeValue<string>("formxml"));
        Console.WriteLine("Profile controls: " + string.Join(", ",xml.Descendants("control").Select(x => (string?)x.Attribute("datafieldname"))));
        Console.WriteLine("Profile events: " + xml.Root?.Element("events"));
        Console.WriteLine("Profile libraries: " + xml.Root?.Element("formLibraries"));
        foreach (var r in Find("customapi","uniquename",PreviewApi,"isfunction","bindingtype","boundentitylogicalname","plugintypeid"))
            Console.WriteLine("Existing preview API " + r.Id);
    }
    static Guid CreateInSolution(Entity row)
    {
        var request = new CreateRequest { Target = row }; request["SolutionUniqueName"] = Solution;
        return ((CreateResponse)Client.Execute(request)).id;
    }
    static void EntitlementDeploy()
    {
        var dll = Path.Combine(Root,"server/Leave.Plugins/bin/Release/net462/Leave.Plugins.dll");
        var name = AssemblyName.GetAssemblyName(dll);
        var assembly = Find("pluginassembly","name",name.Name!,"version","publickeytoken").Single();
        if (assembly.GetAttributeValue<string>("publickeytoken") != Convert.ToHexString(name.GetPublicKeyToken()!).ToLowerInvariant())
            throw new Exception("Assembly signing identity differs.");
        Client.Update(new Entity("pluginassembly",assembly.Id) { ["content"] = Convert.ToBase64String(File.ReadAllBytes(dll)) });
        const string typename = "Leave.Plugins.EmployeeEntitlementPreview";
        var types = Find("plugintype","typename",typename,"pluginassemblyid");
        if (types.Any(x => x.GetAttributeValue<EntityReference>("pluginassemblyid")?.Id != assembly.Id)) throw new Exception("Preview type uses a different assembly.");
        var typeId = types.Length == 0 ? Client.Create(new Entity("plugintype") {
            ["pluginassemblyid"] = assembly.ToEntityReference(), ["typename"] = typename,
            ["name"] = typename, ["friendlyname"] = typename
        }) : types.Single().Id;
        var apis = Find("customapi","uniquename",PreviewApi,"isfunction","bindingtype","boundentitylogicalname");
        Guid apiId;
        if (apis.Length == 0) apiId = CreateInSolution(new Entity("customapi") {
            ["uniquename"] = PreviewApi, ["name"] = PreviewApi, ["displayname"] = "Employee entitlement rate preview",
            ["description"] = "Read-only current full-year leave rates from saved HR inputs. Does not grant or change balances.",
            ["isfunction"] = true, ["isprivate"] = false, ["bindingtype"] = new OptionSetValue(1),
            ["boundentitylogicalname"] = Employee, ["allowedcustomprocessingsteptype"] = new OptionSetValue(0),
            ["workflowsdkstepenabled"] = false, ["plugintypeid"] = new EntityReference("plugintype",typeId)
        });
        else {
            var existing = apis.Single(); apiId = existing.Id;
            if (!existing.GetAttributeValue<bool>("isfunction") || existing.GetAttributeValue<OptionSetValue>("bindingtype")?.Value != 1 || existing.GetAttributeValue<string>("boundentitylogicalname") != Employee)
                throw new Exception("Existing API contract differs.");
            Client.Update(new Entity("customapi",apiId) { ["plugintypeid"] = new EntityReference("plugintype",typeId) });
        }
        var props = Find("customapiresponseproperty","customapiid",apiId,"uniquename","type");
        if (props.Length == 0) CreateInSolution(new Entity("customapiresponseproperty") {
            ["customapiid"] = new EntityReference("customapi",apiId), ["uniquename"] = "PreviewJson",
            ["name"] = PreviewApi + ".PreviewJson", ["displayname"] = "Entitlement rate preview",
            ["type"] = new OptionSetValue(10)
        });
        else if (props.Length != 1 || props[0].GetAttributeValue<string>("uniquename") != "PreviewJson" || props[0].GetAttributeValue<OptionSetValue>("type")?.Value != 10)
            throw new Exception("API response contract differs.");

        var resources = Find("webresource","name",PreviewResource);
        var resource = new Entity("webresource") {
            ["name"] = PreviewResource, ["displayname"] = "Employee entitlement preview",
            ["webresourcetype"] = new OptionSetValue(3),
            ["content"] = Convert.ToBase64String(File.ReadAllBytes(Path.Combine(Root,PreviewResource)))
        };
        Guid resourceId;
        if (resources.Length == 0) resourceId = CreateInSolution(resource);
        else { resourceId = resources.Single().Id; resource.Id = resourceId; Client.Update(resource); }
        Add(resourceId,61);
        var form = Client.Retrieve("systemform",EmployeeForm,new ColumnSet("formxml"));
        var xml = XDocument.Parse(form.GetAttributeValue<string>("formxml"));
        var root = xml.Root!;
        var libraries = root.Element("formLibraries");
        if (libraries == null) { libraries = new XElement("formLibraries"); root.Add(libraries); }
        if (!libraries.Elements("Library").Any(x => (string?)x.Attribute("name") == PreviewResource))
            libraries.Add(new XElement("Library",new XAttribute("name",PreviewResource),new XAttribute("libraryUniqueId","{"+resourceId+"}")));
        var events = root.Element("events");
        if (events == null) { events = new XElement("events"); root.Add(events); }
        var onload = events.Elements("event").SingleOrDefault(x => (string?)x.Attribute("name") == "onload");
        if (onload == null) { onload = new XElement("event",new XAttribute("name","onload"),new XAttribute("application","false"),new XAttribute("active","true"),new XElement("Handlers")); events.Add(onload); }
        var handlers = onload.Element("Handlers");
        if (handlers == null) { handlers = new XElement("Handlers"); onload.Add(handlers); }
        var handler = handlers.Elements("Handler").SingleOrDefault(x => (string?)x.Attribute("functionName") == "EmployeeEntitlement.onLoad");
        if (handler == null) { handler = new XElement("Handler",new XAttribute("handlerUniqueId","{"+Guid.NewGuid()+"}")); handlers.Add(handler); }
        handler.SetAttributeValue("functionName","EmployeeEntitlement.onLoad"); handler.SetAttributeValue("libraryName",PreviewResource);
        handler.SetAttributeValue("enabled","true"); handler.SetAttributeValue("parameters",""); handler.SetAttributeValue("passExecutionContext","true");
        var fields = Meta(Employee).Attributes.Where(a => a.LogicalName.Contains("annual") && a.LogicalName.Contains("entitlement")).Select(a => a.LogicalName)
            .Concat(new[]{"cr2c9_entrauserid","cr2c9_superior"}).ToArray();
        foreach (var control in xml.Descendants("control").Where(x => fields.Contains((string?)x.Attribute("datafieldname")))) {
            control.SetAttributeValue("disabled","true");
            if ((string?)control.Attribute("datafieldname") == "cr2c9_annualleaveentitlementdays") {
                var cell = control.Parent;
                cell?.SetAttributeValue("visible","false");
                foreach (var label in cell?.Descendants("label") ?? Enumerable.Empty<XElement>()) label.SetAttributeValue("description","Legacy Annual Days");
            }
        }
        Client.Update(new Entity("systemform",EmployeeForm) { ["formxml"] = xml.ToString(SaveOptions.DisableFormatting) });
        Add(EmployeeForm,60);
        Client.Execute(new PublishXmlRequest { ParameterXml = "<importexportxml><entities><entity>cr2c9_employee</entity></entities><webresources><webresource>"+resourceId+"</webresource></webresources></importexportxml>" });
        Console.WriteLine("Deployed read-only preview API " + apiId + "; resource " + resourceId + "; profile form published. No role or balance changes.");
    }
    static void EntitlementTest()
    {
        string Preview(Guid employee) {
            var request = new OrganizationRequest(PreviewApi) { ["Target"] = new EntityReference(Employee,employee) };
            return (string)Client.Execute(request)["PreviewJson"];
        }
        var ownId = Guid.Parse("6938b1f2-22c2-f111-aaad-000d3a826b69");
        var ownBefore = Client.Retrieve(Employee,ownId,new ColumnSet(false)).RowVersion;
        var ownJson = Preview(ownId);
        using (var document = JsonDocument.Parse(ownJson)) {
            var annual = document.RootElement.GetProperty("Annual");
            if (annual.GetProperty("Status").GetString() != "PendingHrDetails" || annual.GetProperty("Days").ValueKind != JsonValueKind.Null)
                throw new Exception("Own missing-input preview did not skip.");
        }
        if (Client.Retrieve(Employee,ownId,new ColumnSet(false)).RowVersion != ownBefore) throw new Exception("Preview wrote the employee.");
        Console.WriteLine("PASS live missing-input preview returns Pending HR details/null and leaves employee version unchanged.");
        // Only this existing explicitly fictional development fixture is given
        // calculation inputs. No real synced employee gets defaults.
        var today = DateTime.UtcNow.AddHours(8).Date;
        Client.Update(new Entity(Employee,FixtureEmployee) {
            ["cr2c9_datejoined"] = today.AddYears(-5), ["cr2c9_employmenttype"] = new OptionSetValue(290040000),
            ["cr2c9_employeelevel"] = new OptionSetValue(290040000), ["cr2c9_position"] = "Analyst (fictional development fixture)"
        });
        var fixtureBefore = Client.Retrieve(Employee,FixtureEmployee,new ColumnSet(false)).RowVersion;
        var json = Preview(FixtureEmployee);
        using (var document = JsonDocument.Parse(json)) {
            var root = document.RootElement;
            if (root.GetProperty("CompletedYears").GetInt32() != 5 || root.GetProperty("Annual").GetProperty("Days").GetInt32() != 16 ||
                root.GetProperty("Sick").GetProperty("Days").GetInt32() != 22 || root.GetProperty("Hospitalisation").GetProperty("Days").GetInt32() != 60)
                throw new Exception("Live rate calculation differs from expected five-year fixture.");
        }
        if (Client.Retrieve(Employee,FixtureEmployee,new ColumnSet(false)).RowVersion != fixtureBefore) throw new Exception("Preview wrote fixture.");
        Console.WriteLine("PASS live fictional five-year profile: Annual 16 days / 128 hours, Sick 22, separate Hospitalisation 60; read-only preview.");
        Client.Update(new Entity(Employee,FixtureEmployee) { ["cr2c9_employeelevel"] = null });
        using (var document = JsonDocument.Parse(Preview(FixtureEmployee))) {
            var root = document.RootElement;
            if (root.GetProperty("Annual").GetProperty("Days").ValueKind != JsonValueKind.Null || root.GetProperty("Sick").GetProperty("Days").GetInt32() != 22)
                throw new Exception("Null level did not skip annual independently.");
        }
        Client.Update(new Entity(Employee,FixtureEmployee) { ["cr2c9_employeelevel"] = new OptionSetValue(290040000) });
        Console.WriteLine("PASS live null level skips annual independently; fictional fixture restored for UI review.");
    }
}
