using System.Xml.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Crm.Sdk.Messages;

internal static partial class Development
{
    static readonly Guid CancellationFormId = Guid.Parse("319c6c20-7d7a-45be-a5a0-114863078a74");
    const string CancellationScript = "cr2c9_leaveCancellationForm.js";

    static void CancellationFormInspect()
    {
        foreach (var id in new[] { Guid.Parse("f297bf4c-1a1d-4560-b594-572612a10d5c"), CancellationFormId })
        {
            var row = Client.Retrieve("systemform", id, new ColumnSet("formxml"));
            var xml = XDocument.Parse(row.GetAttributeValue<string>("formxml"));
            Console.WriteLine("FORM " + id);
            foreach (var e in xml.Descendants().Where(e => e.Name.LocalName is "events" or "event" or "Library" or "Handler" or "formLibraries"))
                if (!e.Ancestors().Any(a => a.Name.LocalName == "events" && e.Name.LocalName == "events"))
                    Console.WriteLine(e.ToString(SaveOptions.DisableFormatting).Substring(0, Math.Min(2000,e.ToString(SaveOptions.DisableFormatting).Length)));
        }
    }

    static void CancellationFormDeploy()
    {
        var file = Path.Combine(Root, CancellationScript);
        if (!File.Exists(file)) throw new Exception("Cancellation script is missing.");
        var existing = Find("webresource", "name", CancellationScript, "name");
        if (existing.Length > 1) throw new Exception("Ambiguous cancellation script.");
        var resource = new Entity("webresource") {
            ["name"] = CancellationScript,
            ["displayname"] = "Leave cancellation and HR correction review",
            ["webresourcetype"] = new OptionSetValue(3),
            ["content"] = Convert.ToBase64String(File.ReadAllBytes(file))
        };
        Guid resourceId;
        if (existing.Length == 0) resourceId = Client.Create(resource);
        else { resourceId = existing[0].Id; resource.Id = resourceId; Client.Update(resource); }
        Add(resourceId, 61);

        var form = Client.Retrieve("systemform", CancellationFormId, new ColumnSet("formxml"));
        var xml = XDocument.Parse(form.GetAttributeValue<string>("formxml"));
        var root = xml.Root ?? throw new Exception("Missing cancellation form root.");
        foreach (var name in new[] { "cr2c9_decisionbyentraid", "cr2c9_expectedversion" })
        {
            var control = root.Descendants("control").SingleOrDefault(e => (string?)e.Attribute("datafieldname") == name)
                ?? throw new Exception("Cancellation form lacks " + name);
            control.SetAttributeValue("disabled", "true");
            var cell = control.Ancestors("cell").FirstOrDefault()
                ?? throw new Exception("Cancellation form field has no cell: " + name);
            cell.SetAttributeValue("visible", "false");
        }
        var libraries = root.Element("formLibraries");
        if (libraries == null) { libraries = new XElement("formLibraries"); root.Add(libraries); }
        foreach (var duplicate in libraries.Elements("Library").Where(e => (string?)e.Attribute("name") == CancellationScript).Skip(1).ToArray()) duplicate.Remove();
        if (!libraries.Elements("Library").Any(e => (string?)e.Attribute("name") == CancellationScript))
            libraries.Add(new XElement("Library", new XAttribute("name", CancellationScript),
                new XAttribute("libraryUniqueId", "{" + Guid.NewGuid().ToString("D") + "}")));
        var events = root.Element("events");
        if (events == null) { events = new XElement("events"); root.Add(events); }
        foreach (var pair in new[] { ("onload", "LeaveCancellationForm.onLoad"), ("onsave", "LeaveCancellationForm.onSave") })
        {
            var ev = events.Elements("event").SingleOrDefault(e => (string?)e.Attribute("name") == pair.Item1);
            if (ev == null) {
                ev = new XElement("event", new XAttribute("name", pair.Item1),
                    new XAttribute("application", "false"), new XAttribute("active", "false"), new XElement("Handlers"));
                events.Add(ev);
            }
            var handlers = ev.Element("Handlers");
            if (handlers == null) { handlers = new XElement("Handlers"); ev.Add(handlers); }
            foreach (var old in handlers.Elements("Handler").Where(h => (string?)h.Attribute("libraryName") == CancellationScript).ToArray()) old.Remove();
            handlers.Add(new XElement("Handler", new XAttribute("functionName", pair.Item2),
                new XAttribute("libraryName", CancellationScript),
                new XAttribute("handlerUniqueId", "{" + Guid.NewGuid().ToString("D") + "}"),
                new XAttribute("enabled", "true"), new XAttribute("parameters", ""),
                new XAttribute("passExecutionContext", "true")));
        }
        Client.Update(new Entity("systemform", CancellationFormId) { ["formxml"] = xml.ToString(SaveOptions.DisableFormatting) });
        Client.Execute(new PublishXmlRequest { ParameterXml = "<importexportxml><entities><entity>"+Cancellation+"</entity></entities><webresources><webresource>"+resourceId+"</webresource></webresources></importexportxml>" });
        Console.WriteLine("Published cancellation form script and review events. Web resource: " + resourceId);
    }

