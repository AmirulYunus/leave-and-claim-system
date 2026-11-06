using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Leave.Plugins;

namespace Leave.BalanceGuards
{
    internal static class CancellationContract
    {
        internal const string Table = "cr2c9_leavecancellation";
        internal const string GuardStep = "Leave cancellation safeguard: ";
        internal const string AccountingStep = "Leave future cancellation accounting";
        internal const string PreImage = "BeforeCancellationDecision";
        internal const string Expected = "cr2c9_expectedversion";
        internal const string Lock = "cr2c9_locktoken";
        internal static readonly Guid MarriageType = new Guid("75aa666a-2b4e-49e5-8766-0dc59e6fc25f");
        internal static readonly Guid MaternityType = new Guid("7b3543af-6be9-49f2-a222-724297ef4f09");
        internal static readonly Guid PaternityType = new Guid("8e025448-ff94-4204-90e2-df4d3b3ca8d8");
        internal static readonly Guid CompassionateType = new Guid("e85a19e9-c65c-48a3-8352-63b301521efd");
        internal static bool IsSpecialType(Guid type) => type==MarriageType || type==MaternityType || type==PaternityType || type==CompassionateType;
        internal static AnnualLeaveDate[] SpecialDates(IOrganizationService s, Entity request, Guid type) {
            if (!IsSpecialType(type)) throw Guard.Error("The leave type has no special-leave cancellation rule.");
            var start=request.GetAttributeValue<DateTime?>("cr2c9_startdate");
            var end=request.GetAttributeValue<DateTime?>("cr2c9_enddate");
            var count=type==MarriageType || type==CompassionateType?3:type==PaternityType?7:98;
            if (!start.HasValue || !end.HasValue || start.Value.TimeOfDay!=TimeSpan.Zero ||
                end.Value.TimeOfDay!=TimeSpan.Zero || start.Value>DateTime.MaxValue.AddDays(-(count-1)) ||
                end.Value!=start.Value.AddDays(count-1))
                throw Guard.Error("The approved special leave does not have its required consecutive calendar dates.");
            var dates=ApprovalContract.ReadDates(s,request).OrderBy(d=>d.Date).ToArray();
            if (dates.Length!=count || dates.Where((d,i)=>d.Date!=start.Value.AddDays(i) || d.Hours!=8).Any())
                throw Guard.Error("Every approved special-leave date must have one saved full-day entry.");
            return dates;
        }
        internal static bool CreateSystemColumn(string k) => GrantContract.SystemColumn(k) ||
            k=="owninguser" || k=="owningbusinessunit" || k=="ownerid" || k=="createdon" ||
            k=="createdby" || k=="statecode" || k=="statuscode" || k==Table+"id";
        internal static bool Step(IPluginExecutionContext c, int stage, string name) => c != null &&
            c.Stage == stage && c.Mode == 0 && c.IsInTransaction && c.PrimaryEntityName == Table &&
            c.OwningExtension?.LogicalName == "sdkmessageprocessingstep" && c.OwningExtension.Name == name;
        internal static IPluginExecutionContext Parent(IPluginExecutionContext c) {
            for (var p = c.ParentContext; p != null; p = p.ParentContext)
                if (Step(p,40,AccountingStep) && p.MessageName == "Update" && p.InitiatingUserId == c.InitiatingUserId)
                    return p;
            return null;
        }
        internal static IPluginExecutionContext GuardParent(IPluginExecutionContext c) {
            for (var p = c.ParentContext; p != null; p = p.ParentContext)
                if (Step(p,20,GuardStep+p.MessageName) && p.InitiatingUserId == c.InitiatingUserId) return p;
            return null;
        }
        internal static void Future(IOrganizationService s, Entity request) {
            var leaveType=ApprovalContract.Ref(request,"cr2c9_leavetypename","cr2c9_leavetype").Id;
            var emergency=leaveType!=GrantContract.AnnualType &&
                EmergencyApprovalRules.IsEmergency(s,request,true,false);
            var training=leaveType!=GrantContract.AnnualType && !emergency &&
                TrainingPolicy.IsTraining(s,request,false);
            var unpaid=leaveType!=GrantContract.AnnualType && !emergency && !training &&
                UnpaidPolicy.IsUnpaid(s,request,false);
            if (request.GetAttributeValue<OptionSetValue>("cr2c9_status")?.Value != 1 ||
                leaveType != GrantContract.AnnualType && leaveType != SickContract.SickType &&
                leaveType != HospitalContract.HospitalType && !IsSpecialType(leaveType) && !emergency && !training && !unpaid)
                throw Guard.Error("This approved leave type has no future-cancellation rule.");
            var dates=IsSpecialType(leaveType) ? SpecialDates(s,request,leaveType) :
                leaveType==SickContract.SickType || leaveType==HospitalContract.HospitalType || emergency || training || unpaid
                    ? ApprovalContract.SickDates(s,request) : ApprovalContract.ReadDates(s,request);
            if (training) TrainingPolicy.ChargesAnnual(request);
            var malaysiaToday=EntitlementCalculator.MalaysiaDate(DateTime.UtcNow);
            if (dates.Any(d=>d.Date<=malaysiaToday))
                throw Guard.Error("Cancellation of leave already started or taken is on hold; every selected date must be future.");
        }
        internal static bool HrCorrectionCandidate(IOrganizationService s, Entity request, DateTime today) {
            var type=ApprovalContract.Ref(request,"cr2c9_leavetypename","cr2c9_leavetype").Id;
            if (request.GetAttributeValue<OptionSetValue>("cr2c9_status")?.Value!=1 ||
                type==GrantContract.AnnualType || type==SickContract.SickType ||
                type==HospitalContract.HospitalType || IsSpecialType(type) ||
                !EmergencyApprovalRules.IsEmergency(s,request,true,false)) return false;
            return ApprovalContract.SickDates(s,request).Any(d=>d.Date<=today);
        }
        internal static bool Scope(IOrganizationService s, Entity request, Guid actorUserId,
            string reason, DateTime today) {
            if (HrCorrectionCandidate(s,request,today)) {
                if (!DecisionAuthority.IsHr(s,actorUserId))
                    throw Guard.Error("Only a verified HR member can correct approved Emergency Leave after it started.");
                if (string.IsNullOrWhiteSpace(reason))
                    throw Guard.Error("An HR Emergency Leave correction requires a recorded reason.");
                return true;
            }
            Future(s,request);
            return false;
        }
        internal static Entity Request(IOrganizationService s, Entity cancellation) {
            var link=ApprovalContract.Ref(cancellation,"cr2c9_leaverequest",Guard.Request);
            return s.Retrieve(Guard.Request,link.Id,new ColumnSet("cr2c9_status","cr2c9_employeename","cr2c9_leavetypename",
                "cr2c9_startdate","cr2c9_enddate","cr2c9_leaverequestnumber",TrainingPolicy.Category));
        }
        internal static Guid Actor(IOrganizationService s, Guid userId) {
            if (userId==Guid.Empty) throw Guard.Error("A signed-in cancellation caller is required.");
            var user=s.Retrieve("systemuser",userId,new ColumnSet("azureactivedirectoryobjectid","isdisabled"));
            var identity=ApprovalContract.Identity(user.GetAttributeValue<object>("azureactivedirectoryobjectid"));
            if (user.GetAttributeValue<bool>("isdisabled") || identity==Guid.Empty)
                throw Guard.Error("A verified active Entra identity is required for cancellation.");
            return identity;
        }
        internal static Entity Employee(IOrganizationService s, Entity request) {
            var e=s.Retrieve(Guard.Employee,ApprovalContract.Ref(request,"cr2c9_employeename",Guard.Employee).Id,
                new ColumnSet("cr2c9_isactive","statecode","cr2c9_entrauserid","cr2c9_superior"));
            if (!e.GetAttributeValue<bool>("cr2c9_isactive") || e.GetAttributeValue<OptionSetValue>("statecode")?.Value!=0 ||
                ApprovalContract.Identity(e.GetAttributeValue<object>("cr2c9_entrauserid"))==Guid.Empty)
                throw Guard.Error("The cancellation employee must be active with a verified identity.");
            return e;
        }
        internal static void Reviewer(IOrganizationService s, Entity employee, Guid actor, Guid actorUserId) {
            var employeeIdentity=ApprovalContract.Identity(employee.GetAttributeValue<object>("cr2c9_entrauserid"));
            if (employeeIdentity==actor)
                throw Guard.Error("Employees cannot decide their own cancellation.");
            if (DecisionAuthority.IsHr(s,actorUserId)) return;
            var managerRef=ApprovalContract.Ref(employee,"cr2c9_superior",Guard.Employee);
            if (managerRef.Id==employee.Id)
                throw Guard.Error("Employees cannot decide their own cancellation.");
            var manager=s.Retrieve(Guard.Employee,managerRef.Id,new ColumnSet("cr2c9_isactive","statecode","cr2c9_entrauserid"));
            if (!manager.GetAttributeValue<bool>("cr2c9_isactive") || manager.GetAttributeValue<OptionSetValue>("statecode")?.Value!=0 ||
                ApprovalContract.Identity(manager.GetAttributeValue<object>("cr2c9_entrauserid"))!=actor)
                throw Guard.Error("Only the latest synced Superior can decide this cancellation.");
        }
        internal static void LockRow(IOrganizationService s, Entity row, string field) {
            if (string.IsNullOrWhiteSpace(row.RowVersion)) throw Guard.Error("A cancellation row version is required.");
            var update=new Entity(row.LogicalName,row.Id){RowVersion=row.RowVersion};
            update[field]=Guid.NewGuid().ToString("D");
            s.Execute(new UpdateRequest{Target=update,ConcurrencyBehavior=ConcurrencyBehavior.IfRowVersionMatches});
        }
        internal static Entity Decided(IOrganizationService s, IPluginExecutionContext c, bool nested) {
            if (!Step(c,40,AccountingStep) || c.MessageName!="Update" || !c.PreEntityImages.Contains(PreImage) ||
                c.PreEntityImages[PreImage].GetAttributeValue<OptionSetValue>("cr2c9_status")?.Value!=0 ||
                !(c.InputParameters["Target"] is Entity target) || target.Id!=c.PrimaryEntityId ||
                target.GetAttributeValue<OptionSetValue>("cr2c9_status")?.Value!=1 ||
                (!nested && c.UserId!=c.InitiatingUserId))
                throw Guard.Error("A new verified cancellation decision is required.");
            var row=s.Retrieve(Table,c.PrimaryEntityId,new ColumnSet("cr2c9_status","cr2c9_leaverequest",
                "cr2c9_decisionbyentraid","cr2c9_decisionon","cr2c9_reason"));
            var actor=Actor(s,nested?c.UserId:c.InitiatingUserId);
            if (row.GetAttributeValue<OptionSetValue>("cr2c9_status")?.Value!=1 ||
                ApprovalContract.Identity(row.GetAttributeValue<object>("cr2c9_decisionbyentraid"))!=actor ||
                !row.Contains("cr2c9_decisionon")) throw Guard.Error("Saved cancellation decision identity is invalid.");
            var request=Request(s,row);
            Scope(s,request,nested?c.UserId:c.InitiatingUserId,
                row.GetAttributeValue<string>("cr2c9_reason"),
                EntitlementCalculator.MalaysiaDate(DateTime.UtcNow));
            Reviewer(s,Employee(s,request),actor,nested?c.UserId:c.InitiatingUserId);
            return row;
        }
    }

