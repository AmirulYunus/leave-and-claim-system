using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Crm.Sdk.Messages;

internal static partial class Development
{
    const string SuperiorGrant = "cr2c9_superioraccessgrant";

    static void SuperiorAccessSchema()
    {
        var all = ((RetrieveAllEntitiesResponse)Client.Execute(new RetrieveAllEntitiesRequest {
            EntityFilters = EntityFilters.Entity, RetrieveAsIfPublished = true
        })).EntityMetadata;
        var existing = all.SingleOrDefault(x => x.LogicalName == SuperiorGrant);
        if (existing == null) {
            Client.Execute(new CreateEntityRequest {
                SolutionUniqueName = Solution,
                Entity = new EntityMetadata {
                    SchemaName = SuperiorGrant, DisplayName = L("Superior Access Grant"),
                    DisplayCollectionName = L("Superior Access Grants"),
                    Description = L("System-owned provenance for assigned Superior record shares; not an HR editing surface."),
                    OwnershipType = OwnershipTypes.UserOwned, IsActivity = false
                },
                PrimaryAttribute = Text("cr2c9_name", "Grant Target", 200, true),
                HasActivities = false, HasNotes = false
            });
        } else if (existing.OwnershipType != OwnershipTypes.UserOwned)
            throw new Exception("Superior grant table ownership differs.");

        void Lookup(string target, string field, string label)
        {
            if (Meta(SuperiorGrant).Attributes.Any(a => a.LogicalName == field)) return;
            Client.Execute(new CreateOneToManyRequest {
                SolutionUniqueName = Solution,
                Lookup = new LookupAttributeMetadata {
                    SchemaName = field, DisplayName = L(label),
                    RequiredLevel = new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.None)
                },
                OneToManyRelationship = new OneToManyRelationshipMetadata {
                    SchemaName = "cr2c9_superiorgrant_" + target,
                    ReferencedEntity = target, ReferencingEntity = SuperiorGrant,
                    CascadeConfiguration = new CascadeConfiguration {
                        Assign = CascadeType.NoCascade,
                        Delete = target == Request ? CascadeType.Cascade : CascadeType.RemoveLink,
                        Merge = CascadeType.NoCascade, Reparent = CascadeType.NoCascade,
                        Share = CascadeType.NoCascade, Unshare = CascadeType.NoCascade
                    }
                }
            });
        }
        Lookup("systemuser", "cr2c9_grantee", "Superior User");
        Lookup(Request, "cr2c9_leaverequest", "Leave Request");
        Lookup("cr2c9_leaverequestdate", "cr2c9_leaverequestdate", "Leave Request Date");
        Lookup(History, "cr2c9_leavedecision", "Leave Decision");
        Lookup(Cancellation, "cr2c9_leavecancellation", "Leave Cancellation");
        Client.Execute(new PublishXmlRequest {
            ParameterXml = "<importexportxml><entities><entity>" + SuperiorGrant + "</entity></entities></importexportxml>"
        });
        Console.WriteLine("Superior grant provenance schema is ready. No sharing steps are active.");
    }
}
