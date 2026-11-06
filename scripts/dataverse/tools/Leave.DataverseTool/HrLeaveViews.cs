using System.Xml.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

internal static partial class Development
{
    static void HrLeaveViewsDeploy()
    {
        var source = Find("savedquery", "name", "Active Leave Requests",
            "returnedtypecode", "querytype", "fetchxml", "layoutxml").Single(x =>
            x.GetAttributeValue<string>("returnedtypecode") == Request &&
            x.GetAttributeValue<int?>("querytype") == 0);
        var chosen = new List<EntityReference>();
        foreach (var (name, period, status, upcoming) in new[] {
            ("All Leave Requests", "", (int?)null, false),
            ("Pending Approval", "", (int?)0, false),
            ("Approved Leave", "", (int?)1, false),
            ("Upcoming Leave", "", (int?)1, true),
            ("Approved Leave — This Month", "this-month", (int?)1, false),
            ("Approved Leave — This Year", "this-year", (int?)1, false)
        }) {
            var xml = XDocument.Parse(source.GetAttributeValue<string>("fetchxml"));
            var entity = xml.Root?.Element("entity") ?? throw new Exception("Source view has no entity.");
            entity.Elements("order").Remove();
            entity.AddFirst(new XElement("order", new XAttribute("attribute", upcoming ? "cr2c9_startdate" : "createdon"),
                new XAttribute("descending", upcoming ? "false" : "true")));
            if (status.HasValue)
                entity.Add(new XElement("filter", new XAttribute("type", "and"),
                    new XElement("condition", new XAttribute("attribute", "cr2c9_status"),
                        new XAttribute("operator", "eq"), new XAttribute("value", status.Value))));
            if (upcoming)
                entity.Add(new XElement("filter", new XAttribute("type", "and"),
                    new XElement("condition", new XAttribute("attribute", "cr2c9_startdate"),
                        new XAttribute("operator", "next-x-days"), new XAttribute("value", "3650"))));
            if (period.Length > 0)
                entity.Add(new XElement("filter", new XAttribute("type", "and"),
                    new XElement("condition", new XAttribute("attribute", "cr2c9_startdate"),
                        new XAttribute("operator", period))));
            AddClassificationColumns(entity);
            xml.Root?.Attribute("savedqueryid")?.Remove();
            var existing = Find("savedquery", "name", name, "returnedtypecode")
                .Where(x => x.GetAttributeValue<string>("returnedtypecode") == Request).ToArray();
            if (name == "All Leave Requests" && existing.Length == 0)
                existing = Find("savedquery", "name", "Leave Requests — Latest First", "returnedtypecode")
                    .Where(x => x.GetAttributeValue<string>("returnedtypecode") == Request).ToArray();
            if (existing.Length > 1) throw new Exception("Duplicate HR view: " + name);
            var view = new Entity("savedquery") {
                ["name"] = name, ["returnedtypecode"] = Request, ["querytype"] = 0,
                ["fetchxml"] = xml.ToString(SaveOptions.DisableFormatting),
                ["layoutxml"] = ClassificationLayout(source),
                ["description"] = upcoming ? "Upcoming approved leave, earliest start date first."
                    : status == null ? "All leave requests, newest submissions first."
                    : status == 0 ? "Pending leave requests, newest submissions first."
                    : period.Length == 0 ? "Approved leave, newest submissions first."
                    : "Approved leave beginning in the selected current calendar period; export the filtered grid to Excel."
            };
            var id = existing.Length == 0 ? Client.Create(view) : existing[0].Id;
            if (existing.Length == 1) { view.Id = id; Client.Update(view); }
            Add(id, 26);
            chosen.Add(new EntityReference("savedquery", id));
            var result = Client.RetrieveMultiple(new FetchExpression(view.GetAttributeValue<string>("fetchxml")));
            Console.WriteLine(name + ": " + result.Entities.Count + " accessible rows in Developer environment.");
        }
        Client.Execute(new AddAppComponentsRequest {
            AppId = HrApp, Components = new EntityReferenceCollection(chosen)
        });
        Client.Execute(new PublishXmlRequest { ParameterXml =
            "<importexportxml><appmodules><appmodule>" + HrApp +
            "</appmodule></appmodules></importexportxml>" });
        Console.WriteLine("HR approved-leave views published. Use the grid's date filters and Export to Excel command.");
    }

    static void HrReportResourceDeploy()
    {
        const string name = "cr2c9_hrLeaveReport.html";
        if (Meta(Request).EntitySetName != "cr2c9_leaverequests")
            throw new Exception("The report's Leave Requests API collection name differs from the live table.");
        var content = Convert.ToBase64String(File.ReadAllBytes(Root + "/" + name));
        var existing = Find("webresource", "name", name, "content");
        if (existing.Length > 1) throw new Exception("Duplicate HR report page.");
        Guid id;
        if (existing.Length == 0)
            id = Client.Create(new Entity("webresource") {
                ["name"] = name, ["displayname"] = "Approved leave report",
                ["webresourcetype"] = new OptionSetValue(1), ["content"] = content
            });
        else {
            id = existing[0].Id;
            Client.Update(new Entity("webresource", id) { ["content"] = content });
        }
        Add(id, 61);
        Client.Execute(new PublishXmlRequest { ParameterXml =
            "<importexportxml><webresources><webresource>" + id +
            "</webresource></webresources></importexportxml>" });
        Console.WriteLine("Published HR reporting page " + id + ". Add it to the HR app navigation as a web-resource page.");
    }
}