    public sealed class FutureCancellationGuard : IPlugin
    {
        public void Execute(IServiceProvider provider) {
            var c=(IPluginExecutionContext)provider.GetService(typeof(IPluginExecutionContext));
            if (!CancellationContract.Step(c,20,CancellationContract.GuardStep+c.MessageName))
                throw Guard.Error("Invalid cancellation safeguard registration.");
            if (c.MessageName=="Delete") throw Guard.Error("Cancellation history cannot be deleted.");
            var target=c.InputParameters["Target"] as Entity;
            if (target==null || target.LogicalName!=CancellationContract.Table) throw Guard.Error("Invalid cancellation target.");
            var ownParent=CancellationContract.GuardParent(c);
            if (c.MessageName=="Update" && target.Contains(CancellationContract.Lock) &&
                target.Attributes.Keys.All(k=>k==CancellationContract.Lock || k==CancellationContract.Table+"id" || GrantContract.SystemColumn(k)) &&
                ownParent?.PrimaryEntityId==target.Id && ownParent.MessageName=="Update") return;
            var s=GrantContract.System(provider);
            var actor=CancellationContract.Actor(s,c.InitiatingUserId);
            if (c.MessageName=="Create") {
                if (target.GetAttributeValue<OptionSetValue>("cr2c9_status")?.Value!=0 ||
                    string.IsNullOrWhiteSpace(target.GetAttributeValue<string>("cr2c9_reason")))
                    throw Guard.Error("A future cancellation needs Pending Superior status and a reason.");
                var unexpected=target.Attributes.Keys.Where(k=>k!="cr2c9_status" && k!="cr2c9_reason" &&
                    k!="cr2c9_leaverequest" && k!="cr2c9_name" && !CancellationContract.CreateSystemColumn(k)).ToArray();
                if(unexpected.Length!=0) throw Guard.Error("Unsupported cancellation creation columns: "+string.Join(", ",unexpected));
                var owner=target.GetAttributeValue<EntityReference>("ownerid");
                if(owner!=null && (owner.LogicalName!="systemuser" || owner.Id!=c.InitiatingUserId))
                    throw Guard.Error("A cancellation must be owned by its requesting employee's signed-in account.");
                var originalRequest=CancellationContract.Request(s,target);
                var correction=CancellationContract.Scope(s,originalRequest,c.InitiatingUserId,
                    target.GetAttributeValue<string>("cr2c9_reason"),
                    EntitlementCalculator.MalaysiaDate(DateTime.UtcNow));
                var requestor=CancellationContract.Employee(s,originalRequest);
                if (correction) CancellationContract.Reviewer(s,requestor,actor,c.InitiatingUserId);
                else if (ApprovalContract.Identity(requestor.GetAttributeValue<object>("cr2c9_entrauserid"))!=actor)
                    throw Guard.Error("Only the request's employee can ask to cancel future leave.");
                CancellationContract.LockRow(s,originalRequest,Guard.LockField);
                var q=new QueryExpression(CancellationContract.Table){ColumnSet=new ColumnSet("cr2c9_status"),TopCount=1};
                q.Criteria.AddCondition("cr2c9_leaverequest",ConditionOperator.Equal,originalRequest.Id);
                q.Criteria.AddCondition("cr2c9_status",ConditionOperator.In,new object[]{0,1});
                if (s.RetrieveMultiple(q).Entities.Count!=0)
                    throw Guard.Error("A pending or approved cancellation already exists for this request.");
                target["cr2c9_name"]=(correction?"HR correction of ":"Cancellation of ")+
                    (originalRequest.GetAttributeValue<string>("cr2c9_leaverequestnumber")??originalRequest.Id.ToString("D"));
                return;
            }
            if (c.MessageName!="Update") throw Guard.Error("Unsupported cancellation operation.");
            var saved=s.Retrieve(CancellationContract.Table,target.Id,new ColumnSet(true));
            var next=target.GetAttributeValue<OptionSetValue>("cr2c9_status")?.Value;
            if (saved.GetAttributeValue<OptionSetValue>("cr2c9_status")?.Value!=0 || (next!=1 && next!=3) ||
                target.Attributes.Keys.Any(k=>k!="cr2c9_status" && k!="cr2c9_decisioncomments" && k!=CancellationContract.Expected && k!=CancellationContract.Table+"id" && !GrantContract.SystemColumn(k)))
                throw Guard.Error("Only a pending cancellation can be approved or rejected; its details are immutable.");
            if (target.GetAttributeValue<string>(CancellationContract.Expected)!=saved.RowVersion)
                throw Guard.Error("The cancellation changed after review. Refresh before deciding.");
            CancellationContract.LockRow(s,saved,CancellationContract.Lock);
            var request=CancellationContract.Request(s,saved);
            CancellationContract.LockRow(s,request,Guard.LockField);
            CancellationContract.Scope(s,request,c.InitiatingUserId,
                saved.GetAttributeValue<string>("cr2c9_reason"),
                EntitlementCalculator.MalaysiaDate(DateTime.UtcNow));
            var employee=CancellationContract.Employee(s,request);
            CancellationContract.LockRow(s,employee,Guard.LockField);
            employee=CancellationContract.Employee(s,request);
            CancellationContract.Reviewer(s,employee,actor,c.InitiatingUserId);
            var user=s.Retrieve("systemuser",c.InitiatingUserId,new ColumnSet("fullname"));
            var name=user.GetAttributeValue<string>("fullname");
            if (string.IsNullOrWhiteSpace(name)) throw Guard.Error("Cancellation decision-maker name is required.");
            target.Attributes.Remove(CancellationContract.Expected);
            target["cr2c9_decisionbyentraid"]=actor.ToString("D");
            target["cr2c9_decisionbyname"]=name;
            target["cr2c9_decisionon"]=DateTime.UtcNow;
        }
    }

