using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

namespace Leave.Plugins
{
    // Synchronous PreOperation registrations only. This hardens decisions and
    // history; entitlement validation, ledger processing and record access roles
    // are separate requirements and are not implemented by this assembly.
    internal static class Guard
    {
        internal const string Request = "cr2c9_leaverequest", Employee = "cr2c9_employee";
        internal const string Date = "cr2c9_leaverequestdate", History = "cr2c9_leavedecision";
        internal const string LockField = "cr2c9_reviewlocktoken", Expected = "cr2c9_decisionexpectedversion";
        // Dataverse injects these system-managed columns before PreOperation, even
        // when they were absent from the external update payload.
        internal static bool SystemUpdateColumn(string key) { return key == "modifiedon" || key == "modifiedby" || key == "modifiedonbehalfby" || key == "timezoneruleversionnumber" || key == "utcconversiontimezonecode"; }
        internal static InvalidPluginExecutionException Error(string text) { return new InvalidPluginExecutionException(text); }
        internal static IPluginExecutionContext Context(IServiceProvider provider)
        {
            var context = (IPluginExecutionContext)provider.GetService(typeof(IPluginExecutionContext));
            if (context.Stage != 20 || !context.IsInTransaction) throw Error("Leave safeguards must run synchronously in PreOperation.");
            return context;
        }
        internal static IOrganizationService Service(IServiceProvider provider)
        {
            // Fixed server operations only; no read results are returned to the
            // caller. Caller identity is always InitiatingUserId, not this service.
            return ((IOrganizationServiceFactory)provider.GetService(typeof(IOrganizationServiceFactory))).CreateOrganizationService(null);
        }
        internal static Entity Read(IOrganizationService service, string table, Guid id) { return service.Retrieve(table, id, new ColumnSet(true)); }
        internal static Guid Entra(object value)
        {
            Guid result; return Guid.TryParse(Convert.ToString(value), out result) ? result : Guid.Empty;
        }
        internal static bool SameFormValue(object submitted, object saved)
        {
            if (submitted == null || saved == null)
                return string.IsNullOrEmpty(Convert.ToString(submitted)) && string.IsNullOrEmpty(Convert.ToString(saved));
            if (submitted is EntityReference a && saved is EntityReference b)
                return a.LogicalName == b.LogicalName && a.Id == b.Id;
            if (submitted is OptionSetValue optionA && saved is OptionSetValue optionB)
                return optionA.Value == optionB.Value;
            if (submitted is DateTime dateA && saved is DateTime dateB)
                return dateA == dateB;
            if (submitted is decimal || submitted is double || submitted is int)
                return saved is decimal || saved is double || saved is int ?
                    Convert.ToDecimal(submitted) == Convert.ToDecimal(saved) : false;
            return Equals(submitted, saved);
        }
        internal static void ValidateClassification(IOrganizationService service, Entity target)
        {
            foreach (var pair in new[] {
                (Field: "cr2c9_department", Table: "cr2c9_department", Label: "Department"),
                (Field: "cr2c9_costcentre", Table: "cr2c9_costcentre", Label: "Cost Centre")
            }) {
                if (!target.Contains(pair.Field) || target[pair.Field] == null) continue;
                var selected = target[pair.Field] as EntityReference;
                if (selected == null || selected.LogicalName != pair.Table || selected.Id == Guid.Empty)
                    throw Error("Choose a valid " + pair.Label + " from the HR-maintained list.");
                var option = service.Retrieve(pair.Table, selected.Id, new ColumnSet("statecode"));
                if (option.GetAttributeValue<OptionSetValue>("statecode")?.Value != 0)
                    throw Error(pair.Label + " is inactive. Choose an active option.");
            }
        }
        internal static Entity Actor(IOrganizationService service, IPluginExecutionContext context)
        {
            var user = service.Retrieve("systemuser", context.InitiatingUserId, new ColumnSet("azureactivedirectoryobjectid", "fullname", "isdisabled"));
            if (user.GetAttributeValue<bool>("isdisabled") || Entra(user.GetAttributeValue<object>("azureactivedirectoryobjectid")) == Guid.Empty)
                throw Error("The signed-in Entra identity could not be verified (" + context.PrimaryEntityName + "/" + context.MessageName + ", stage " + context.Stage + ").");
            return user;
        }
        internal static void Lock(IOrganizationService service, IPluginExecutionContext context, Entity record)
        {
            if (string.IsNullOrEmpty(record.RowVersion)) throw Error("A row version is required for a leave operation.");
            var update = new Entity(record.LogicalName, record.Id) { RowVersion = record.RowVersion };
            update[LockField] = Guid.NewGuid().ToString("D");
            service.Execute(new UpdateRequest { Target = update, ConcurrencyBehavior = ConcurrencyBehavior.IfRowVersionMatches });
        }
        internal static bool VerifiedStep(IOrganizationService service, IPluginExecutionContext context, string expectedName)
        {
            var extension = context.OwningExtension;
            if (extension == null || extension.LogicalName != "sdkmessageprocessingstep" || extension.Id == Guid.Empty)
                return false;
            // Dataverse does not always populate EntityReference.Name on a nested
            // execution context. Resolve the actual registered step by ID instead
            // of treating a missing display name as an untrusted caller.
            if (extension.Name == expectedName) return true;
            var step = service.Retrieve("sdkmessageprocessingstep", extension.Id, new ColumnSet("name"));
            return step.GetAttributeValue<string>("name") == expectedName;
        }
        internal static IPluginExecutionContext TrustedParent(IOrganizationService service, IPluginExecutionContext context, string table, string message = null)
        {
            // Live Dataverse inserts ObjectModel contexts and does not propagate
            // local SharedVariables through these SYSTEM service calls. Verify
            // the server-owned registered step envelope, never client parameters.
            for (var p = context.ParentContext; p != null; p = p.ParentContext)
                if (p.Stage == 20 && p.PrimaryEntityName == table && (message == null || p.MessageName == message) &&
                    VerifiedStep(service, p, "Leave safeguard: " + table + " " + p.MessageName))
                    return p;
            return null;
        }
        internal static IPluginExecutionContext CancellationParent(IPluginExecutionContext context, int stage, string name)
        {
            for (var p = context.ParentContext; p != null; p = p.ParentContext)
                if (p.Stage == stage && p.Mode == 0 && p.IsInTransaction && p.PrimaryEntityName == "cr2c9_leavecancellation" &&
                    p.OwningExtension?.LogicalName == "sdkmessageprocessingstep" && p.OwningExtension.Name == name) return p;
            return null;
        }
        internal static bool CancellationTargetsRequest(IOrganizationService service, IPluginExecutionContext parent, Guid requestId)
        {
            if (parent == null || requestId == Guid.Empty) return false;
            EntityReference link = null;
            if (parent.MessageName == "Create")
                link = (parent.InputParameters["Target"] as Entity)?.GetAttributeValue<EntityReference>("cr2c9_leaverequest");
            else if (parent.MessageName == "Update" && parent.PrimaryEntityId != Guid.Empty)
                link = service.Retrieve("cr2c9_leavecancellation", parent.PrimaryEntityId,
                    new ColumnSet("cr2c9_leaverequest")).GetAttributeValue<EntityReference>("cr2c9_leaverequest");
            return link?.LogicalName == Request && link.Id == requestId;
        }
        internal static bool IsInternalLock(IOrganizationService service, IPluginExecutionContext context, Entity target)
        {
            if (!target.Contains(LockField) || !target.Attributes.Keys.All(k => k == LockField || k == Request + "id" || SystemUpdateColumn(k))) return false;
            // Canvas SubmitForm reaches the nested CAS update through Dataverse's
            // ObjectModel stage-30 envelope. A no-op edit arrives at depth 2;
            // a changed uniform schedule arrives at depth 3. Both omit the
            // registered PreOperation parent, so verify the exact same-record
            // lock payload and nested platform context before accepting it.
            var platform = context.ParentContext;
            if (context.Depth >= 2 && context.Depth <= 3 && platform != null && platform.Stage == 30 && platform.IsInTransaction &&
                platform.PrimaryEntityName == Request && platform.MessageName == "Update" &&
                platform.PrimaryEntityId == target.Id && platform.OwningExtension?.Name == "ObjectModel Implementation" &&
                platform.InputParameters.Contains("Target") && platform.InputParameters["Target"] is Entity parentTarget &&
                parentTarget.Id == target.Id && parentTarget.Contains(LockField) &&
                Equals(parentTarget[LockField], target[LockField]) &&
                parentTarget.Attributes.Keys.All(k => k == LockField || k == Request + "id" || SystemUpdateColumn(k)))
                return true;
            if (UniformAnnualSchedule.TrustedParent(service, context, target.Id)) return true;
            var parent = TrustedParent(service, context, Request, "Update");
            if (parent != null && parent.PrimaryEntityId == target.Id) return true;
            parent = CancellationParent(context, 20, "Leave cancellation safeguard: Create") ??
                CancellationParent(context, 20, "Leave cancellation safeguard: Update");
            if (CancellationTargetsRequest(service, parent, target.Id)) return true;
            parent = TrustedParent(service, context, Date);
            if (parent == null || (parent.MessageName != "Create" && parent.MessageName != "Update" && parent.MessageName != "Delete")) return false;
            var date = parent.InputParameters["Target"] as Entity;
            var reference = parent.InputParameters["Target"] as EntityReference;
            var request = date != null ? date.GetAttributeValue<EntityReference>("cr2c9_leaverequest") : null;
            if (request == null && parent.MessageName != "Create") {
                var saved = service.Retrieve(Date, date != null ? date.Id : reference.Id, new ColumnSet("cr2c9_leaverequest"));
                request = saved.GetAttributeValue<EntityReference>("cr2c9_leaverequest");
            }
            return request != null && request.LogicalName == Request && request.Id == target.Id;
        }
        internal static Entity RequestEmployee(IOrganizationService service, Entity request)
        {
            var reference = request.GetAttributeValue<EntityReference>("cr2c9_employeename");
            if (reference == null || reference.LogicalName != Employee || reference.Id == Guid.Empty) throw Error("The saved request must identify an Employee.");
            var employee = Read(service, Employee, reference.Id);
            if (!employee.GetAttributeValue<bool>("cr2c9_isactive")) throw Error("The employee profile is inactive.");
            if (Entra(employee.GetAttributeValue<object>("cr2c9_entrauserid")) == Guid.Empty) throw Error("The employee Entra identity is missing or invalid.");
            return employee;
        }
    }

