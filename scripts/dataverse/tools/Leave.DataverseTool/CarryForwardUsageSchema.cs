using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;

internal static partial class Development
{
    const string CarryForwardUsageTable = "cr2c9_carryforwardusage";

    // Immutable debit/reversal storage. The guard is deployed before any
    // approval path is allowed to write to it; no direct HR write privilege.
    static void CarryForwardUsageSchema()
    {
        var known = ((RetrieveAllEntitiesResponse)Client.Execute(new RetrieveAllEntitiesRequest {
            EntityFilters = EntityFilters.Entity, RetrieveAsIfPublished = true
        })).EntityMetadata.SingleOrDefault(e => e.LogicalName == CarryForwardUsageTable);
        if (known == null) {
            Client.Execute(new CreateEntityRequest {
                SolutionUniqueName = Solution,
                Entity = new EntityMetadata {
                    SchemaName = CarryForwardUsageTable,
                    DisplayName = L("Annual Leave Carry-Forward Usage"),
                    DisplayCollectionName = L("Annual Leave Carry-Forward Usage Entries"),
                    Description = L("Immutable approval debits and exact cancellation reversals against a confirmed carry-forward credit."),
                    OwnershipType = OwnershipTypes.UserOwned, IsActivity = false
                },
                PrimaryAttribute = Text("cr2c9_name", "Usage Name", 120, true),
                HasActivities = false, HasNotes = false
            });
            Console.WriteLine("Created carry-forward usage table.");
        } else if (known.OwnershipType != OwnershipTypes.UserOwned || known.IsActivity == true)
            throw new Exception("Existing carry-forward usage table has a different ownership contract.");
        void Lookup(string field, string label, string target, bool required)
        {
            var existing = Meta(CarryForwardUsageTable).Attributes.SingleOrDefault(a => a.LogicalName == field);
            if (existing != null) {
                if (existing is not LookupAttributeMetadata lookup || lookup.Targets.Length != 1 || lookup.Targets[0] != target)
                    throw new Exception("Existing carry-forward usage lookup differs: " + field);
                return;
            }
            Client.Execute(new CreateOneToManyRequest {
                SolutionUniqueName = Solution,
                Lookup = new LookupAttributeMetadata {
                    SchemaName = field, DisplayName = L(label),
                    RequiredLevel = new AttributeRequiredLevelManagedProperty(required ? AttributeRequiredLevel.ApplicationRequired : AttributeRequiredLevel.None)
                },
                OneToManyRelationship = new OneToManyRelationshipMetadata {
                    SchemaName = CarryForwardUsageTable + "_" + field,
                    ReferencedEntity = target, ReferencingEntity = CarryForwardUsageTable,
                    CascadeConfiguration = new CascadeConfiguration {
                        Assign = CascadeType.NoCascade, Delete = CascadeType.Restrict,
                        Merge = CascadeType.NoCascade, Reparent = CascadeType.NoCascade,
                        Share = CascadeType.NoCascade, Unshare = CascadeType.NoCascade
                    }
                }
            });
            Console.WriteLine("Created " + field);
        }
        Lookup("cr2c9_credit", "Confirmed Carry-Forward Credit", CarryForwardCreditTable, true);
        Lookup("cr2c9_leaverequest", "Approved Leave Request", Request, true);
        Lookup("cr2c9_cancellation", "Approved Cancellation", "cr2c9_leavecancellation", false);
        Lookup("cr2c9_reverses", "Reversed Usage Entry", CarryForwardUsageTable, false);
        Attribute(CarryForwardUsageTable, new IntegerAttributeMetadata {
            SchemaName = "cr2c9_hours", DisplayName = L("Hours"),
            MinValue = 4, MaxValue = 100000, Format = IntegerFormat.None,
            RequiredLevel = new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.ApplicationRequired)
        });
        Attribute(CarryForwardUsageTable, Text("cr2c9_operationkey", "Usage Operation Key", 120, true));
        Attribute(CarryForwardUsageTable, new PicklistAttributeMetadata {
            SchemaName = "cr2c9_entrykind", DisplayName = L("Entry Kind"),
            RequiredLevel = new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.ApplicationRequired),
            OptionSet = new OptionSetMetadata {
                IsGlobal = false,
                Options = {
                    new OptionMetadata(L("Approval debit"), 290040000),
                    new OptionMetadata(L("Cancellation reversal"), 290040001)
                }
            }
        });
        var metadata = ((RetrieveEntityResponse)Client.Execute(new RetrieveEntityRequest {
            LogicalName = CarryForwardUsageTable, EntityFilters = EntityFilters.All,
            RetrieveAsIfPublished = true
        })).EntityMetadata;
        if (metadata.IsOptimisticConcurrencyEnabled != true)
            throw new Exception("Carry-forward usage requires row-version concurrency.");
        var key = metadata.Keys.SingleOrDefault(k => k.LogicalName == "cr2c9_uniquecarryusage");
        if (key == null)
            Client.Execute(new CreateEntityKeyRequest {
                SolutionUniqueName = Solution, EntityName = CarryForwardUsageTable,
                EntityKey = new EntityKeyMetadata {
                    SchemaName = "cr2c9_uniquecarryusage", DisplayName = L("Unique Carry-Forward Operation"),
                    KeyAttributes = new[] { "cr2c9_operationkey" }
                }
            });
        else if (!key.KeyAttributes.SequenceEqual(new[] { "cr2c9_operationkey" }))
            throw new Exception("Carry-forward usage uniqueness key differs.");
        Client.Execute(new PublishXmlRequest { ParameterXml = "<importexportxml><entities><entity>" +
            CarryForwardUsageTable + "</entity></entities></importexportxml>" });
        Console.WriteLine("Published carry-forward usage storage only. No usage or reversal is active.");
    }
}