    public sealed class FutureCancellationAccounting : IPlugin
    {
        public void Execute(IServiceProvider provider) {
            var c=(IPluginExecutionContext)provider.GetService(typeof(IPluginExecutionContext));
            if (!CancellationContract.Step(c,40,CancellationContract.AccountingStep))
                throw Guard.Error("Invalid cancellation accounting registration.");
            var target=c.InputParameters["Target"] as Entity;
            if (target?.GetAttributeValue<OptionSetValue>("cr2c9_status")?.Value!=1) return;
            var s=GrantContract.System(provider);
            var cancellation=CancellationContract.Decided(s,c,false);
            var request=CancellationContract.Request(s,cancellation);
            var leaveType=ApprovalContract.Ref(request,"cr2c9_leavetypename","cr2c9_leavetype").Id;
            if (CancellationContract.IsSpecialType(leaveType)) {
                // Special leave has no Annual/Sick/Hospitalisation balance debit.
                // The request's guarded status update removes its public absence
                // in this same transaction. No ledger reversal may be invented.
                CancellationContract.SpecialDates(s,request,leaveType);
                s.Update(new Entity(Guard.Request,request.Id){["cr2c9_status"]=new OptionSetValue(5)});
                return;
            }
            var emergency=leaveType!=GrantContract.AnnualType &&
                EmergencyApprovalRules.IsEmergency(s,request,true,false);
            var training=leaveType!=GrantContract.AnnualType && !emergency &&
                TrainingPolicy.IsTraining(s,request,false);
            var unpaid=leaveType!=GrantContract.AnnualType && !emergency && !training &&
                UnpaidPolicy.IsUnpaid(s,request,false);
            if (training && !TrainingPolicy.ChargesAnnual(request)) {
                // Company-required training has no Annual debit to reverse.
                ApprovalContract.SickDates(s,request);
                s.Update(new Entity(Guard.Request,request.Id){["cr2c9_status"]=new OptionSetValue(5)});
                return;
            }
            if (unpaid) {
                // Unpaid Leave has no balance debit to reverse.
                ApprovalContract.SickDates(s,request);
                s.Update(new Entity(Guard.Request,request.Id){["cr2c9_status"]=new OptionSetValue(5)});
                return;
            }
            var annual=leaveType==GrantContract.AnnualType || emergency || training;
            var balanceType=annual ? GrantContract.AnnualType : leaveType;
            var dates=annual && !emergency && !training ? ApprovalContract.ReadDates(s,request) : ApprovalContract.SickDates(s,request);
            var employee=ApprovalContract.Ref(request,"cr2c9_employeename",Guard.Employee).Id;
            var periods=dates.Select(d=>d.Date.Year).Distinct().OrderBy(y=>y).Select(y=>{
                var rows=GrantContract.Periods(s,employee,new DateTime(y,1,1),new DateTime(y,12,31),balanceType);
                if (rows.Length!=1) throw Guard.Error("The approved leave's calendar balance is missing.");
                return ApprovalContract.Period(rows.Single(),employee,balanceType);
            }).ToArray();
            try {
                if (annual) new BalancePersistence(s,c).ApplyApprovedAnnualCancellationWithCarry(
                    employee,balanceType,request.Id,cancellation.Id,dates,periods);
                else new BalancePersistence(s,c).ApplyApprovedAnnualCancellation(
                    employee,balanceType,request.Id,cancellation.Id,dates,periods);
            }
            catch(InvalidOperationException e){throw Guard.Error(e.Message);}
            catch(ArgumentException e){throw Guard.Error(e.Message);}
            s.Update(new Entity(Guard.Request,request.Id){["cr2c9_status"]=new OptionSetValue(5)});
        }
    }
}
