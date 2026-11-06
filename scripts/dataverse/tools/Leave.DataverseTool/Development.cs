using System.Reflection;
using System.ServiceModel;
using System.Xml.Linq;
using Microsoft.Identity.Client;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Crm.Sdk.Messages;

// Fixed-environment development deployment. No credentials or bearer tokens are
// read from browser/PAC caches. Authentication remains in memory for this process.
internal static partial class Development
{
    const string Org = "https://orgf7668341.crm5.dynamics.com", Solution = "Leaveandclaimhub";
    const string Request = "cr2c9_leaverequest", Employee = "cr2c9_employee", History = "cr2c9_leavedecision";
    const string Root = "/Users/amirul/Documents/leave_and_claim_system/scripts/dataverse";
    static ServiceClient Client = null!;
    static Guid FixtureRequest = Guid.Parse("edcb0c35-8450-48b3-9b66-fd7873366d28");
    static Guid FixtureEmployee = Guid.Parse("a3a2b544-41de-4385-bacf-9859c45656c5");
    static readonly Dictionary<string, Dictionary<Guid, DateTime>> DateExpectations = new();
    static readonly List<Guid> DateJobs = new();
    static Label L(string value) => new(value, 1033);
    static EntityMetadata Meta(string table) => ((RetrieveEntityResponse)Client.Execute(new RetrieveEntityRequest {
        LogicalName = table, EntityFilters = EntityFilters.Entity | EntityFilters.Attributes, RetrieveAsIfPublished = true
    })).EntityMetadata;
    static Entity[] Find(string table, string field, object value, params string[] cols) {
        var q = new QueryExpression(table) { ColumnSet = new ColumnSet(cols) };
        q.Criteria.AddCondition(field, ConditionOperator.Equal, value);
        return Client.RetrieveMultiple(q).Entities.ToArray();
    }
    static void Add(Guid id, int type) => Client.Execute(new AddSolutionComponentRequest {
        ComponentId = id, ComponentType = type, SolutionUniqueName = Solution, AddRequiredComponents = false
    });
    public static async Task Run() {
        var app = PublicClientApplicationBuilder.Create("51f81489-12ee-4a9e-aaae-a2591f45987d")
            .WithAuthority("https://login.microsoftonline.com/organizations").WithRedirectUri("http://localhost").Build();
        var scopes = new[] { Org + "/user_impersonation" };
        var auth = await app.AcquireTokenWithDeviceCode(scopes, c => { Console.WriteLine(c.Message); return Task.CompletedTask; }).ExecuteAsync();
        using var client = new ServiceClient(new Uri(Org), async _ => (await app.AcquireTokenSilent(scopes, auth.Account).ExecuteAsync()).AccessToken, true);
        Client = client;
        if (!client.IsReady) throw new Exception("Dataverse connection failed.");
        var who = (WhoAmIResponse)Client.Execute(new WhoAmIRequest());
        var user = Client.Retrieve("systemuser", who.UserId, new ColumnSet("azureactivedirectoryobjectid"));
        if (user.GetAttributeValue<Guid>("azureactivedirectoryobjectid") != Guid.Parse("a8f9e5ea-402e-43a1-812c-dffee674d6f9")) throw new Exception("Wrong development identity.");
        Console.WriteLine("Connected to authorized Leave development environment. Commands include employee-profile-write-test, annual-balance-view-deploy, annual-grant-regression, access-inspect, quit; see Development.cs for the full command list.");
        for (string? command; (command = Console.ReadLine()) != null && command != "quit";) {
            try {
                switch (command) {
                    case "inspect": Inspect(); break;
                    case "leave-type-inspect": LeaveTypeInspect(); break;
                    case "schema": Schema(); break;
                    case "publish": Publish(); break;
                    case "deploy": Deploy(); break;
                    case "public-absence-schema-deploy": PublicAbsenceSchemaDeploy(); break;
                    case "classification-schema-deploy": ClassificationSchemaDeploy(); break;
                    case "classification-roles-deploy": ClassificationRolesDeploy(); break;
                    case "classification-review-form-deploy": ClassificationReviewFormDeploy(); break;
                    case "classification-server-deploy": ClassificationServerDeploy(); break;
                    case "classification-calendar-backfill": ClassificationCalendarBackfill(); break;
                    case "role-dashboard-resource-deploy": RoleDashboardResourceDeploy(); break;
                    case "public-absence-read-roles-deploy": PublicAbsenceReadRolesDeploy(); break;
                    case "calendar-reader-auto-deploy": CalendarReaderAutoDeploy(); break;
                    case "calendar-reader-darwisyah-test": CalendarReaderDarwisyahTest(); break;
                    case "calendar-reader-role-inspect": CalendarReaderRoleInspect(); break;
                    case "public-absence-audit": PublicAbsenceAudit(); break;
                    case "public-absence-lr0098-inspect": PublicAbsenceLr0098(false); break;
                    case "public-absence-lr0098-backfill": PublicAbsenceLr0098(true); break;
                    case "smoke": Smoke(); break;
                    case "fixture": Fixture(); break;
                    case "positive": Positive(); break;
                    case "own-dates": OwnDates(); break;
                    case "date-preflight": DatePreflight(); break;
                    case "dates": Dates(); break;
                    case "verify-dates": VerifyDates(); break;
                    case "entitlement-inspect": EntitlementInspect(); break;
                    case "entitlement-deploy": EntitlementDeploy(); break;
                    case "entitlement-test": EntitlementTest(); break;
                    case "submission-deploy": SubmissionDeploy(); break;
                    case "submission-test": SubmissionTest(); break;
                    case "uniform-annual-deploy": UniformAnnualDeploy(); break;
                    case "uniform-annual-test": UniformAnnualTest(); break;
                    case "uniform-range-test": UniformRangeTest(); break;
                    case "submission-dateonly-inspect": SubmissionDateOnlyInspect(); break;
                    case "balance-inspect": BalanceInspect(); break;
                    case "balance-schema": BalanceSchema(); break;
                    case "balance-protect": BalanceProtect(); break;
                    case "balance-protection-test": BalanceProtectionTest(); break;
                    case "attachments-inspect": AttachmentsInspect(); break;
                    case "employee-request-audit": EmployeeRequestAudit(); break;
                    case "request-edit-diagnostic": RequestEditDiagnostic(); break;
                    case "request-edit-probe": RequestEditProbe(); break;
                    case "employee-note-access-fix": EmployeeNoteAccessFix(); break;
                    case "annual-grant-deploy": AnnualGrantDeploy(); break;
                    case "sick-grant-deploy": SickGrantDeploy(); break;
                    case "hospital-grant-deploy": HospitalGrantDeploy(); break;
                    case "hospital-grant-fixture-test": HospitalGrantFixtureTest(); break;
                    case "hospital-approval-enable": HospitalApprovalEnable(); break;
                    case "hospital-approval-fixture-test": HospitalApprovalFixtureTest(); break;
                    case "hospital-cancellation-fixture-test": HospitalCancellationFixtureTest(); break;
                    case "sick-grant-fixture-test": SickGrantFixtureTest(); break;
                    case "sick-grant-live-audit": SickGrantLiveAudit(); break;
                    case "sick-approval-enable": SickApprovalEnable(); break;
                    case "sick-approval-fixture-test": SickApprovalFixtureTest(); break;
                    case "annual-grant-test": AnnualGrantTest(); break;
                    case "annual-grant-verify": AnnualGrantVerify(); break;
                    case "annual-accrual-fixture-test": AnnualAccrualFixtureTest(); break;
                    case "annual-accrual-approval-test": AnnualAccrualApprovalTest(); break;
                    case "legacy-earned-cap-test": LegacyEarnedCapTest(); break;
                    case "annual-credit-audit": AnnualCreditAudit(); break;
                    case "annual-grant-preflight": AnnualGrantPreflight(); break;
                    case "hr-config-inspect": HrConfigurationInspect(); break;
                    case "annual-grant-ui": AnnualGrantUi(); break;
                    case "annual-balance-view-deploy": AnnualBalanceViewDeploy(); break;
                    case "annual-grant-regression": AnnualGrantRegression(); break;
                    case "annual-accrual-flow-inspect": AnnualAccrualFlowInspect(); break;
                    case "sick-grant-flow-preflight": SickGrantFlowPreflight(); break;
                    case "sick-grant-registration-compare": SickGrantRegistrationCompare(); break;
                    case "sick-grant-publish-metadata": SickGrantPublishMetadata(); break;
                    case "sick-grant-webapi-metadata": await SickGrantWebApiMetadata((await app.AcquireTokenSilent(scopes, auth.Account).ExecuteAsync()).AccessToken); break;
                    case "annual-accrual-pause-flow": AnnualAccrualPauseFlow(); break;
                    case "annual-accrual-resume-flow": AnnualAccrualResumeFlow(); break;
                    case "approval-accounting-deploy": ApprovalAccountingDeploy(); break;
                    case "approval-notification-inspect": ApprovalNotificationInspect(); break;
                    case "notification-flow-inspect": NotificationFlowInspect(); break;
                    case "leave-decision-notifications-deploy": LeaveDecisionNotificationsDeploy(); break;
                    case "approval-notification-protect": ApprovalNotificationProtect(); break;
                    case "approval-fixture-prepare": ApprovalFixturePrepare(); break;
                    case "approval-fixture-individual": ApprovalFixturePrepare(true); break;
                    case "approval-accounting-smoke": ApprovalAccountingSmoke(); break;
                    case "approval-accounting-race": ApprovalAccountingRace(); break;
                    case "access-inspect": AccessInspect(); break;
                    case "ordinary-candidate-inspect": OrdinaryCandidateInspect(); break;
                    case "ordinary-candidate-remove-privileged-roles": OrdinaryCandidateRemovePrivilegedRoles(); break;
                    case "ordinary-candidate-basic-role": OrdinaryCandidateBasicRole(); break;
                    case "employee-test-role-deploy": EmployeeTestRoleDeploy(); break;
                    case "employee-test-row-access": EmployeeTestRowAccess(); break;
                    case "employee-test-submission": EmployeeTestSubmission(); break;
                    case "employee-profile-write-test": EmployeeProfileWriteTest(); break;
                    case "superior-role-inspect": SuperiorRoleInspect(); break;
                    case "superior-role-deploy": SuperiorRoleDeploy(); break;
                    case "hr-role-deploy": HrRoleDeploy(); break;
                    case "hr-approval-plugin-deploy": HrApprovalPluginDeploy(); break;
                    case "carryforward-schema": CarryForwardSchema(); break;
                    case "carryforward-credit-schema": CarryForwardCreditSchema(); break;
                    case "carryforward-usage-schema": CarryForwardUsageSchema(); break;
                    case "carryforward-exception-deploy": CarryForwardExceptionDeploy(); break;
                    case "carryforward-notification-deploy": CarryForwardNotificationDeploy(); break;
                    case "carryforward-reminder-deploy": CarryForwardReminderDeploy(); break;
                    case "carryforward-darwisyah-dry-run": CarryForwardDarwisyahDryRun(); break;
                    case "carryforward-guard-deploy": CarryForwardGuardDeploy(); break;
                    case "carryforward-guard-test": CarryForwardGuardTest(); break;
                    case "carryforward-preview-deploy": CarryForwardPreviewDeploy(); break;
                    case "carryforward-preview-test": CarryForwardPreviewTest(); break;
                    case "carryforward-confirm-deploy": CarryForwardConfirmDeploy(); break;
                    case "carryforward-confirm-test": CarryForwardConfirmTest(); break;
                    case "superior-access-inspect": SuperiorAccessInspect(); break;
                    case "superior-access-schema": SuperiorAccessSchema(); break;
                    case "superior-access-deploy": SuperiorAccessDeploy(); break;
                    case "superior-access-fixture": SuperiorAccessFixture(); break;
                    case "superior-views-inspect": SuperiorViewsInspect(); break;
                    case "superior-grid-script-deploy": SuperiorGridScriptDeploy(); break;
                    case "superior-manager-reassign-audit": SuperiorManagerReassignAudit(); break;
                    case "superior-sync-flow-audit": SuperiorSyncFlowAudit(); break;
                    case "superior-sync-flow-deploy": SuperiorSyncFlowDeploy(); break;
                    case "superior-sync-flow-finalize": SuperiorSyncFlowFinalize(); break;
                    case "superior-all-record-audit": SuperiorAllRecordAudit(); break;
                    case "superior-owner-fix-deploy": SuperiorOwnerFixDeploy(); break;
                    case "maternity-error-trace": MaternityErrorTrace(); break;
                    case "maternity-schema-deploy": MaternitySchemaDeploy(); break;
                    case "maternity-form-inspect": MaternityFormInspect(); break;
                    case "maternity-form-deploy": MaternityFormDeploy(); break;
                    case "paternity-schema-deploy": PaternitySchemaDeploy(); break;
                    case "paternity-form-deploy": PaternityFormDeploy(); break;
                    case "marriage-schema-deploy": MarriageSchemaDeploy(); break;
                    case "marriage-form-deploy": MarriageFormDeploy(); break;
                    case "marriage-type-deploy": MarriageTypeDeploy(); break;
                    case "compassionate-schema-deploy": CompassionateSchemaDeploy(); break;
                    case "compassionate-form-deploy": CompassionateFormDeploy(); break;
                    case "compassionate-type-deploy": CompassionateTypeDeploy(); break;
                    case "training-schema-deploy": TrainingSchemaDeploy(); break;
                    case "training-review-form-deploy": TrainingReviewFormDeploy(); break;
                    case "training-server-deploy": TrainingServerDeploy(); break;
                    case "superior-views-deploy": SuperiorViewsDeploy(); break;
                    case "hr-leave-views-deploy": HrLeaveViewsDeploy(); break;
                    case "hr-report-resource-deploy": HrReportResourceDeploy(); break;
                    case "medical-rename-deploy": MedicalRenameDeploy(); break;
                    case "superior-form-fixture": SuperiorFormFixture(); break;
                    case "superior-form-verify": SuperiorFormVerify(); break;
                    case "hr-approval-live-test": HrApprovalLiveTest(); break;
                    case "hr-cancellation-live-test": HrCancellationLiveTest(); break;
                    case "hr-approval-post-verify": HrApprovalPostVerify(); break;
                    case "hr-superior-app-access-audit": HrSuperiorAppAccessAudit(); break;
                    case "app-runtime-baseline-audit": AppRuntimeBaselineAudit(); break;
                    case "app-runtime-entity-fix": AppRuntimeEntityFix(); break;
                    case "cancellation-schema": CancellationSchema(); break;
                    case "cancellation-deploy": CancellationDeploy(); break;
                    case "cancellation-test": CancellationTest(); break;
                    case "cancellation-form-inspect": CancellationFormInspect(); break;
                    case "cancellation-form-deploy": CancellationFormDeploy(); break;
                    case "cancellation-form-fixture": CancellationFormFixture(); break;
                    case "cancellation-form-verify": CancellationFormVerify(); break;
                    case "cancellation-candidates": CancellationCandidates(); break;
                    case "cancellation-own-test-fixture": CancellationOwnTestFixture(); break;
                    default: Console.WriteLine("Unknown command."); break;
                }
                Console.WriteLine("READY");
            } catch (FaultException<OrganizationServiceFault> ex) {
                Console.WriteLine("FAILED " + ex.Detail.ErrorCode + ": " + ex.Detail.Message);
                Console.WriteLine("Transaction request index: " + ex.Detail.GetType().GetProperty("FaultedRequestIndex")?.GetValue(ex.Detail));
                Console.WriteLine("Trace: " + ex.Detail.TraceText);
            }
              catch (Exception ex) { Console.WriteLine("FAILED " + ex.GetType().Name + ": " + ex.Message); }
        }
    }
    static void OwnDates() {
        const string dates = "cr2c9_leaverequestdate";
        var own = Guid.Parse("6938b1f2-22c2-f111-aaad-000d3a826b69");
        var typeName = Meta("cr2c9_leavetype").PrimaryNameAttribute;
        var types = Client.RetrieveMultiple(new QueryExpression("cr2c9_leavetype") { ColumnSet = new ColumnSet(typeName) }).Entities;
        var annual = types.Single(x => x.GetAttributeValue<string>(typeName) == "Annual Leave");
        var request = new Entity(Request) {
            ["cr2c9_employeename"] = new EntityReference(Employee, own),
            ["cr2c9_leavetypename"] = annual.ToEntityReference(),
            ["cr2c9_startdate"] = new DateTime(2026,11,2), ["cr2c9_enddate"] = new DateTime(2026,11,3),
            ["cr2c9_status"] = new OptionSetValue(0),
            ["cr2c9_reason"] = "Fictional development fixture: own pending date CRUD and range validation. Not an approval or entitlement test."
        };
        var id = Client.Create(request);
        Console.WriteLine("Own-date fixture request " + id + " (kept Pending Superior; no approval notification)");
        Entity Entry(DateTime day, int duration) => new(dates) {
            ["cr2c9_entryname"] = "Development date fixture", ["cr2c9_leaverequest"] = new EntityReference(Request,id),
            ["cr2c9_leavedate"] = day, ["cr2c9_duration"] = new OptionSetValue(duration)
        };
        var first = Client.Create(Entry(new DateTime(2026,11,2),290040000));
        var second = Client.Create(Entry(new DateTime(2026,11,3),290040001));
        if (Find(dates,"cr2c9_leaverequest",id,"cr2c9_duration").Length != 2) throw new Exception("Mixed date creation failed.");
        Console.WriteLine("PASS live own pending mixed 4/8-hour date creation");
        Client.Update(new Entity(dates,first) { ["cr2c9_duration"] = new OptionSetValue(290040001) });
        if (Client.Retrieve(dates,first,new ColumnSet("cr2c9_duration")).GetAttributeValue<OptionSetValue>("cr2c9_duration").Value != 290040001) throw new Exception("Date update failed.");
        Console.WriteLine("PASS live own pending date update");
        var before = Client.Retrieve(Request,id,new ColumnSet("cr2c9_status")).RowVersion;
        Reject("Date outside request range", () => Client.Create(Entry(new DateTime(2026,11,4),290040000)), "within the request");
        if (Client.Retrieve(Request,id,new ColumnSet("cr2c9_status")).RowVersion != before) throw new Exception("Failed date edit changed parent version.");
        Console.WriteLine("PASS range failure rolls back the parent lock");
        Client.Delete(dates,second);
        if (Find(dates,"cr2c9_leaverequest",id,"cr2c9_duration").Length != 1) throw new Exception("Date deletion failed.");
        Console.WriteLine("PASS live own pending date deletion; one date retained for review");
    }
    static void Inspect() {
        foreach (var table in new[] { Request, Employee }) {
            var meta = Meta(table);
            Console.WriteLine($"{table}: primary={meta.PrimaryNameAttribute}, concurrency={meta.IsOptimisticConcurrencyEnabled}");
            foreach (var a in meta.Attributes.Where(a => a.LogicalName.StartsWith("cr2c9_")))
                Console.WriteLine($"  {a.LogicalName} {a.AttributeType} required={a.RequiredLevel?.Value}");
        }
        var f = Client.Retrieve("systemform", Guid.Parse("f297bf4c-1a1d-4560-b594-572612a10d5c"), new ColumnSet("formxml"));
        var xml = XDocument.Parse(f.GetAttributeValue<string>("formxml"));
        Console.WriteLine("Stamp controls: " + string.Join(" | ", xml.Descendants("control").Where(e => (string?)e.Attribute("datafieldname") == "cr2c9_decisionbyentraid").Select(e => e.Parent!.ToString())));
        var types = Client.RetrieveMultiple(new QueryExpression("plugintype") { ColumnSet = new ColumnSet("typename", "name") }).Entities;
        Console.WriteLine("Leave plugin types: " + string.Join(", ", types.Where(x => x.GetAttributeValue<string>("typename")?.StartsWith("Leave.Plugins.") == true).Select(x => x.GetAttributeValue<string>("typename"))));
    }
    static void LeaveTypeInspect() {
        var rows=Client.RetrieveMultiple(new QueryExpression("cr2c9_leavetype") {
            ColumnSet=new ColumnSet("cr2c9_leavetypename","statecode")
        }).Entities;
        foreach(var row in rows.OrderBy(r=>r.GetAttributeValue<string>("cr2c9_leavetypename")))
            Console.WriteLine(row.GetAttributeValue<string>("cr2c9_leavetypename")+" "+row.Id+
                " active="+(row.GetAttributeValue<OptionSetValue>("statecode")?.Value==0));
    }
    static StringAttributeMetadata Text(string schema, string display, int length, bool required = false) => new() {
        SchemaName = schema, DisplayName = L(display), MaxLength = length, FormatName = StringFormatName.Text,
        RequiredLevel = new AttributeRequiredLevelManagedProperty(required ? AttributeRequiredLevel.ApplicationRequired : AttributeRequiredLevel.None)
    };
    static void Attribute(string table, AttributeMetadata definition) {
        var existing = Meta(table).Attributes.SingleOrDefault(a => a.LogicalName == definition.SchemaName.ToLowerInvariant());
        if (existing != null) {
            if (existing.AttributeType != definition.AttributeType || existing is StringAttributeMetadata s && definition is StringAttributeMetadata d && s.MaxLength != d.MaxLength)
                throw new Exception("Existing column has a different definition: " + definition.SchemaName);
            Console.WriteLine("Verified existing " + definition.SchemaName); return;
        }
        var response = (CreateAttributeResponse)Client.Execute(new CreateAttributeRequest { EntityName = table, Attribute = definition, SolutionUniqueName = Solution });
        Console.WriteLine("Created " + definition.SchemaName + " " + response.AttributeId);
    }
    static void Schema() {
        Attribute(Request, Text("cr2c9_reviewlocktoken", "Server Review Lock", 36));
        Attribute(Employee, Text("cr2c9_reviewlocktoken", "Server Review Lock", 36));
        Attribute(Request, Text("cr2c9_decisionexpectedversion", "Review Expected Version", 30));
        var all = (RetrieveAllEntitiesResponse)Client.Execute(new RetrieveAllEntitiesRequest { EntityFilters = EntityFilters.Entity, RetrieveAsIfPublished = true });
        if (!all.EntityMetadata.Any(e => e.LogicalName == History)) {
            var response = (CreateEntityResponse)Client.Execute(new CreateEntityRequest {
                SolutionUniqueName = Solution,
                Entity = new EntityMetadata { SchemaName = History, DisplayName = L("Leave Decision"), DisplayCollectionName = L("Leave Decisions"),
                    Description = L("Immutable server-written Superior decisions, linked to their request."), OwnershipType = OwnershipTypes.UserOwned, IsActivity = false },
                PrimaryAttribute = Text("cr2c9_name", "Decision Name", 100, true), HasActivities = false, HasNotes = false
            });
            Console.WriteLine("Created Leave Decision table " + response.EntityId);
        }
        Attribute(History, Text("cr2c9_operationkey", "Operation Key", 100, true));
        Attribute(History, Text("cr2c9_actorentraid", "Decision Maker Entra ID", 36, true));
        Attribute(History, Text("cr2c9_actorname", "Decision Maker Name", 200, true));
        Attribute(History, new DateTimeAttributeMetadata { SchemaName = "cr2c9_decidedon", DisplayName = L("Decided On"), Format = DateTimeFormat.DateAndTime,
            DateTimeBehavior = DateTimeBehavior.UserLocal, RequiredLevel = new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.ApplicationRequired) });
        Attribute(History, new MemoAttributeMetadata { SchemaName = "cr2c9_comments", DisplayName = L("Decision Comments"), MaxLength = 2000,
            RequiredLevel = new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.None) });
        Attribute(History, new PicklistAttributeMetadata { SchemaName = "cr2c9_outcome", DisplayName = L("Outcome"),
            OptionSet = new OptionSetMetadata { IsGlobal = false, OptionSetType = OptionSetType.Picklist, Options = { new OptionMetadata(L("Approved"), 1), new OptionMetadata(L("Rejected"), 3) } },
            RequiredLevel = new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.ApplicationRequired) });
        if (!Meta(History).Attributes.Any(a => a.LogicalName == "cr2c9_leaverequest")) {
            Client.Execute(new CreateOneToManyRequest { SolutionUniqueName = Solution,
                Lookup = new LookupAttributeMetadata { SchemaName = "cr2c9_leaverequest", DisplayName = L("Leave Request"), RequiredLevel = new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.ApplicationRequired) },
                OneToManyRelationship = new OneToManyRelationshipMetadata { SchemaName = "cr2c9_leavedecision_LeaveRequest", ReferencedEntity = Request, ReferencingEntity = History,
                    CascadeConfiguration = new CascadeConfiguration { Assign = CascadeType.NoCascade, Delete = CascadeType.Restrict, Merge = CascadeType.NoCascade,
                        Reparent = CascadeType.NoCascade, Share = CascadeType.NoCascade, Unshare = CascadeType.NoCascade } }
            }); Console.WriteLine("Created restrictive decision relationship.");
        }
        var meta = Meta(History);
        var keys = ((RetrieveEntityResponse)Client.Execute(new RetrieveEntityRequest { LogicalName = History, EntityFilters = EntityFilters.All, RetrieveAsIfPublished = true })).EntityMetadata.Keys;
        if (!keys.Any(k => k.LogicalName == "cr2c9_uniquedecisionoperation")) {
            Client.Execute(new CreateEntityKeyRequest { EntityName = History, EntityKey = new EntityKeyMetadata {
                SchemaName = "cr2c9_uniquedecisionoperation", DisplayName = L("Unique Decision Operation"), KeyAttributes = new[] { "cr2c9_operationkey" }
            }, SolutionUniqueName = Solution }); Console.WriteLine("Created unique decision key; must be Active before deployment.");
        }
        Console.WriteLine("Schema ready.");
    }
    static void Publish() {
        var formId = Guid.Parse("f297bf4c-1a1d-4560-b594-572612a10d5c");
        var form = Client.Retrieve("systemform", formId, new ColumnSet("formxml"));
        var xml = XDocument.Parse(form.GetAttributeValue<string>("formxml"));
        if (!xml.Descendants("control").Any(e => (string?)e.Attribute("datafieldname") == "cr2c9_decisionexpectedversion")) {
            var stamp = xml.Descendants("control").Single(e => (string?)e.Attribute("datafieldname") == "cr2c9_decisionbyentraid");
            var row = new XElement(stamp.Parent!.Parent!);
            foreach (var element in row.DescendantsAndSelf().Where(e => e.Attribute("id") != null)) element.SetAttributeValue("id", "{" + Guid.NewGuid().ToString("D") + "}");
            var cell = row.Element("cell")!; cell.SetAttributeValue("visible", "false");
            var control = row.Descendants("control").Single(); control.SetAttributeValue("id", "cr2c9_decisionexpectedversion");
            control.SetAttributeValue("datafieldname", "cr2c9_decisionexpectedversion"); control.SetAttributeValue("disabled", "true");
            foreach (var label in row.Descendants("label")) label.SetAttributeValue("description", "Review Expected Version");
            stamp.Parent.Parent!.AddAfterSelf(row);
            Client.Update(new Entity("systemform", formId) { ["formxml"] = xml.ToString(SaveOptions.DisableFormatting) });
            Console.WriteLine("Added hidden expected-version form control.");
        }
        var resources = Find("webresource", "name", "cr2c9_leaveRequestForm.js", "content", "name");
        if (resources.Length != 1) throw new Exception("Expected exactly one existing leave review web resource.");
        Client.Update(new Entity("webresource", resources[0].Id) { ["content"] = Convert.ToBase64String(File.ReadAllBytes(Path.Combine(Root, "cr2c9_leaveRequestForm.js"))) });
        Client.Execute(new PublishXmlRequest { ParameterXml = "<importexportxml><entities><entity>cr2c9_employee</entity><entity>cr2c9_leaverequest</entity><entity>cr2c9_leavedecision</entity></entities><webresources><webresource>" + resources[0].Id + "</webresource></webresources></importexportxml>" });
        Console.WriteLine("Published version-aware review form and web resource.");
    }
    static void Deploy() {
        var keys = ((RetrieveEntityResponse)Client.Execute(new RetrieveEntityRequest { LogicalName = History, EntityFilters = EntityFilters.All, RetrieveAsIfPublished = true })).EntityMetadata.Keys;
        if (keys.Single(k => k.LogicalName == "cr2c9_uniquedecisionoperation").EntityKeyIndexStatus != EntityKeyIndexStatus.Active) throw new Exception("Decision key is not Active yet.");
        foreach (var table in new[] { Request, Employee }) if (Meta(table).IsOptimisticConcurrencyEnabled != true) throw new Exception("Concurrency is not enabled for " + table);
        var dll = Path.Combine(Root, "server/Leave.Plugins/bin/Release/net462/Leave.Plugins.dll");
        var assembly = AssemblyName.GetAssemblyName(dll);
        var existing = Find("pluginassembly", "name", assembly.Name!, "name");
        var registration = new Entity("pluginassembly") { ["content"] = Convert.ToBase64String(File.ReadAllBytes(dll)),
            ["name"] = assembly.Name, ["version"] = assembly.Version!.ToString(), ["culture"] = "neutral",
            ["publickeytoken"] = Convert.ToHexString(assembly.GetPublicKeyToken()!).ToLowerInvariant(),
            ["isolationmode"] = new OptionSetValue(2), ["sourcetype"] = new OptionSetValue(0),
            ["description"] = "Synchronous leave review, per-date and immutable decision safeguards." };
        if (existing.Length > 1) throw new Exception("Ambiguous plugin assembly.");
        Guid assemblyId;
        if (existing.Length == 0) assemblyId = Client.Create(registration);
        else { assemblyId = existing[0].Id; registration.Id = assemblyId; Client.Update(registration); }
        Add(assemblyId, 91);
        foreach (var pair in new[] { (History, "Leave.Plugins.LeaveDecisionHistoryGuard"), ("cr2c9_leaverequestdate", "Leave.Plugins.LeaveDateGuard"), (Request, "Leave.Plugins.LeaveRequestGuard") }) {
            var matching = Find("plugintype", "typename", pair.Item2, "pluginassemblyid").Where(x => x.GetAttributeValue<EntityReference>("pluginassemblyid")?.Id == assemblyId).ToArray();
            Entity type;
            if (matching.Length == 0) {
                var id = Client.Create(new Entity("plugintype") { ["pluginassemblyid"] = new EntityReference("pluginassembly", assemblyId),
                    ["typename"] = pair.Item2, ["name"] = pair.Item2, ["friendlyname"] = pair.Item2 });
                type = new Entity("plugintype", id); Console.WriteLine("Registered " + pair.Item2);
            } else type = matching.Single();
            foreach (var message in new[] { "Create", "Update", "Delete" }) {
                var messageId = Find("sdkmessage", "name", message).Single().Id;
                var q = new QueryExpression("sdkmessagefilter") { ColumnSet = new ColumnSet("sdkmessagefilterid") };
                q.Criteria.AddCondition("sdkmessageid", ConditionOperator.Equal, messageId); q.Criteria.AddCondition("primaryobjecttypecode", ConditionOperator.Equal, pair.Item1);
                var filter = Client.RetrieveMultiple(q).Entities.Single();
                var name = "Leave safeguard: " + pair.Item1 + " " + message;
                var old = Find("sdkmessageprocessingstep", "name", name, "statecode", "stage", "mode", "eventhandler", "sdkmessageid", "sdkmessagefilterid");
                var step = new Entity("sdkmessageprocessingstep") { ["name"] = name, ["eventhandler"] = type.ToEntityReference(),
                    ["sdkmessageid"] = new EntityReference("sdkmessage", messageId), ["sdkmessagefilterid"] = filter.ToEntityReference(),
                    ["stage"] = new OptionSetValue(20), ["mode"] = new OptionSetValue(0), ["rank"] = 10,
                    ["supporteddeployment"] = new OptionSetValue(0), ["description"] = "Calling user; synchronous transactional guard; applies to every write." };
                Guid stepId;
                if (old.Length == 0) stepId = Client.Create(step);
                else { stepId = old.Single().Id; step.Id = stepId; Client.Update(step); }
                Add(stepId, 92); Console.WriteLine("Enabled " + name + " " + stepId);
            }
        }
        Console.WriteLine("Server assembly and nine synchronous steps deployed in Leave and claim hub.");
    }
    static void Reject(string label, Action action, string expectedText) {
        try { action(); throw new Exception("Unexpectedly allowed: " + label); }
        catch (FaultException<OrganizationServiceFault> ex) {
            if (!ex.Detail.Message.Contains(expectedText, StringComparison.OrdinalIgnoreCase)) throw;
            Console.WriteLine("PASS " + label + ": " + ex.Detail.Message.Split('\n')[0]);
        }
    }

    static void Fixture() {
        // Explicitly fictional dummy records, seeded before guard activation.
        if (Find(Request, Request + "id", FixtureRequest).Length != 0) { Console.WriteLine("Fixture already exists."); return; }
        if (Find("sdkmessageprocessingstep", "name", "Leave safeguard: " + Request + " Create").Length != 0) throw new Exception("Seed fixture before activation; no bypass is used.");
        if (Find(Employee, Employee + "id", FixtureEmployee).Length == 0) Client.Create(new Entity(Employee, FixtureEmployee) {
            ["cr2c9_employeename"] = "Development Test Employee (fictional)", ["cr2c9_email"] = "leave-development@example.invalid",
            ["cr2c9_entrauserid"] = Guid.Parse("a4c8bef4-ffb8-4efd-9ef7-e551e0bd1c09").ToString(), ["cr2c9_isactive"] = true,
            ["cr2c9_superior"] = new EntityReference(Employee, Guid.Parse("6938b1f2-22c2-f111-aaad-000d3a826b69"))
        });
        var type = Client.RetrieveMultiple(new QueryExpression("cr2c9_leavetype") { ColumnSet = new ColumnSet(false), TopCount = 1 }).Entities.Single();
        Client.Create(new Entity(Request, FixtureRequest) { ["cr2c9_employeename"] = new EntityReference(Employee, FixtureEmployee),
            ["cr2c9_leavetypename"] = type.ToEntityReference(), ["cr2c9_status"] = new OptionSetValue(0),
            ["cr2c9_startdate"] = new DateTime(2026, 11, 1, 16, 0, 0, DateTimeKind.Utc),
            ["cr2c9_enddate"] = new DateTime(2026, 11, 1, 16, 0, 0, DateTimeKind.Utc),
            ["cr2c9_reason"] = "Fictional development fixture for server rejection and history tests. No balance or approval notification." });
        Console.WriteLine("Seeded fictional request " + FixtureRequest);
    }
    static void Positive() {
        var current = Client.Retrieve(Request, FixtureRequest, new ColumnSet("cr2c9_status", "cr2c9_decisionon"));
        if (current.GetAttributeValue<OptionSetValue>("cr2c9_status").Value != 0) throw new Exception("Fixture already decided; do not repeat positive test.");
        Reject("Missing expected version", () => Client.Update(new Entity(Request, FixtureRequest) { ["cr2c9_status"] = new OptionSetValue(3) }), "changed after review");
        Reject("Stale expected version", () => Client.Update(new Entity(Request, FixtureRequest) { ["cr2c9_status"] = new OptionSetValue(3), ["cr2c9_decisionexpectedversion"] = "1" }), "changed after review");
        Client.Update(new Entity(Request, FixtureRequest) { ["cr2c9_status"] = new OptionSetValue(3), ["cr2c9_decisionexpectedversion"] = current.RowVersion,
            ["cr2c9_decisionbyentraid"] = "forged", ["cr2c9_decisionbyname"] = "forged", ["cr2c9_decisionon"] = new DateTime(2000, 1, 1),
            ["cr2c9_decisioncomments"] = "Development integration test: fictional request rejected." });
        var saved = Client.Retrieve(Request, FixtureRequest, new ColumnSet("cr2c9_status", "cr2c9_decisionbyentraid", "cr2c9_decisionbyname", "cr2c9_decisionon", "cr2c9_decisionexpectedversion"));
        if (saved.GetAttributeValue<OptionSetValue>("cr2c9_status").Value != 3 || saved.GetAttributeValue<string>("cr2c9_decisionbyentraid") != "a8f9e5ea-402e-43a1-812c-dffee674d6f9" || saved.GetAttributeValue<DateTime>("cr2c9_decisionon").Year != DateTime.UtcNow.Year || saved.Contains("cr2c9_decisionexpectedversion")) throw new Exception("Server stamps/version cleanup failed.");
        var history = Find(History, "cr2c9_leaverequest", FixtureRequest, "cr2c9_outcome", "cr2c9_actorentraid").Single();
        if (history.GetAttributeValue<OptionSetValue>("cr2c9_outcome").Value != 3 || history.GetAttributeValue<string>("cr2c9_actorentraid") != saved.GetAttributeValue<string>("cr2c9_decisionbyentraid")) throw new Exception("Decision/history mismatch.");
        Console.WriteLine("PASS live Superior rejection: server replaced forged stamps, wrote exactly one linked history row, cleared transport version.");
        Reject("Edit history", () => Client.Update(new Entity(History, history.Id) { ["cr2c9_comments"] = "forged" }), "append-only");
        Reject("Delete history", () => Client.Delete(History, history.Id), "append-only");
        Reject("Repeat rejection", () => Client.Update(new Entity(Request, FixtureRequest) { ["cr2c9_status"] = new OptionSetValue(3), ["cr2c9_decisionexpectedversion"] = saved.RowVersion }), "Only Pending Superior");
        Console.WriteLine("Positive integration checks complete; fictional rejected fixture retained for review.");
    }
    static void DatePreflight() {
        var zone = Find("timezonedefinition", "standardname", "Singapore Standard Time", "timezonecode", "userinterfacename").Single();
        Console.WriteLine("Conversion timezone: " + zone.GetAttributeValue<string>("userinterfacename") + " code=" + zone.GetAttributeValue<int>("timezonecode"));
        foreach (var pair in new[] { (Request, "cr2c9_startdate"), (Request, "cr2c9_enddate"), (Employee, "cr2c9_datejoined") }) {
            var a = (DateTimeAttributeMetadata)Meta(pair.Item1).Attributes.Single(x => x.LogicalName == pair.Item2);
            var dependencies = (RetrieveDependenciesForDeleteResponse)Client.Execute(new RetrieveDependenciesForDeleteRequest { ComponentType = 2, ObjectId = a.MetadataId!.Value });
            Console.WriteLine(pair.Item2 + " behavior=" + a.DateTimeBehavior.Value + " changeable=" + a.CanChangeDateTimeBehavior.Value + " dependency types=" + string.Join(",", dependencies.EntityCollection.Entities.Select(e => e.GetAttributeValue<OptionSetValue>("dependentcomponenttype")?.Value).Distinct()));
            foreach (var d in dependencies.EntityCollection.Entities.Where(e => e.GetAttributeValue<OptionSetValue>("dependentcomponenttype")?.Value == 29)) {
                var wf = Client.Retrieve("workflow", d.GetAttributeValue<Guid>("dependentcomponentobjectid"), new ColumnSet("name", "category"));
                Console.WriteLine("Workflow dependency " + wf.GetAttributeValue<string>("name") + " category=" + wf.GetAttributeValue<OptionSetValue>("category")?.Value);
            }
        }
        var flows = Client.RetrieveMultiple(new QueryExpression("workflow") { ColumnSet = new ColumnSet("name", "clientdata"), Criteria = new FilterExpression { Conditions = { new ConditionExpression("category", ConditionOperator.Equal, 5) } } }).Entities;
        foreach (var flow in flows) {
            var data = flow.GetAttributeValue<string>("clientdata") ?? "";
            foreach (var field in new[] { "cr2c9_startdate", "cr2c9_enddate", "cr2c9_datejoined" }) {
                var index = data.IndexOf(field, StringComparison.OrdinalIgnoreCase);
                if (index >= 0) Console.WriteLine("Flow date use: " + flow.GetAttributeValue<string>("name") + " " + data.Substring(Math.Max(0, index - 100), Math.Min(250, data.Length - Math.Max(0, index - 100))));
            }
        }
    }
    static void Dates() {
        if (DateJobs.Count != 0) throw new Exception("Conversion already submitted in this session.");
        var fields = new Dictionary<string, string[]> { [Request] = new[] { "cr2c9_startdate", "cr2c9_enddate" }, [Employee] = new[] { "cr2c9_datejoined" } };
        var changed = new Dictionary<string, string[]>();
        foreach (var pair in fields) {
            var meta = Meta(pair.Key);
            var names = pair.Value.Where(n => ((DateTimeAttributeMetadata)meta.Attributes.Single(a => a.LogicalName == n)).DateTimeBehavior.Value == "UserLocal").ToArray();
            if (names.Length == 0) continue;
            var rows = Client.RetrieveMultiple(new QueryExpression(pair.Key) { ColumnSet = new ColumnSet(names) }).Entities;
            foreach (var name in names) DateExpectations[pair.Key + ":" + name] = rows.Where(r => r.Contains(name)).ToDictionary(r => r.Id, r => r.GetAttributeValue<DateTime>(name).AddHours(8).Date);
            foreach (var name in names) {
                var a = (DateTimeAttributeMetadata)meta.Attributes.Single(x => x.LogicalName == name);
                if (a.CanChangeDateTimeBehavior.Value != true) throw new Exception("Date behavior cannot change: " + name);
                Client.Execute(new UpdateAttributeRequest { EntityName = pair.Key, Attribute = new DateTimeAttributeMetadata { MetadataId = a.MetadataId, SchemaName = a.SchemaName,
                    DateTimeBehavior = DateTimeBehavior.DateOnly, Format = DateTimeFormat.DateOnly }, MergeLabels = true });
                Console.WriteLine("Changed " + name + " to DateOnly behavior.");
            }
            changed[pair.Key] = names;
        }
        if (changed.Count == 0) { Console.WriteLine("Already DateOnly; no conversion submitted."); return; }
        Client.Execute(new PublishXmlRequest { ParameterXml = "<importexportxml><entities>" + string.Join("", changed.Keys.Select(t => "<entity>" + t + "</entity>")) + "</entities></importexportxml>" });
        var code = Find("timezonedefinition", "standardname", "Singapore Standard Time", "timezonecode").Single().GetAttributeValue<int>("timezonecode");
        var attributes = new EntityAttributeCollection();
        foreach (var pair in changed) { var names = new System.Collections.Specialized.StringCollection(); names.AddRange(pair.Value); attributes.Add(pair.Key, names); }
        var response = (ConvertDateAndTimeBehaviorResponse)Client.Execute(new ConvertDateAndTimeBehaviorRequest {
            Attributes = attributes, ConversionRule = DateTimeBehaviorConversionRule.SpecificTimeZone.Value, TimeZoneCode = code, AutoConvert = false
        });
        DateJobs.Add(response.JobId);
        Console.WriteLine("Submitted KL UTC+8 date conversion job " + response.JobId + ". Expected values remain in memory only; no backup created.");
    }
    static void VerifyDates() {
        foreach (var job in DateJobs) {
            var state = Client.Retrieve("asyncoperation", job, new ColumnSet("statecode", "statuscode", "message"));
            Console.WriteLine("Date job state=" + state.GetAttributeValue<OptionSetValue>("statecode")?.Value + " status=" + state.GetAttributeValue<OptionSetValue>("statuscode")?.Value);
            if (state.GetAttributeValue<OptionSetValue>("statuscode")?.Value != 30) throw new Exception("Conversion job not successful yet: " + state.GetAttributeValue<string>("message"));
        }
        foreach (var pair in DateExpectations) {
            var parts = pair.Key.Split(':');
            foreach (var row in pair.Value) {
                var actual = Client.Retrieve(parts[0], row.Key, new ColumnSet(parts[1])).GetAttributeValue<DateTime>(parts[1]);
                if (actual != row.Value) throw new Exception("Date verification failed for " + pair.Key + " " + row.Key);
            }
            if (((DateTimeAttributeMetadata)Meta(parts[0]).Attributes.Single(a => a.LogicalName == parts[1])).DateTimeBehavior.Value != "DateOnly") throw new Exception("Wrong date behavior.");
            Console.WriteLine("PASS " + pair.Key + ": " + pair.Value.Count + " dates match original UTC+8 calendar dates.");
        }
        var maria = Client.Retrieve(Request, Guid.Parse("a590c686-22c2-f111-aaad-000d3a826b69"), new ColumnSet("cr2c9_startdate", "cr2c9_enddate", "cr2c9_decisionon"));
        Console.WriteLine("LR-0006 calendar dates=" + maria.GetAttributeValue<DateTime>("cr2c9_startdate").ToString("yyyy-MM-dd") + ".." + maria.GetAttributeValue<DateTime>("cr2c9_enddate").ToString("yyyy-MM-dd"));
    }

    static void Smoke() {
        var id = Guid.Parse("a590c686-22c2-f111-aaad-000d3a826b69");
        var before = Client.Retrieve(Request, id, new ColumnSet("cr2c9_status", "cr2c9_decisionon", "cr2c9_reviewlocktoken"));
        Reject("Repeat an existing approval", () => Client.Update(new Entity(Request, id) { ["cr2c9_status"] = new OptionSetValue(1), ["cr2c9_decisionexpectedversion"] = before.RowVersion }), "Only Pending Superior");
        Reject("Change decided request details", () => Client.Update(new Entity(Request, id) { ["cr2c9_decisioncomments"] = "Development negative test" }), "Only Pending Superior");
        Reject("Forge decision history", () => Client.Create(new Entity(History) { ["cr2c9_name"] = "Development negative test", ["cr2c9_operationkey"] = id + ":decision", ["cr2c9_leaverequest"] = new EntityReference(Request, id), ["cr2c9_outcome"] = new OptionSetValue(1) }), "server decision operation");
        Reject("Add a date to approved leave", () => Client.Create(new Entity("cr2c9_leaverequestdate") { ["cr2c9_entryname"] = "Development negative test", ["cr2c9_leaverequest"] = new EntityReference(Request, id), ["cr2c9_leavedate"] = new DateTime(2026, 10, 7), ["cr2c9_duration"] = new OptionSetValue(290040001) }), "Dates of decided");
        var after = Client.Retrieve(Request, id, new ColumnSet("cr2c9_status", "cr2c9_decisionon", "cr2c9_reviewlocktoken"));
        if (after.RowVersion != before.RowVersion || !Equals(before.GetAttributeValue<DateTime>("cr2c9_decisionon"), after.GetAttributeValue<DateTime>("cr2c9_decisionon"))) throw new Exception("Rejected operation changed original record.");
        Console.WriteLine("PASS transaction rollback: original status, decision timestamp and row version unchanged.");
    }
}
