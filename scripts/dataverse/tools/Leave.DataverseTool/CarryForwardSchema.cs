using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Crm.Sdk.Messages;

internal static partial class Development
{
    const string CarryForwardReviewTable = "cr2c9_carryforwardreview";

    static void CarryForwardSchema()
    {
        var known = ((RetrieveAllEntitiesResponse)Client.Execute(new RetrieveAllEntitiesRequest {
            EntityFilters = EntityFilters.Entity, RetrieveAsIfPublished = true
        })).EntityMetadata.SingleOrDefault(e => e.LogicalName == CarryForwardReviewTable);
        if (known == null) {
            var created = (CreateEntityResponse)Client.Execute(new CreateEntityRequest {
                SolutionUniqueName = Solution,
                Entity = new EntityMetadata {
                    SchemaName = CarryForwardReviewTable,
                    DisplayName = L("Annual Leave Carry-Forward Review"),
                    DisplayCollectionName = L("Annual Leave Carry-Forward Reviews"),
                    Description = L("Year-end draft, HR review and eventual confirmation. No credit or expiry is posted by this table alone."),
                    OwnershipType = OwnershipTypes.UserOwned, IsActivity = false
                },
                PrimaryAttribute = Text("cr2c9_name", "Review Name", 120, true),
                HasActivities = false, HasNotes = true
            });
            Console.WriteLine("Created carry-forward review table " + created.EntityId);
        } else if (known.OwnershipType != OwnershipTypes.UserOwned || known.IsActivity == true)
            throw new Exception("Existing carry-forward review table has a different ownership contract.");

        void Lookup(string field, string label, string target)
        {
            var existing = Meta(CarryForwardReviewTable).Attributes.SingleOrDefault(a => a.LogicalName == field);
            if (existing != null) {
                if (existing is not LookupAttributeMetadata lookup || lookup.Targets.Length != 1 ||
                    lookup.Targets[0] != target)
                    throw new Exception("Existing carry-forward lookup differs: " + field);
                return;
            }
            Client.Execute(new CreateOneToManyRequest {
                SolutionUniqueName = Solution,
                Lookup = new LookupAttributeMetadata {
                    SchemaName = field, DisplayName = L(label),
                    RequiredLevel = new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.ApplicationRequired)
                },
                OneToManyRelationship = new OneToManyRelationshipMetadata {
                    SchemaName = CarryForwardReviewTable + "_" + field,
                    ReferencedEntity = target, ReferencingEntity = CarryForwardReviewTable,
                    CascadeConfiguration = new CascadeConfiguration {
                        Assign = CascadeType.NoCascade, Delete = CascadeType.Restrict,
                        Merge = CascadeType.NoCascade, Reparent = CascadeType.NoCascade,
                        Share = CascadeType.NoCascade, Unshare = CascadeType.NoCascade
                    }
                }
            });
            Console.WriteLine("Created " + field);
        }
        void Hours(string field, string label)
        {
            Attribute(CarryForwardReviewTable, new IntegerAttributeMetadata {
                SchemaName = field, DisplayName = L(label), MinValue = 0, MaxValue = 100000,
                Format = IntegerFormat.None,
                RequiredLevel = new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.None)
            });
        }

        Lookup("cr2c9_employee", "Employee", Employee);
        Lookup("cr2c9_sourceperiod", "Source Annual Balance Period", BalancePeriodTable);
        Lookup("cr2c9_targetperiod", "Target Annual Balance Period", BalancePeriodTable);
        Attribute(CarryForwardReviewTable, new PicklistAttributeMetadata {
            SchemaName = "cr2c9_reviewstatus", DisplayName = L("Review Status"),
            OptionSet = new OptionSetMetadata {
                IsGlobal = false, OptionSetType = OptionSetType.Picklist,
                Options = {
                    new OptionMetadata(L("Draft"), 290040000),
                    new OptionMetadata(L("Pending HR Review"), 290040001),
                    new OptionMetadata(L("Confirmed"), 290040002)
                }
            },
            RequiredLevel = new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.ApplicationRequired)
        });
        foreach (var pair in new[] {
            ("cr2c9_sourceunusedhours", "Original Unused Hours"),
            ("cr2c9_approvedlimithours", "Verified Carry-Forward Limit Hours"),
            ("cr2c9_originalcarryhours", "Original Calculated Carry-Forward Hours"),
            ("cr2c9_originalunusedhours", "Original Separate Unused Hours"),
            ("cr2c9_reviewedcarryhours", "HR Reviewed Carry-Forward Hours"),
            ("cr2c9_reviewedunusedhours", "HR Reviewed Separate Unused Hours"),
            ("cr2c9_speciallimithours", "Written Special Limit Hours")
        }) Hours(pair.Item1, pair.Item2);
        Attribute(CarryForwardReviewTable, new MemoAttributeMetadata {
            SchemaName = "cr2c9_adjustmentreason", DisplayName = L("HR Adjustment Reason"), MaxLength = 2000
        });
        Attribute(CarryForwardReviewTable, Text("cr2c9_approvaldocumentref", "Supporting Approval Document Reference", 200));
        Attribute(CarryForwardReviewTable, Text("cr2c9_specialverifiedby", "Special Limit Verified By Entra ID", 36));
        Attribute(CarryForwardReviewTable, new DateTimeAttributeMetadata {
            SchemaName = "cr2c9_specialverifiedon", DisplayName = L("Special Limit Verified On"),
            Format = DateTimeFormat.DateAndTime, DateTimeBehavior = DateTimeBehavior.UserLocal
        });
        Attribute(CarryForwardReviewTable, Text("cr2c9_confirmedby", "Confirmed By Entra ID", 36));
        Attribute(CarryForwardReviewTable, new DateTimeAttributeMetadata {
            SchemaName = "cr2c9_confirmedon", DisplayName = L("Confirmed On"),
            Format = DateTimeFormat.DateAndTime, DateTimeBehavior = DateTimeBehavior.UserLocal
        });
        Attribute(CarryForwardReviewTable, Text("cr2c9_operationkey", "Carry-Forward Operation Key", 200));
        Attribute(CarryForwardReviewTable, Text("cr2c9_sourcerowversion", "Source Ledger Snapshot Version", 40));
        Attribute(CarryForwardReviewTable, Text("cr2c9_sourceledgerhash", "Source Ledger Snapshot Hash", 64));
        Attribute(CarryForwardReviewTable, Text("cr2c9_locktoken", "Server Review Lock", 36));

        var metadata = ((RetrieveEntityResponse)Client.Execute(new RetrieveEntityRequest {
            LogicalName = CarryForwardReviewTable, EntityFilters = EntityFilters.All,
            RetrieveAsIfPublished = true
        })).EntityMetadata;
        if (metadata.IsOptimisticConcurrencyEnabled != true)
            throw new Exception("Carry-forward review table requires row-version concurrency.");
        var key = metadata.Keys.SingleOrDefault(k => k.LogicalName == "cr2c9_uniquecarrysource");
        if (key == null) {
            Client.Execute(new CreateEntityKeyRequest {
                SolutionUniqueName = Solution, EntityName = CarryForwardReviewTable,
                EntityKey = new EntityKeyMetadata {
                    SchemaName = "cr2c9_uniquecarrysource", DisplayName = L("One Review Per Source Period"),
                    KeyAttributes = new[] { "cr2c9_sourceperiod" }
                }
            });
        } else if (!key.KeyAttributes.SequenceEqual(new[] { "cr2c9_sourceperiod" }))
            throw new Exception("Carry-forward source-period uniqueness key differs.");
        Client.Execute(new PublishXmlRequest { ParameterXml = "<importexportxml><entities><entity>" +
            CarryForwardReviewTable + "</entity></entities></importexportxml>" });
        Console.WriteLine("Published carry-forward review schema only. No review records, HR permissions, confirmation, credits, expiry, flows or notifications were created.");
    }
}