    public sealed class LeaveRequestGuard : IPlugin
    {
        public void Execute(IServiceProvider provider)
        {
            var context = Guard.Context(provider);
            var service = Guard.Service(provider);
            if (context.PrimaryEntityName != Guard.Request) throw Guard.Error("Invalid Leave Request safeguard registration.");
            if (context.MessageName == "Delete") throw Guard.Error("Leave requests and their decision history must be preserved.");
            var target = (Entity)context.InputParameters["Target"];
            if (context.MessageName == "Update" && Guard.IsInternalLock(service, context, target)) return;
            if (context.MessageName == "Update" && target.Attributes.Keys.All(k => k == "cr2c9_status" || k == Guard.Request + "id" || Guard.SystemUpdateColumn(k)) &&
                target.GetAttributeValue<OptionSetValue>("cr2c9_status")?.Value == 5)
            {
                var accounting = Guard.CancellationParent(context, 40, "Leave future cancellation accounting");
                if (accounting?.MessageName == "Update" && Guard.CancellationTargetsRequest(service, accounting, target.Id) &&
                    Guard.Read(service, Guard.Request, target.Id).GetAttributeValue<OptionSetValue>("cr2c9_status")?.Value == 1) {
                    PublicAbsence.Remove(service, target.Id);
                    return;
                }
            }
            var actor = Guard.Actor(service, context);
            var actorId = Guard.Entra(actor.GetAttributeValue<object>("azureactivedirectoryobjectid"));
            if (context.MessageName == "Create")
            {
                if (target.GetAttributeValue<OptionSetValue>("cr2c9_status") == null || target.GetAttributeValue<OptionSetValue>("cr2c9_status").Value != 0)
                    throw Guard.Error("New leave requests must start as Pending Superior.");
                if (new[] { "cr2c9_decisionbyentraid", "cr2c9_decisionbyname", "cr2c9_decisionon", "cr2c9_decisioncomments", Guard.Expected, Guard.LockField, MaternityApprovalRules.HrVerified, PaternityApprovalRules.HrVerified }.Any(target.Contains))
                    throw Guard.Error("A new request cannot supply a decision or server operation fields.");
                var employee = Guard.RequestEmployee(service, target);
                Guard.ValidateClassification(service, target);
                if (Guard.Entra(employee.GetAttributeValue<object>("cr2c9_entrauserid")) != actorId)
                    throw Guard.Error("Employees can create requests only for their own verified profile.");
                var callerService = ((IOrganizationServiceFactory)provider.GetService(typeof(IOrganizationServiceFactory)))
                    .CreateOrganizationService(context.InitiatingUserId);
                var leaveTypeName = SubmissionValidation.Validate(target, callerService, service);
                if (MaternityApprovalRules.IsMaternity(target)) MaternityApprovalRules.RequireDates(target);
                if (leaveTypeName == "Paternity Leave") PaternityApprovalRules.RequireSubmittedDates(target);
                if (leaveTypeName == "Marriage Leave") MarriageApprovalRules.RequireSubmittedDetails(target);
                if (leaveTypeName == "Compassionate Leave") CompassionateApprovalRules.RequireSubmittedDetails(target);
                if (leaveTypeName == TrainingPolicy.TypeName) TrainingPolicy.ChargesAnnual(target);
                if (leaveTypeName == UnpaidPolicy.TypeName) UnpaidPolicy.RequireWrittenRequest(target);
                if (SickLeaveRules.IsSick(target)) SickLeaveRules.RequireTimelySubmission(target, DateTime.UtcNow);
                return;
            }
            if (context.MessageName != "Update") throw Guard.Error("Unsupported leave operation.");
            var current = Guard.Read(service, Guard.Request, target.Id);
            if (target.Attributes.Keys.All(k => k == MaternityApprovalRules.HrVerified ||
                    k == Guard.Request + "id" || Guard.SystemUpdateColumn(k)) &&
                target.Contains(MaternityApprovalRules.HrVerified)) {
                if (!MaternityApprovalRules.IsMaternity(current) ||
                    current.GetAttributeValue<OptionSetValue>("cr2c9_status")?.Value != 0 ||
                    !DecisionAuthority.IsHr(service, context.InitiatingUserId))
                    throw Guard.Error("Only HR can verify a pending Maternity Leave request.");
                var maternityEmployee = Guard.RequestEmployee(service, current);
                if (Guard.Entra(maternityEmployee.GetAttributeValue<object>("cr2c9_entrauserid")) == actorId)
                    throw Guard.Error("You cannot verify your own maternity request.");
                Guard.Lock(service, context, current);
                current = Guard.Read(service, Guard.Request, target.Id);
                if (target.GetAttributeValue<DateTime?>(MaternityApprovalRules.HrVerified).HasValue) {
                    MaternityApprovalRules.RequireDates(current);
                    target[MaternityApprovalRules.HrVerified] = DateTime.UtcNow;
                }
                return;
            }
            if (target.Attributes.Keys.All(k => k == PaternityApprovalRules.HrVerified ||
                    k == Guard.Request + "id" || Guard.SystemUpdateColumn(k)) &&
                target.Contains(PaternityApprovalRules.HrVerified)) {
                if (!PaternityApprovalRules.IsPaternity(service, current) ||
                    current.GetAttributeValue<OptionSetValue>("cr2c9_status")?.Value != 0 ||
                    !DecisionAuthority.IsHr(service, context.InitiatingUserId))
                    throw Guard.Error("Only HR can verify a pending Paternity Leave request.");
                var paternityEmployee = Guard.RequestEmployee(service, current);
                if (Guard.Entra(paternityEmployee.GetAttributeValue<object>("cr2c9_entrauserid")) == actorId)
                    throw Guard.Error("You cannot verify your own paternity request.");
                Guard.Lock(service, context, current);
                current = Guard.Read(service, Guard.Request, target.Id);
                if (target.GetAttributeValue<DateTime?>(PaternityApprovalRules.HrVerified).HasValue) {
                    var review = new Entity(Guard.Request, current.Id);
                    foreach (var pair in current.Attributes) review[pair.Key] = pair.Value;
                    review[PaternityApprovalRules.HrVerified] = DateTime.UtcNow;
                    PaternityApprovalRules.RequireReview(service, review, paternityEmployee);
                    target[PaternityApprovalRules.HrVerified] = review[PaternityApprovalRules.HrVerified];
                }
                return;
            }
            var formReadonly = new[] { "cr2c9_employeename", "cr2c9_status" };
            if (target.Contains("cr2c9_startdate") || target.Contains("cr2c9_enddate") || target.Contains("cr2c9_hoursperday") ||
                target.Contains("cr2c9_employeename") || target.Contains("cr2c9_leavetypename") || target.Contains("cr2c9_reason") ||
                target.Contains(MaternityApprovalRules.ExpectedConfinement) || target.Contains(MaternityApprovalRules.FormalNotice) ||
                target.Contains(PaternityApprovalRules.ExpectedConfinement) || target.Contains(PaternityApprovalRules.ActualConfinement) ||
                target.Contains(PaternityApprovalRules.Notice) || target.Contains(MarriageApprovalRules.RegisteredOn) ||
                target.Contains(MarriageApprovalRules.FirstMarriage) || target.Contains(CompassionateApprovalRules.Relationship) ||
                target.Contains(TrainingPolicy.Category) || target.Contains("cr2c9_department") ||
                target.Contains("cr2c9_costcentre")) {
                var editColumns = new[] { "cr2c9_startdate", "cr2c9_enddate", "cr2c9_hoursperday", "cr2c9_leavetypename", "cr2c9_reason",
                    MaternityApprovalRules.ExpectedConfinement, MaternityApprovalRules.FormalNotice,
                    PaternityApprovalRules.ExpectedConfinement, PaternityApprovalRules.ActualConfinement,
                    PaternityApprovalRules.Notice, MarriageApprovalRules.RegisteredOn,
                    MarriageApprovalRules.FirstMarriage, CompassionateApprovalRules.Relationship,
                    TrainingPolicy.Category, "cr2c9_department", "cr2c9_costcentre", Guard.Request + "id" };
                if (target.Attributes.Keys.Any(k => !editColumns.Contains(k) && !formReadonly.Contains(k) && !Guard.SystemUpdateColumn(k)))
                    throw Guard.Error("A pending leave edit may change only its type, reason, dates, duration, Department and Cost Centre.");
                foreach (var field in formReadonly.Where(target.Contains).ToArray()) {
                    if (!Guard.SameFormValue(target[field], current.Contains(field) ? current[field] : null))
                        throw Guard.Error("A pending leave edit cannot change " + field + ".");
                    target.Attributes.Remove(field);
                }
                Guard.ValidateClassification(service, target);
                Guard.Lock(service, context, current);
                current = Guard.Read(service, Guard.Request, target.Id);
                if (current.GetAttributeValue<OptionSetValue>("cr2c9_status")?.Value != 0)
                    throw Guard.Error("Only Pending Superior leave can be edited.");
                var employee = Guard.RequestEmployee(service, current);
                Guard.Lock(service, context, employee);
                employee = Guard.RequestEmployee(service, current);
                if (Guard.Entra(employee.GetAttributeValue<object>("cr2c9_entrauserid")) != actorId)
                    throw Guard.Error("Only the request's employee can edit its pending details.");
                var merged = new Entity(Guard.Request, target.Id);
                foreach (var pair in current.Attributes) merged[pair.Key] = pair.Value;
                foreach (var pair in target.Attributes) merged[pair.Key] = pair.Value;
                if (MaternityApprovalRules.IsMaternity(merged) || MaternityApprovalRules.IsMaternity(current))
                    target[MaternityApprovalRules.HrVerified] = null;
                if (PaternityApprovalRules.IsPaternity(service, merged) || PaternityApprovalRules.IsPaternity(service, current))
                    target[PaternityApprovalRules.HrVerified] = null;
                var callerService = ((IOrganizationServiceFactory)provider.GetService(typeof(IOrganizationServiceFactory)))
                    .CreateOrganizationService(context.InitiatingUserId);
                SubmissionValidation.Validate(merged, callerService, service);
                if (MaternityApprovalRules.IsMaternity(merged)) MaternityApprovalRules.RequireDates(merged);
                if (PaternityApprovalRules.IsPaternity(service, merged)) PaternityApprovalRules.RequireSubmittedDates(merged);
                if (MarriageApprovalRules.IsMarriage(merged)) MarriageApprovalRules.RequireSubmittedDetails(merged);
                if (CompassionateApprovalRules.IsCompassionate(merged)) CompassionateApprovalRules.RequireSubmittedDetails(merged);
                if (TrainingPolicy.IsTraining(service, merged)) TrainingPolicy.ChargesAnnual(merged);
                if (UnpaidPolicy.IsUnpaid(service, merged)) UnpaidPolicy.RequireWrittenRequest(merged);
                // A pending edit must not move an existing Sick request to an
                // earlier start date that was already outside the original
                // app-submission window. Changing from another leave type to
                // Sick is subject to the same original submission timestamp.
                if (SickLeaveRules.IsSick(merged)) {
                    var submitted = current.GetAttributeValue<DateTime?>("createdon");
                    if (!submitted.HasValue)
                        throw Guard.Error("The original Medical Leave app submission time is missing.");
                    SickLeaveRules.RequireTimelySubmission(merged, submitted.Value);
                }
                return;
            }
            var oldStatus = current.GetAttributeValue<OptionSetValue>("cr2c9_status");
            var nextStatus = target.GetAttributeValue<OptionSetValue>("cr2c9_status");
            var permitted = new[] { "cr2c9_status", "cr2c9_decisioncomments", "cr2c9_decisionbyentraid", "cr2c9_decisionbyname", "cr2c9_decisionon", Guard.Expected, Guard.Request + "id" };
            if (target.Attributes.Keys.Any(k => !permitted.Contains(k) && !Guard.SystemUpdateColumn(k)))
                throw Guard.Error("Submitted request details cannot be changed through a review update. Unsupported columns: " + string.Join(", ", target.Attributes.Keys.Where(k => !permitted.Contains(k) && !Guard.SystemUpdateColumn(k))));
            if (oldStatus == null || oldStatus.Value != 0 || nextStatus == null || (nextStatus.Value != 1 && nextStatus.Value != 3))
                throw Guard.Error("Only Pending Superior can change to Approved or Rejected. Existing decisions cannot be repeated or overwritten.");
            // Only categories with a registered same-transaction deduction may
            // be approved. The Sick branch also requires timely app submission
            // and an uploaded certificate before the decision is saved.
            var hospitalisation = current.GetAttributeValue<EntityReference>("cr2c9_leavetypename")?.Id ==
                new Guid("91f9367f-5bd7-4a06-a750-124c36d5729f");
            var maternity = MaternityApprovalRules.IsMaternity(current);
            var paternity = nextStatus.Value == 1 && !UniformAnnualSchedule.IsAnnual(current) && !SickLeaveRules.IsSick(current) &&
                !hospitalisation && !maternity && PaternityApprovalRules.IsPaternity(service, current);
            var marriage = MarriageApprovalRules.IsMarriage(current);
            var compassionate = CompassionateApprovalRules.IsCompassionate(current);
            var emergency = EmergencyApprovalRules.IsEmergency(service, current);
            var training = TrainingPolicy.IsTraining(service, current);
            var unpaid = UnpaidPolicy.IsUnpaid(service, current);
            if (nextStatus.Value == 1 && !UniformAnnualSchedule.IsAnnual(current) &&
                !SickLeaveRules.IsSick(current) && !hospitalisation && !maternity && !paternity && !marriage && !compassionate && !emergency && !training && !unpaid)
                throw Guard.Error("This leave type cannot be approved yet. Its eligibility and balance rules are still being configured; contact HR.");
            if (nextStatus.Value == 1 && maternity) MaternityApprovalRules.RequireReview(service, current);
            if (nextStatus.Value == 1 && SickLeaveRules.IsSick(current)) {
                var submitted = current.GetAttributeValue<DateTime?>("createdon");
                if (!submitted.HasValue) throw Guard.Error("The original Medical Leave app submission time is missing.");
                SickLeaveRules.RequireTimelySubmission(current, submitted.Value);
                SickLeaveRules.RequireCertificate(service, current.Id);
            }
            if (nextStatus.Value == 1 && hospitalisation)
                SickLeaveRules.RequireCertificate(service, current.Id, "Hospitalisation Leave");
            if (nextStatus.Value == 1 && emergency)
                EmergencyApprovalRules.RequireEvidence(service, current.Id);
            if (nextStatus.Value == 1 && training)
                TrainingApprovalRules.RequireSavedDates(service, current);
            if (nextStatus.Value == 1 && unpaid) {
                UnpaidPolicy.RequireWrittenRequest(current);
                TrainingApprovalRules.RequireSavedDates(service, current, UnpaidPolicy.TypeName);
            }
            if (string.IsNullOrEmpty(current.RowVersion) || target.GetAttributeValue<string>(Guard.Expected) != current.RowVersion)
                throw Guard.Error("This request changed after review. Refresh it before deciding.");
            Guard.Lock(service, context, current); // CAS + transaction lock serializes competing decisions/date edits.
            var requestor = Guard.RequestEmployee(service, current);
            Guard.Lock(service, context, requestor);
            requestor = Guard.Read(service, Guard.Employee, requestor.Id);
            if (nextStatus.Value == 1 && paternity)
                PaternityApprovalRules.RequireReview(service, current, requestor);
            if (nextStatus.Value == 1 && marriage)
                MarriageApprovalRules.RequireApproval(service, current, requestor);
            if (nextStatus.Value == 1 && compassionate)
                CompassionateApprovalRules.RequireApproval(service, current);
            var superior = requestor.GetAttributeValue<EntityReference>("cr2c9_superior");
            if (Guard.Entra(requestor.GetAttributeValue<object>("cr2c9_entrauserid")) == actorId || (superior != null && superior.Id == requestor.Id))
                throw Guard.Error("You cannot decide your own leave request.");
            if (!DecisionAuthority.IsHr(service, context.InitiatingUserId))
            {
                if (superior == null || superior.LogicalName != Guard.Employee || superior.Id == Guid.Empty)
                    throw Guard.Error("No latest synced Superior is assigned.");
                var manager = Guard.Read(service, Guard.Employee, superior.Id);
                Guard.Lock(service, context, manager);
                manager = Guard.Read(service, Guard.Employee, manager.Id);
                if (!manager.GetAttributeValue<bool>("cr2c9_isactive") ||
                    Guard.Entra(manager.GetAttributeValue<object>("cr2c9_entrauserid")) != actorId)
                    throw Guard.Error("Only the employee's latest synced Superior or a verified HR member can decide this request.");
            }
            var now = DateTime.UtcNow;
            var name = actor.GetAttributeValue<string>("fullname");
            if (string.IsNullOrWhiteSpace(name)) throw Guard.Error("Decision-maker name could not be verified.");
            target.Attributes.Remove(Guard.Expected); // Transport input; never persist it as a reusable authority stamp.
            target["cr2c9_decisionbyentraid"] = actorId.ToString("D");
            target["cr2c9_decisionbyname"] = name;
            target["cr2c9_decisionon"] = now;
            var history = new Entity(Guard.History);
            var operation = current.Id.ToString("D") + ":decision";
            history["cr2c9_name"] = operation;
            history["cr2c9_operationkey"] = operation;
            history["cr2c9_leaverequest"] = current.ToEntityReference();
            history["cr2c9_outcome"] = new OptionSetValue(nextStatus.Value);
            history["cr2c9_actorentraid"] = actorId.ToString("D");
            history["cr2c9_actorname"] = name;
            history["cr2c9_decidedon"] = now;
            history["cr2c9_comments"] = target.GetAttributeValue<string>("cr2c9_decisioncomments");
            var requestOwner = service.Retrieve(Guard.Request, current.Id,
                new ColumnSet("ownerid")).GetAttributeValue<EntityReference>("ownerid");
            if (requestOwner == null || requestOwner.Id == Guid.Empty)
                throw Guard.Error("The request owner could not be verified for decision history.");
            history["ownerid"] = requestOwner;
            service.Create(history); // Same transaction: failure rolls back the decision and locks.
            if (nextStatus.Value == 1)
                PublicAbsence.Publish(service, current, requestor);
        }
    }

