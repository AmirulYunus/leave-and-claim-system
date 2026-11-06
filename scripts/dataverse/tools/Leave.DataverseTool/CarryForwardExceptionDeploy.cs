using System.Reflection;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

internal static partial class Development
{
    const string CarryForwardExceptionTable = "cr2c9_carryforwardexception";
    const string CarryForwardExceptionApi = "cr2c9_RecordAnnualCarryForwardException";

    static void CarryForwardExceptionDeploy()
    {
        var known = ((RetrieveAllEntitiesResponse)Client.Execute(new RetrieveAllEntitiesRequest {
            EntityFilters = EntityFilters.Entity, RetrieveAsIfPublished = true
        })).EntityMetadata.SingleOrDefault(e => e.LogicalName == CarryForwardExceptionTable);
        if (known == null) Client.Execute(new CreateEntityRequest {
            SolutionUniqueName = Solution,
            Entity = new EntityMetadata {
                SchemaName = CarryForwardExceptionTable,
                DisplayName = L("Written Carry-Forward Exception"),
                DisplayCollectionName = L("Written Carry-Forward Exceptions"),
                Description = L("Immutable HR verification of a written immediate-Superior approval received by 15 December."),
                OwnershipType = OwnershipTypes.UserOwned, IsActivity = false
            },
            PrimaryAttribute = Text("cr2c9_name", "Exception Name", 120, true),
            HasActivities = false, HasNotes = true
        });
        else if (known.OwnershipType != OwnershipTypes.UserOwned || known.IsActivity == true)
            throw new Exception("Written exception table has the wrong ownership contract.");

        void Lookup(string field, string label, string referenced)
        {
            var saved = Meta(CarryForwardExceptionTable).Attributes.SingleOrDefault(a => a.LogicalName == field);
            if (saved != null) {
                if (saved is not LookupAttributeMetadata lookup || lookup.Targets.Length != 1 ||
                    lookup.Targets[0] != referenced)
                    throw new Exception("Written exception lookup differs: " + field);
                return;
            }
            Client.Execute(new CreateOneToManyRequest {
                SolutionUniqueName = Solution,
                Lookup = new LookupAttributeMetadata {
                    SchemaName = field, DisplayName = L(label),
                    RequiredLevel = new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.ApplicationRequired)
                },
                OneToManyRelationship = new OneToManyRelationshipMetadata {
                    SchemaName = CarryForwardExceptionTable + "_" + field,
                    ReferencedEntity = referenced, ReferencingEntity = CarryForwardExceptionTable,
                    CascadeConfiguration = new CascadeConfiguration {
                        Assign = CascadeType.NoCascade, Delete = CascadeType.Restrict,
                        Merge = CascadeType.NoCascade, Reparent = CascadeType.NoCascade,
                        Share = CascadeType.NoCascade, Unshare = CascadeType.NoCascade
                    }
                }
            });
        }
        Lookup("cr2c9_employee", "Employee", Employee);
        Lookup("cr2c9_sourceperiod", "Closing-Year Annual Balance", BalancePeriodTable);
        Attribute(CarryForwardExceptionTable, new IntegerAttributeMetadata {
            SchemaName = "cr2c9_approvedlimithours", DisplayName = L("Superior-Approved Limit Hours"),
            MinValue = 44, MaxValue = 1000, Format = IntegerFormat.None,
            RequiredLevel = new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.ApplicationRequired)
        });
        Attribute(CarryForwardExceptionTable, Text("cr2c9_documentreference", "Written Approval Document Reference", 200));
        Attribute(CarryForwardExceptionTable, Text("cr2c9_superiorentraid", "Immediate Superior Entra ID", 36));
        Attribute(CarryForwardExceptionTable, Text("cr2c9_verifiedby", "HR Verifier Entra ID", 36));
        Attribute(CarryForwardExceptionTable, new DateTimeAttributeMetadata {
            SchemaName = "cr2c9_verifiedon", DisplayName = L("HR Verified On"),
            Format = DateTimeFormat.DateAndTime, DateTimeBehavior = DateTimeBehavior.UserLocal
        });
        var metadata = ((RetrieveEntityResponse)Client.Execute(new RetrieveEntityRequest {
            LogicalName = CarryForwardExceptionTable, EntityFilters = EntityFilters.All,
            RetrieveAsIfPublished = true
        })).EntityMetadata;
        if (metadata.IsOptimisticConcurrencyEnabled != true)
            throw new Exception("Written exception table needs row-version concurrency.");
        var key = metadata.Keys.SingleOrDefault(k => k.LogicalName == "cr2c9_uniqueexceptionsource");
        if (key == null) Client.Execute(new CreateEntityKeyRequest {
            SolutionUniqueName = Solution, EntityName = CarryForwardExceptionTable,
            EntityKey = new EntityKeyMetadata {
                SchemaName = "cr2c9_uniqueexceptionsource",
                DisplayName = L("One Written Exception Per Closing Year"),
                KeyAttributes = new[] { "cr2c9_sourceperiod" }
            }
        });
        else if (!key.KeyAttributes.SequenceEqual(new[] { "cr2c9_sourceperiod" }))
            throw new Exception("Written exception uniqueness key differs.");
        Client.Execute(new PublishXmlRequest { ParameterXml = "<importexportxml><entities><entity>" +
            CarryForwardExceptionTable + "</entity></entities></importexportxml>" });
        var activeKey = ((RetrieveEntityResponse)Client.Execute(new RetrieveEntityRequest {
            LogicalName = CarryForwardExceptionTable, EntityFilters = EntityFilters.All,
            RetrieveAsIfPublished = true
        })).EntityMetadata.Keys.Single(k => k.LogicalName == "cr2c9_uniqueexceptionsource");
        if (activeKey.EntityKeyIndexStatus != EntityKeyIndexStatus.Active)
            throw new Exception("The unique written-exception key is still building. Rerun deployment after it becomes active.");

        // The same exact early-stage guard protects Create, Update and Delete.
        var assembly = Find("pluginassembly", "name", "Leave.Plugins", "publickeytoken").Single();
        var path = Root + "/server/Leave.Plugins/bin/Release/net462/Leave.Plugins.dll";
        var identity = AssemblyName.GetAssemblyName(path);
        if (assembly.GetAttributeValue<string>("publickeytoken") !=
            Convert.ToHexString(identity.GetPublicKeyToken()!).ToLowerInvariant())
            throw new Exception("Signed Leave.Plugins assembly differs.");
        Client.Update(new Entity("pluginassembly", assembly.Id) {
            ["content"] = Convert.ToBase64String(File.ReadAllBytes(path))
        });
        Add(assembly.Id, 91);
        var guard = Find("plugintype", "typename", "Leave.Plugins.CarryForwardWriteGuard").Single();
        foreach (var message in new[] { "Create", "Update", "Delete", "Assign", "SetState", "CreateMultiple", "UpdateMultiple" }) {
            var name = "Leave carry-forward write safeguard: " + CarryForwardExceptionTable + " " + message;
            if (Find("sdkmessageprocessingstep", "name", name).Length != 0) continue;
            var messages = Find("sdkmessage", "name", message);
            if (messages.Length == 0) {
                if (message is "Create" or "Update" or "Delete") throw new Exception("Mandatory write message is missing: " + message);
                continue;
            }
            var sdkMessage = messages.Single();
            var filterQuery = new QueryExpression("sdkmessagefilter") { ColumnSet = new ColumnSet("iscustomprocessingstepallowed") };
            filterQuery.Criteria.AddCondition("sdkmessageid", ConditionOperator.Equal, sdkMessage.Id);
            filterQuery.Criteria.AddCondition("primaryobjecttypecode", ConditionOperator.Equal, CarryForwardExceptionTable);
            var filters = Client.RetrieveMultiple(filterQuery).Entities;
            if (filters.Count != 1 || !filters[0].GetAttributeValue<bool>("iscustomprocessingstepallowed")) {
                if (message is "Create" or "Update" or "Delete") throw new Exception("Mandatory write filter is missing: " + message);
                continue;
            }
            var filter = filters.Single();
            var stepId = Client.Create(new Entity("sdkmessageprocessingstep") {
                ["name"] = name, ["stage"] = new OptionSetValue(20),
                ["mode"] = new OptionSetValue(0), ["rank"] = 1,
                ["sdkmessageid"] = sdkMessage.ToEntityReference(),
                ["sdkmessagefilterid"] = filter.ToEntityReference(),
                ["eventhandler"] = guard.ToEntityReference(),
                ["supporteddeployment"] = new OptionSetValue(0),
                ["statecode"] = new OptionSetValue(0), ["statuscode"] = new OptionSetValue(1)
            });
            Add(stepId, 92);
        }
        var balanceAssembly = Find("pluginassembly", "name", "Leave.BalanceGuards", "publickeytoken").Single();
        var balancePath = Root + "/server/Leave.BalanceGuards/bin/Release/net462/Leave.BalanceGuards.dll";
        var balanceIdentity = AssemblyName.GetAssemblyName(balancePath);
        if (balanceAssembly.GetAttributeValue<string>("publickeytoken") !=
            Convert.ToHexString(balanceIdentity.GetPublicKeyToken()!).ToLowerInvariant())
            throw new Exception("Signed Leave.BalanceGuards assembly differs.");
        Client.Update(new Entity("pluginassembly", balanceAssembly.Id) {
            ["content"] = Convert.ToBase64String(File.ReadAllBytes(balancePath))
        });
        Add(balanceAssembly.Id, 91);
        const string typeName = "Leave.BalanceGuards.CarryForwardExceptionOperation";
        var types = Find("plugintype", "typename", typeName);
        if (types.Length > 1) throw new Exception("Duplicate written exception plugin types.");
        var typeId = types.Length == 1 ? types[0].Id : Client.Create(new Entity("plugintype") {
            ["pluginassemblyid"] = balanceAssembly.ToEntityReference(),
            ["typename"] = typeName, ["name"] = typeName, ["friendlyname"] = typeName
        });
        var readPrivilege = ((RetrieveEntityResponse)Client.Execute(new RetrieveEntityRequest {
            LogicalName = Employee, EntityFilters = EntityFilters.Privileges
        })).EntityMetadata.Privileges.Single(p => p.PrivilegeType == PrivilegeType.Read).Name;
        var apis = Find("customapi", "uniquename", CarryForwardExceptionApi,
            "bindingtype", "boundentitylogicalname", "plugintypeid");
        if (apis.Length > 1) throw new Exception("Duplicate written exception APIs.");
        var apiId = apis.Length == 1 ? apis[0].Id : CreateInSolution(new Entity("customapi") {
            ["uniquename"] = CarryForwardExceptionApi, ["name"] = CarryForwardExceptionApi,
            ["displayname"] = "Record written Annual Leave carry-forward exception",
            ["description"] = "HR-only verified written immediate-Superior approval, through 15 December.",
            ["isfunction"] = false, ["isprivate"] = false,
            ["bindingtype"] = new OptionSetValue(1), ["boundentitylogicalname"] = Employee,
            ["allowedcustomprocessingsteptype"] = new OptionSetValue(0),
            ["workflowsdkstepenabled"] = false, ["executeprivilegename"] = readPrivilege,
            ["plugintypeid"] = new EntityReference("plugintype", typeId)
        });
        if (apis.Length == 1) Client.Update(new Entity("customapi", apiId) {
            ["plugintypeid"] = new EntityReference("plugintype", typeId)
        });
        var parameters = Find("customapirequestparameter", "customapiid", apiId, "uniquename");
        foreach (var (name, type) in new[] { ("ApprovedLimitHours", 7), ("DocumentReference", 10) })
            if (!parameters.Any(p => p.GetAttributeValue<string>("uniquename") == name))
                CreateInSolution(new Entity("customapirequestparameter") {
                    ["customapiid"] = new EntityReference("customapi", apiId),
                    ["uniquename"] = name, ["name"] = CarryForwardExceptionApi + "." + name,
                    ["displayname"] = name, ["type"] = new OptionSetValue(type),
                    ["isoptional"] = false
                });
        var outputs = Find("customapiresponseproperty", "customapiid", apiId, "uniquename");
        foreach (var name in new[] { "Status", "ExceptionId" })
            if (!outputs.Any(p => p.GetAttributeValue<string>("uniquename") == name))
                CreateInSolution(new Entity("customapiresponseproperty") {
                    ["customapiid"] = new EntityReference("customapi", apiId),
                    ["uniquename"] = name, ["name"] = CarryForwardExceptionApi + "." + name,
                    ["displayname"] = name, ["type"] = new OptionSetValue(10)
                });
        var confirm = Find("customapi", "uniquename", CarryForwardConfirmApi).Single();
        Attribute(CarryForwardReviewTable, Text("cr2c9_adjustmentdocumentref",
            "HR Adjustment Supporting Document Reference", 200));
        Client.Execute(new PublishXmlRequest { ParameterXml =
            "<importexportxml><entities><entity>" + CarryForwardReviewTable +
            "</entity></entities></importexportxml>" });
        var confirmInputs = Find("customapirequestparameter", "customapiid", confirm.Id, "uniquename");
        foreach (var (name, type) in new[] {
            ("ReviewedCarryHours", 7), ("AdjustmentReason", 10),
            ("AdjustmentDocumentReference", 10)
        }) {
            if (confirmInputs.Count(p => p.GetAttributeValue<string>("uniquename") == name) > 1)
                throw new Exception("Duplicate carry-forward adjustment input: " + name);
            if (!confirmInputs.Any(p => p.GetAttributeValue<string>("uniquename") == name))
                CreateInSolution(new Entity("customapirequestparameter") {
                    ["customapiid"] = confirm.ToEntityReference(),
                    ["uniquename"] = name,
                    ["name"] = CarryForwardConfirmApi + "." + name,
                    ["displayname"] = name,
                    ["type"] = new OptionSetValue(type),
                    ["isoptional"] = true
                });
        }
        Client.Update(new Entity("customapi", confirm.Id) {
            ["displayname"] = "Confirm reviewed Annual Leave carry-forward",
            ["description"] = "HR-only year-end confirmation. Uses the five-day limit or timely written exception; a lower HR adjustment requires a reason and document reference."
        });
        Console.WriteLine("Deployed written carry-forward exception and audited HR adjustment actions. No exception or credit was posted.");
    }
}
