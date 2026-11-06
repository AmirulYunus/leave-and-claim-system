using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;

internal static partial class Development
{
    const string CarryForwardCreditTable = "cr2c9_carryforwardcredit";

    // Storage only. The confirmation API and write guards must be deployed and
    // verified before any row can be created. No HR create/update permission.
    static void CarryForwardCreditSchema()
    {
        var known = ((RetrieveAllEntitiesResponse)Client.Execute(new RetrieveAllEntitiesRequest {
            EntityFilters = EntityFilters.Entity, RetrieveAsIfPublished = true
        })).EntityMetadata.SingleOrDefault(e => e.LogicalName == CarryForwardCreditTable);
        if (known == null) {
            var created = (CreateEntityResponse)Client.Execute(new CreateEntityRequest {
                SolutionUniqueName = Solution,
                Entity = new EntityMetadata {
                    SchemaName = CarryForwardCreditTable,
                    DisplayName = L("Annual Leave Carry-Forward Credit"),
                    DisplayCollectionName = L("Annual Leave Carry-Forward Credits"),
                    Description = L("Separate confirmed carry-forward bucket. Ordinary Annual Leave grants remain in the annual ledger."),
                    OwnershipType = OwnershipTypes.UserOwned, IsActivity = false
                },
                PrimaryAttribute = Text("cr2c9_name", "Credit Name", 120, true),
                HasActivities = false, HasNotes = false
            });
            Console.WriteLine("Created carry-forward credit table " + created.EntityId);
        } else if (known.OwnershipType != OwnershipTypes.UserOwned || known.IsActivity == true)
            throw new Exception("Existing carry-forward credit table has a different ownership contract.");

        void Lookup(string field, string label, string target)
        {
            var existing = Meta(CarryForwardCreditTable).Attributes.SingleOrDefault(a => a.LogicalName == field);
            if (existing != null) {
                if (existing is not LookupAttributeMetadata lookup || lookup.Targets.Length != 1 ||
                    lookup.Targets[0] != target)
                    throw new Exception("Existing carry-forward credit lookup differs: " + field);
                return;
            }
            Client.Execute(new CreateOneToManyRequest {
                SolutionUniqueName = Solution,
                Lookup = new LookupAttributeMetadata {
                    SchemaName = field, DisplayName = L(label),
                    RequiredLevel = new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.ApplicationRequired)
                },
                OneToManyRelationship = new OneToManyRelationshipMetadata {
                    SchemaName = CarryForwardCreditTable + "_" + field,
                    ReferencedEntity = target, ReferencingEntity = CarryForwardCreditTable,
                    CascadeConfiguration = new CascadeConfiguration {
                        Assign = CascadeType.NoCascade, Delete = CascadeType.Restrict,
                        Merge = CascadeType.NoCascade, Reparent = CascadeType.NoCascade,
                        Share = CascadeType.NoCascade, Unshare = CascadeType.NoCascade
                    }
                }
            });
            Console.WriteLine("Created " + field);
        }
        Lookup("cr2c9_employee", "Employee", Employee);
        Lookup("cr2c9_sourceperiod", "Source Annual Balance Period", BalancePeriodTable);
        Lookup("cr2c9_targetperiod", "Target Annual Balance Period", BalancePeriodTable);
        Lookup("cr2c9_review", "Confirmed HR Review", CarryForwardReviewTable);
        Attribute(CarryForwardCreditTable, new IntegerAttributeMetadata {
            SchemaName = "cr2c9_hours", DisplayName = L("Credited Carry-Forward Hours"),
            MinValue = 0, MaxValue = 100000, Format = IntegerFormat.None,
            RequiredLevel = new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.ApplicationRequired)
        });
        Attribute(CarryForwardCreditTable, Text("cr2c9_operationkey", "Carry-Forward Operation Key", 120, true));
        Attribute(CarryForwardCreditTable, Text("cr2c9_locktoken", "Server Carry-Forward Lock", 36));
        Attribute(CarryForwardCreditTable, new DateTimeAttributeMetadata {
            SchemaName = "cr2c9_useby", DisplayName = L("Use By"),
            Format = DateTimeFormat.DateOnly, DateTimeBehavior = DateTimeBehavior.DateOnly,
            RequiredLevel = new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.ApplicationRequired)
        });
        Attribute(CarryForwardCreditTable, Text("cr2c9_confirmedby", "Confirmed By Entra ID", 36));
        Attribute(CarryForwardCreditTable, new DateTimeAttributeMetadata {
            SchemaName = "cr2c9_confirmedon", DisplayName = L("Confirmed On"),
            Format = DateTimeFormat.DateAndTime, DateTimeBehavior = DateTimeBehavior.UserLocal
        });
        var metadata = ((RetrieveEntityResponse)Client.Execute(new RetrieveEntityRequest {
            LogicalName = CarryForwardCreditTable, EntityFilters = EntityFilters.All,
            RetrieveAsIfPublished = true
        })).EntityMetadata;
        if (metadata.IsOptimisticConcurrencyEnabled != true)
            throw new Exception("Carry-forward credit table requires row-version concurrency.");
        var key = metadata.Keys.SingleOrDefault(k => k.LogicalName == "cr2c9_uniquecarrycredit");
        if (key == null)
            Client.Execute(new CreateEntityKeyRequest {
                SolutionUniqueName = Solution, EntityName = CarryForwardCreditTable,
                EntityKey = new EntityKeyMetadata {
                    SchemaName = "cr2c9_uniquecarrycredit", DisplayName = L("One Credit Per Source Period"),
                    KeyAttributes = new[] { "cr2c9_sourceperiod" }
                }
            });
        else if (!key.KeyAttributes.SequenceEqual(new[] { "cr2c9_sourceperiod" }))
            throw new Exception("Carry-forward credit uniqueness key differs.");
        Client.Execute(new PublishXmlRequest { ParameterXml = "<importexportxml><entities><entity>" +
            CarryForwardCreditTable + "</entity></entities></importexportxml>" });
        Console.WriteLine("Published carry-forward credit storage only. No credits, server confirmation, app command, expiry or notifications were created.");
    }
}