    public sealed class LeaveDateGuard : IPlugin
    {
        public void Execute(IServiceProvider provider)
        {
            var context = Guard.Context(provider);
            if (context.PrimaryEntityName != Guard.Date) throw Guard.Error("Invalid Leave Request Date safeguard registration.");
            var service = Guard.Service(provider);
            var target = context.InputParameters["Target"] as Entity;
            var reference = context.InputParameters["Target"] as EntityReference;
            var current = context.MessageName == "Create" ? new Entity(Guard.Date) : Guard.Read(service, Guard.Date, target != null ? target.Id : reference.Id);
            var merged = new Entity(Guard.Date, current.Id);
            foreach (var value in current.Attributes) merged[value.Key] = value.Value;
            if (target != null) foreach (var value in target.Attributes) merged[value.Key] = value.Value;
            var parent = merged.GetAttributeValue<EntityReference>("cr2c9_leaverequest");
            if (parent == null || parent.LogicalName != Guard.Request || parent.Id == Guid.Empty) throw Guard.Error("Leave Request is required for each date entry.");
            var oldParent = current.GetAttributeValue<EntityReference>("cr2c9_leaverequest");
            if (oldParent != null && parent.Id != oldParent.Id) throw Guard.Error("A date entry cannot be moved to another request.");
            var request = Guard.Read(service, Guard.Request, parent.Id);
            Guard.Lock(service, context, request);
            request = Guard.Read(service, Guard.Request, parent.Id);
            var status = request.GetAttributeValue<OptionSetValue>("cr2c9_status");
            if (status == null || status.Value != 0) throw Guard.Error("Dates of decided and historical requests cannot be changed.");
            var employee = Guard.RequestEmployee(service, request);
            Guard.Lock(service, context, employee);
            employee = Guard.RequestEmployee(service, request);
            var trustedUniformWrite = UniformAnnualSchedule.TrustedParent(service, context, request.Id) ||
                UniformAnnualSchedule.TrustedDateRegeneration(service, context);
            var actor = trustedUniformWrite ? null : Guard.Actor(service, context);
            if (!trustedUniformWrite && Guard.Entra(employee.GetAttributeValue<object>("cr2c9_entrauserid")) != Guard.Entra(actor.GetAttributeValue<object>("azureactivedirectoryobjectid")))
                throw Guard.Error("Only the request's employee can change its pending date entries.");
            if ((UniformAnnualSchedule.NumericHours(request) == 4 ||
                 UniformAnnualSchedule.NumericHours(request) == 8) &&
                !trustedUniformWrite)
                throw Guard.Error("Use the leave request form to edit dates and duration; individual date changes are not allowed.");
            if (context.MessageName == "Delete") return;
            var duration = merged.GetAttributeValue<OptionSetValue>("cr2c9_duration");
            if (duration == null || (duration.Value != 290040000 && duration.Value != 290040001)) throw Guard.Error("Duration must be Half Day (4 hours) or Full Day (8 hours).");
            if (!merged.Contains("cr2c9_leavedate") || !(merged["cr2c9_leavedate"] is DateTime) || ((DateTime)merged["cr2c9_leavedate"]).TimeOfDay != TimeSpan.Zero)
                throw Guard.Error("A calendar Leave Date without a time is required.");
            if (!request.Contains("cr2c9_startdate") || !request.Contains("cr2c9_enddate") ||
                !(request["cr2c9_startdate"] is DateTime) || !(request["cr2c9_enddate"] is DateTime))
                throw Guard.Error("The request needs a Start Date and End Date before adding leave dates.");
            var start = ((DateTime)request["cr2c9_startdate"]).Date;
            var end = ((DateTime)request["cr2c9_enddate"]).Date;
            var leaveDate = (DateTime)merged["cr2c9_leavedate"];
            if (end < start || leaveDate < start || leaveDate > end)
                throw Guard.Error("Each Leave Date must fall within the request's Start Date and End Date.");
            if (context.MessageName != "Create" && context.MessageName != "Update") throw Guard.Error("Unsupported date-entry operation.");
        }
    }

