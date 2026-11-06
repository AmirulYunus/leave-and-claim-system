using System.Xml.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

internal static partial class Development
{
    static void SuperiorViewsInspect()
    {
        var response = (RetrieveAppComponentsResponse)Client.Execute(new RetrieveAppComponentsRequest {
            AppModuleId = SuperiorApp
        });
        foreach (var component in response.AppComponents.Entities)
            if (component.LogicalName == "appmodulecomponent" &&
                component.GetAttributeValue<OptionSetValue>("componenttype")?.Value == 26) {
                var id = component.GetAttributeValue<Guid>("objectid");
                var view = Client.Retrieve("savedquery", id,
                    new ColumnSet("name", "returnedtypecode"));
                if (new[] { Request, Cancellation }.Contains(view.GetAttributeValue<string>("returnedtypecode")))
                    Console.WriteLine("Superior app view " + id + " " +
                        view.GetAttributeValue<string>("name") + " for " +
                        view.GetAttributeValue<string>("returnedtypecode"));
            }
    }

    static void SuperiorViewsDeploy()
    {
        var selected = new List<EntityReference>();
        foreach (var pair in new[] {
                     (Request, "Active Leave Requests", "Summary Leave Request", "cr2c9_leaverequest", "cr2c9_leaverequestid", (int?)null, false),
                     (Request, "Active Leave Requests", "Pending Approval", "cr2c9_leaverequest", "cr2c9_leaverequestid", (int?)0, false),
                     (Request, "Active Leave Requests", "Approved Leave", "cr2c9_leaverequest", "cr2c9_leaverequestid", (int?)1, false),
                     (Request, "Active Leave Requests", "Upcoming Leave", "cr2c9_leaverequest", "cr2c9_leaverequestid", (int?)1, true),
                     (Cancellation, "Active Leave Cancellations", "Assigned pending cancellations", "cr2c9_leavecancellation", "cr2c9_leavecancellationid", (int?)0, false) }) {
            var source = Find("savedquery", "name", pair.Item2,
                "returnedtypecode", "querytype", "fetchxml", "layoutxml").Single(x =>
                x.GetAttributeValue<string>("returnedtypecode") == pair.Item1 &&
                x.GetAttributeValue<int?>("querytype") == 0);
            var xml = XDocument.Parse(source.GetAttributeValue<string>("fetchxml"));
            var entity = xml.Root?.Element("entity") ?? throw new Exception("Source view has no entity.");
            entity.Elements("order").Remove();
            entity.AddFirst(new XElement("order", new XAttribute("attribute", pair.Item7 ? "cr2c9_startdate" : "createdon"),
                new XAttribute("descending", pair.Item7 ? "false" : "true")));
            if (pair.Item6.HasValue)
                entity.Add(new XElement("filter", new XAttribute("type", "and"),
                    new XElement("condition", new XAttribute("attribute", "cr2c9_status"),
                        new XAttribute("operator", "eq"), new XAttribute("value", pair.Item6.Value))));
            if (pair.Item7)
                entity.Add(new XElement("filter", new XAttribute("type", "and"),
                    new XElement("condition", new XAttribute("attribute", "cr2c9_startdate"),
                        new XAttribute("operator", "next-x-days"), new XAttribute("value", "3650"))));
            if (pair.Item1 == Request) AddClassificationColumns(entity);
            entity.Add(new XElement("link-entity", new XAttribute("name", SuperiorGrant),
                new XAttribute("from", pair.Item4), new XAttribute("to", pair.Item5),
                new XAttribute("link-type", "inner"), new XAttribute("alias", "assignedgrant"),
                new XElement("filter", new XElement("condition",
                    new XAttribute("attribute", "cr2c9_grantee"),
                    new XAttribute("operator", "eq-userid")))));
            xml.Root?.Attribute("savedqueryid")?.Remove();
            var existing = Find("savedquery", "name", pair.Item3,
                "returnedtypecode").Where(x => x.GetAttributeValue<string>("returnedtypecode") == pair.Item1).ToArray();
            if (pair.Item3 == "Summary Leave Request" && existing.Length == 0)
                existing = Find("savedquery", "name", "Assigned pending leave requests",
                    "returnedtypecode").Where(x => x.GetAttributeValue<string>("returnedtypecode") == pair.Item1).ToArray();
            if (existing.Length > 1) throw new Exception("Duplicate assigned-staff view: " + pair.Item3);
            var view = new Entity("savedquery") {
                ["name"] = pair.Item3,
                ["returnedtypecode"] = pair.Item1,
                ["querytype"] = 0,
                ["fetchxml"] = xml.ToString(SaveOptions.DisableFormatting),
                ["layoutxml"] = pair.Item1 == Request ? ClassificationLayout(source) : source.GetAttributeValue<string>("layoutxml"),
                ["description"] = pair.Item7 ? "Upcoming approved leave for assigned staff; earliest start date first."
                    : pair.Item6 == 0 ? "Pending requests assigned to the signed-in Superior; newest submissions first."
                    : pair.Item6 == 1 ? "Approved requests for assigned staff; newest submissions first."
                    : "All requests for assigned staff; newest submissions first."
            };
            Guid id;
            if (existing.Length == 0) id = Client.Create(view);
            else { id = existing[0].Id; view.Id = id; Client.Update(view); }
            Add(id, 26);
            selected.Add(new EntityReference("savedquery", id));
            var verify = Client.RetrieveMultiple(new FetchExpression(view.GetAttributeValue<string>("fetchxml")));
            Console.WriteLine(pair.Item3 + " query verified; current developer sees " + verify.Entities.Count + " records.");
        }
        var components = ((RetrieveAppComponentsResponse)Client.Execute(new RetrieveAppComponentsRequest {
            AppModuleId = SuperiorApp
        })).AppComponents.Entities.Where(x => x.LogicalName == "appmodulecomponent" &&
            x.GetAttributeValue<OptionSetValue>("componenttype")?.Value == 26).ToArray();
        var old = components.Where(x => {
            var id = x.GetAttributeValue<Guid>("objectid");
            if (selected.Any(s => s.Id == id)) return false;
            var view = Client.Retrieve("savedquery", id, new ColumnSet("returnedtypecode"));
            return new[] { Request, Cancellation }.Contains(view.GetAttributeValue<string>("returnedtypecode"));
        }).ToArray();
        if (old.Length != 0) Client.Execute(new RemoveAppComponentsRequest {
            AppId = SuperiorApp, Components = new EntityReferenceCollection(old.Select(x =>
                new EntityReference("savedquery", x.GetAttributeValue<Guid>("objectid"))).ToArray())
        });
        Client.Execute(new AddAppComponentsRequest {
            AppId = SuperiorApp, Components = new EntityReferenceCollection(selected)
        });
        Client.Execute(new PublishXmlRequest { ParameterXml =
            "<importexportxml><appmodules><appmodule>" + SuperiorApp +
            "</appmodule></appmodules></importexportxml>" });
        var published = ((RetrieveAppComponentsResponse)Client.Execute(new RetrieveAppComponentsRequest {
            AppModuleId = SuperiorApp
        })).AppComponents.Entities.Where(x => x.LogicalName == "appmodulecomponent" &&
            x.GetAttributeValue<OptionSetValue>("componenttype")?.Value == 26)
            .Select(x => x.GetAttributeValue<Guid>("objectid"))
            .Where(id => new[] { Request, Cancellation }.Contains(Client.Retrieve("savedquery", id,
                new ColumnSet("returnedtypecode")).GetAttributeValue<string>("returnedtypecode")))
            .ToArray();
        if (selected.Any(x => !published.Contains(x.Id)) ||
            published.Any(x => !selected.Any(s => s.Id == x)))
            throw new Exception("Superior app does not contain exactly the selected assigned-staff views.");
        SuperiorViewsInspect();
        Console.WriteLine("Published summary, pending, approved, upcoming and cancellation views in Superior app. Ordinary-role verification still required.");
    }
}