    static void CancellationFormFixture()
    {
        var approved = Guid.Parse("f0ffcfcc-aed7-4407-9ba6-86a17c44708d");
        SuperiorFormVerify();
        var existing = Find(Cancellation, "cr2c9_leaverequest", approved, "cr2c9_status");
        if (existing.Length > 1 || existing.Any(e => e.GetAttributeValue<OptionSetValue>("cr2c9_status")?.Value != 0))
            throw new Exception("The fictional form request already has a decided or duplicate cancellation.");
        var id = existing.Length == 1 ? existing[0].Id :
            CreateFictionalCancellation(approved, "Fictional Superior cancellation form and exact eight-hour reversal verification.");
        var request = Client.Retrieve(Request, approved, new ColumnSet("cr2c9_status"));
        var period = Find(BalancePeriodTable, "cr2c9_employee", FixtureEmployee, "cr2c9_employee").Single();
        var available = Find(BalanceEntryTable, "cr2c9_balanceperiod", period.Id, "cr2c9_hours")
            .Sum(e => e.GetAttributeValue<int>("cr2c9_hours"));
        if (request.GetAttributeValue<OptionSetValue>("cr2c9_status")?.Value != 1 || available != 120)
            throw new Exception("Pending cancellation changed the original approval or balance.");
        Console.WriteLine("PASS fictional pending cancellation; original Approved, available 120h; cancellation=" + id);
    }

    static void CancellationFormVerify()
    {
        var approved = Guid.Parse("f0ffcfcc-aed7-4407-9ba6-86a17c44708d");
        var row = Find(Cancellation, "cr2c9_leaverequest", approved,
            "cr2c9_status", "cr2c9_decisionbyentraid", "cr2c9_decisionon").Single();
        var request = Client.Retrieve(Request, approved, new ColumnSet("cr2c9_status"));
        var actor = Client.Retrieve("systemuser", ((WhoAmIResponse)Client.Execute(
            new WhoAmIRequest())).UserId, new ColumnSet("azureactivedirectoryobjectid"));
        var period = Find(BalancePeriodTable, "cr2c9_employee", FixtureEmployee, "cr2c9_employee").Single();
        var ledger = Find(BalanceEntryTable, "cr2c9_leaverequest", approved,
            "cr2c9_entrykind", "cr2c9_hours", "cr2c9_reverses");
        var debit = ledger.SingleOrDefault(e => e.GetAttributeValue<OptionSetValue>("cr2c9_entrykind")?.Value == 290040001);
        var reversal = ledger.SingleOrDefault(e => e.GetAttributeValue<OptionSetValue>("cr2c9_entrykind")?.Value == 290040002);
        var available = Find(BalanceEntryTable, "cr2c9_balanceperiod", period.Id, "cr2c9_hours")
            .Sum(e => e.GetAttributeValue<int>("cr2c9_hours"));
        if (row.GetAttributeValue<OptionSetValue>("cr2c9_status")?.Value != 1 ||
            row.GetAttributeValue<string>("cr2c9_decisionbyentraid") != actor.GetAttributeValue<Guid>("azureactivedirectoryobjectid").ToString("D") ||
            !row.Contains("cr2c9_decisionon") || request.GetAttributeValue<OptionSetValue>("cr2c9_status")?.Value != 5 ||
            ledger.Length != 2 || debit?.GetAttributeValue<int>("cr2c9_hours") != -8 ||
            reversal?.GetAttributeValue<int>("cr2c9_hours") != 8 ||
            reversal.GetAttributeValue<EntityReference>("cr2c9_reverses")?.Id != debit.Id || available != 128)
            throw new Exception("Published cancellation form did not save one exact eight-hour reversal.");
        Console.WriteLine("PASS published cancellation form: one Superior decision and +8h exact reversal; fictional balance 120→128.");
    }
}