    public sealed class LeaveDecisionHistoryGuard : IPlugin
    {
        public void Execute(IServiceProvider provider)
        {
            var context = Guard.Context(provider);
            if (context.PrimaryEntityName != Guard.History)
                throw Guard.Error("Decision history guard received " + context.PrimaryEntityName + ".");
            var service = Guard.Service(provider);
            if (context.MessageName == "Update") {
                var ownerChange = (Entity)context.InputParameters["Target"];
                if (!ownerChange.Contains("ownerid") ||
                    ownerChange.Attributes.Keys.Any(k => k != "ownerid" &&
                        k != "owningbusinessunit" && k != Guard.History + "id" &&
                        !Guard.SystemUpdateColumn(k)))
                    throw Guard.Error("Decision history owner reassignment contains unsupported columns: " +
                        string.Join(", ", ownerChange.Attributes.Keys.Where(k => k != "ownerid" &&
                            k != "owningbusinessunit" && k != Guard.History + "id" &&
                            !Guard.SystemUpdateColumn(k))));
                var newOwner = ownerChange.GetAttributeValue<EntityReference>("ownerid");
                var history = service.Retrieve(Guard.History, context.PrimaryEntityId,
                    new ColumnSet("cr2c9_leaverequest"));
                var requestRef = history.GetAttributeValue<EntityReference>("cr2c9_leaverequest");
                if (requestRef?.LogicalName != Guard.Request || requestRef.Id == Guid.Empty)
                    throw Guard.Error("Decision history has no verified request.");
                var parentRequest = service.Retrieve(Guard.Request, requestRef.Id,
                    new ColumnSet("cr2c9_employeename", "ownerid"));
                var employee = parentRequest.GetAttributeValue<EntityReference>("cr2c9_employeename");
                var requestOwner = parentRequest.GetAttributeValue<EntityReference>("ownerid");
                var trusted = false;
                for (var p = context.ParentContext; p != null; p = p.ParentContext)
                    if (p.Stage == 40 && p.Mode == 0 && p.IsInTransaction &&
                        p.PrimaryEntityName == Guard.Employee && p.MessageName == "Update" &&
                        p.PrimaryEntityId == employee?.Id &&
                        Guard.VerifiedStep(service, p, "Leave Superior access: cr2c9_employee Update")) {
                        trusted = true; break;
                    }
                if (!trusted || newOwner?.Id != requestOwner?.Id ||
                    newOwner?.LogicalName != requestOwner?.LogicalName)
                    throw Guard.Error("Decision history owner can change only during a verified Superior reassignment.");
                return;
            }
            if (context.MessageName != "Create")
                throw Guard.Error("Decision history does not support " + context.MessageName + ".");
            var target = (Entity)context.InputParameters["Target"];
            var parent = Guard.TrustedParent(service, context, Guard.Request, "Update");
            var request = target.GetAttributeValue<EntityReference>("cr2c9_leaverequest");
            var operation = target.GetAttributeValue<string>("cr2c9_operationkey");
            if (parent == null || parent.MessageName != "Update" || parent.PrimaryEntityName != Guard.Request ||
                request == null || request.Id != parent.PrimaryEntityId || operation != request.Id.ToString("D") + ":decision")
                throw Guard.Error("Decision history can be created only by the server decision operation.");
        }
    }
}
