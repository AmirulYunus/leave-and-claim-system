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
    const string DepartmentTable = "cr2c9_department";
    const string CostCentreTable = "cr2c9_costcentre";
    const string DepartmentField = "cr2c9_department";
    const string CostCentreField = "cr2c9_costcentre";

    static void AddClassificationColumns(XElement entity)
    {
        foreach (var field in new[] { DepartmentField, CostCentreField })
            if (!entity.Elements("attribute").Any(x => (string?)x.Attribute("name") == field))
                entity.Add(new XElement("attribute", new XAttribute("name", field)));
    }

    static string ClassificationLayout(Entity source)
    {
        var xml = XDocument.Parse(source.GetAttributeValue<string>("layoutxml"));
        var row = xml.Descendants("row").Single();
        foreach (var (field, width) in new[] { (DepartmentField, 150), (CostCentreField, 145) })
            if (!row.Elements("cell").Any(x => (string?)x.Attribute("name") == field))
                row.Add(new XElement("cell", new XAttribute("name", field), new XAttribute("width", width)));
        return xml.ToString(SaveOptions.DisableFormatting);
    }

    static void ClassificationSchemaDeploy()
    {
        foreach (var (table, display, plural, primary) in new[] {
            (DepartmentTable, "Department", "Departments", "cr2c9_name"),
            (CostCentreTable, "Cost Centre", "Cost Centres", "cr2c9_name")
        }) {
            var prior = ((RetrieveAllEntitiesResponse)Client.Execute(new RetrieveAllEntitiesRequest {
                EntityFilters = EntityFilters.Entity, RetrieveAsIfPublished = true
            })).EntityMetadata.SingleOrDefault(x => x.LogicalName == table);
            if (prior == null) {
                Client.Execute(new CreateEntityRequest {
                    SolutionUniqueName = Solution,
                    Entity = new EntityMetadata { SchemaName = table,
                        DisplayName = L(display), DisplayCollectionName = L(plural),
                        Description = L("HR-maintained options for leave request classification."),
                        OwnershipType = OwnershipTypes.OrganizationOwned, IsActivity = false },
                    PrimaryAttribute = Text(primary, display + " Name", 150, true),
                    HasActivities = false, HasNotes = false
                });
            } else if (prior.OwnershipType != OwnershipTypes.OrganizationOwned)
                throw new Exception(table + " must be organization-owned.");
        }
        foreach (var (table, child, field, relation, label) in new[] {
            (DepartmentTable, Request, DepartmentField, "cr2c9_department_leaverequests", "Department"),
            (CostCentreTable, Request, CostCentreField, "cr2c9_costcentre_leaverequests", "Cost Centre"),
            (DepartmentTable, PublicAbsenceTable, DepartmentField, "cr2c9_department_publicabsences", "Department"),
            (CostCentreTable, PublicAbsenceTable, CostCentreField, "cr2c9_costcentre_publicabsences", "Cost Centre")
        }) {
            var existing = Meta(child).Attributes.SingleOrDefault(x => x.LogicalName == field);
            if (existing == null)
                Client.Execute(new CreateOneToManyRequest {
                    SolutionUniqueName = Solution,
                    Lookup = new LookupAttributeMetadata { SchemaName = field, DisplayName = L(label),
                        RequiredLevel = new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.None) },
                    OneToManyRelationship = new OneToManyRelationshipMetadata {
                        SchemaName = relation, ReferencedEntity = table, ReferencingEntity = child,
                        CascadeConfiguration = new CascadeConfiguration {
                            Assign = CascadeType.NoCascade, Delete = CascadeType.Restrict,
                            Merge = CascadeType.NoCascade, Reparent = CascadeType.NoCascade,
                            Share = CascadeType.NoCascade, Unshare = CascadeType.NoCascade
                        }
                    }
                });
            else if (existing is not LookupAttributeMetadata lookup ||
                     lookup.Targets?.SingleOrDefault() != table)
                throw new Exception(field + " has an incompatible target.");
        }
        Client.Execute(new PublishXmlRequest { ParameterXml =
            "<importexportxml><entities><entity>" + DepartmentTable + "</entity><entity>" +
            CostCentreTable + "</entity><entity>" + Request + "</entity><entity>" +
            PublicAbsenceTable + "</entity></entities></importexportxml>" });
        Console.WriteLine("Published HR-configurable Department and Cost Centre tables and request lookups.");
    }

    static void ClassificationRolesDeploy()
    {
        foreach (var table in new[] { DepartmentTable, CostCentreTable }) {
            var meta = ((RetrieveEntityResponse)Client.Execute(new RetrieveEntityRequest {
                LogicalName = table, EntityFilters = EntityFilters.Entity | EntityFilters.Privileges
            })).EntityMetadata;
            if (meta.OwnershipType != OwnershipTypes.OrganizationOwned)
                throw new Exception("Classification table must be organization-owned: " + table);
            var privilege = meta.Privileges.ToDictionary(x => x.PrivilegeType, x => x.PrivilegeId);
            foreach (var roleName in new[] { "Leave Calendar Reader", "Leave Employee (development test)", "Leave Superior", "Leave HR" }) {
                var roles = Find("role", "name", roleName, "businessunitid");
                if (roles.Length == 0) throw new Exception("Missing leave role: " + roleName);
                foreach (var role in roles) {
                    var before = ((RetrieveRolePrivilegesRoleResponse)Client.Execute(
                        new RetrieveRolePrivilegesRoleRequest { RoleId = role.Id })).RolePrivileges;
                    var wanted = roleName == "Leave HR"
                        ? new[] { PrivilegeType.Read, PrivilegeType.AppendTo, PrivilegeType.Create, PrivilegeType.Write, PrivilegeType.Delete }
                        : new[] { PrivilegeType.Read, PrivilegeType.AppendTo };
                    var missing = wanted.Where(type => !before.Any(p => p.PrivilegeId == privilege[type] &&
                        p.Depth == PrivilegeDepth.Global)).Select(type => new RolePrivilege((int)PrivilegeDepth.Global, privilege[type])).ToArray();
                    if (missing.Length > 0)
                        Client.Execute(new AddPrivilegesRoleRequest { RoleId = role.Id, Privileges = missing });
                    var after = ((RetrieveRolePrivilegesRoleResponse)Client.Execute(
                        new RetrieveRolePrivilegesRoleRequest { RoleId = role.Id })).RolePrivileges;
                    if (wanted.Any(type => !after.Any(p => p.PrivilegeId == privilege[type] &&
                        p.Depth == PrivilegeDepth.Global)))
                        throw new Exception("Classification role privilege failed: " + roleName + "/" + table);
                }
            }
        }
        Client.Execute(new AddAppComponentsRequest { AppId = HrApp,
            Components = new EntityReferenceCollection(new[] {
                new EntityReference("entity", Meta(DepartmentTable).MetadataId!.Value),
                new EntityReference("entity", Meta(CostCentreTable).MetadataId!.Value)
            }) });
        Client.Execute(new PublishXmlRequest { ParameterXml =
            "<importexportxml><appmodules><appmodule>" + HrApp + "</appmodule></appmodules></importexportxml>" });
        Console.WriteLine("Verified classification read access for employees and Superiors; HR can maintain both option tables.");
    }

    static void ClassificationReviewFormDeploy()
    {
        // The Superior form is a review surface; employees make their selections
        // in the Canvas request form. Reviewers see the saved choices read-only.
        var formId = Guid.Parse("f297bf4c-1a1d-4560-b594-572612a10d5c");
        var form = Client.Retrieve("systemform", formId, new ColumnSet("formxml"));
        var xml = XDocument.Parse(form.GetAttributeValue<string>("formxml"));
        var start = xml.Descendants("control").Single(x =>
            (string?)x.Attribute("datafieldname") == "cr2c9_startdate");
        var anchor = start.Ancestors("row").First();
        var changed = false;
        foreach (var (field, label) in new[] {
            (DepartmentField, "Department"), (CostCentreField, "Cost Centre")
        }) {
            var controls = xml.Descendants("control").Where(x =>
                (string?)x.Attribute("datafieldname") == field).ToArray();
            if (controls.Length > 1) throw new Exception("Duplicate review field: " + field);
            if (controls.Length != 0) continue;
            var cell = new XElement(start.Ancestors("cell").First());
            foreach (var node in cell.DescendantsAndSelf().Where(x => x.Attribute("id") != null))
                node.SetAttributeValue("id", "{" + Guid.NewGuid().ToString("D") + "}");
            var control = cell.Descendants("control").Single();
            control.SetAttributeValue("id", field);
            control.SetAttributeValue("datafieldname", field);
            control.SetAttributeValue("disabled", "true");
            foreach (var node in cell.Descendants("label")) node.SetAttributeValue("description", label);
            var row = new XElement("row", cell);
            anchor.AddAfterSelf(row);
            anchor = row;
            changed = true;
        }
        if (changed) Client.Update(new Entity("systemform", formId) {
            ["formxml"] = xml.ToString(SaveOptions.DisableFormatting)
        });
        Add(formId, 60);
        Client.Execute(new PublishXmlRequest { ParameterXml =
            "<importexportxml><entities><entity>" + Request + "</entity></entities></importexportxml>" });
        Console.WriteLine("Department and Cost Centre are visible read-only on the Superior review form.");
    }

    static void ClassificationServerDeploy()
    {
        foreach (var table in new[] { Request, PublicAbsenceTable })
            foreach (var field in new[] { DepartmentField, CostCentreField })
                if (Meta(table).Attributes.SingleOrDefault(x => x.LogicalName == field) is not LookupAttributeMetadata)
                    throw new Exception("Publish classification lookup before server deployment: " + table + "." + field);
        const string name = "Leave.Plugins";
        var path = Root + "/server/Leave.Plugins/bin/Release/net462/Leave.Plugins.dll";
        var assemblyName = AssemblyName.GetAssemblyName(path);
        var registered = Find("pluginassembly", "name", name, "publickeytoken", "content").Single();
        var token = Convert.ToHexString(assemblyName.GetPublicKeyToken()!).ToLowerInvariant();
        if (registered.GetAttributeValue<string>("publickeytoken") != token)
            throw new Exception("Leave plug-in signing identity differs.");
        var bytes = File.ReadAllBytes(path);
        Client.Update(new Entity("pluginassembly", registered.Id) { ["content"] = Convert.ToBase64String(bytes) });
        var saved = Client.Retrieve("pluginassembly", registered.Id, new ColumnSet("content"));
        if (!SHA256.HashData(Convert.FromBase64String(saved.GetAttributeValue<string>("content")))
                .SequenceEqual(SHA256.HashData(bytes)))
            throw new Exception("Leave plug-in deployment readback differs.");
        Console.WriteLine("Published leave plug-in with pending-edit classification and approved-calendar projection.");
    }

    static void ClassificationCalendarBackfill()
    {
        var rows = Client.RetrieveMultiple(new QueryExpression(PublicAbsenceTable) {
            ColumnSet = new ColumnSet(DepartmentField, CostCentreField)
        }).Entities;
        var updated = 0;
        foreach (var row in rows) {
            var request = Client.Retrieve(Request, row.Id,
                new ColumnSet("cr2c9_status", DepartmentField, CostCentreField));
            if (request.GetAttributeValue<OptionSetValue>("cr2c9_status")?.Value != 1)
                throw new Exception("Public absence is not approved: " + row.Id);
            var change = new Entity(PublicAbsenceTable, row.Id);
            foreach (var field in new[] { DepartmentField, CostCentreField }) {
                var selected = request.GetAttributeValue<EntityReference>(field);
                var current = row.GetAttributeValue<EntityReference>(field);
                if (selected?.Id != current?.Id) change[field] = selected;
            }
            if (change.Attributes.Count == 0) continue;
            Client.Update(change);
            updated++;
        }
        Console.WriteLine("Verified " + rows.Count + " public absences; refreshed classification on " + updated + ".");
    }

    static void RoleDashboardResourceDeploy()
    {
        const string name = "cr2c9_roleLeaveDashboard.html";
        var content = Convert.ToBase64String(File.ReadAllBytes(Root + "/" + name));
        var existing = Find("webresource", "name", name, "content");
        if (existing.Length > 1) throw new Exception("Duplicate role dashboard web resource.");
        Guid id;
        if (existing.Length == 0)
            id = Client.Create(new Entity("webresource") {
                ["name"] = name, ["displayname"] = "Role-based leave dashboard",
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
        Console.WriteLine("Published role-based dashboard resource " + id + ". Add it as a page in both app designers.");
    }
}
