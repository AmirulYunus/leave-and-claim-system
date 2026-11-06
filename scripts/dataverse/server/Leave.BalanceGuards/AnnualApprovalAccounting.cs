using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Leave.Plugins;

namespace Leave.BalanceGuards
{
    internal static class ApprovalContract
    {
        internal const string Step = "Leave annual approval accounting";
        internal const string PreImage = "BeforeApproval";
        internal const string Dates = "cr2c9_leaverequestdate", History = "cr2c9_leavedecision";
        internal static bool Envelope(IPluginExecutionContext c) => c != null &&
            c.Stage == 40 && c.Mode == 0 && c.IsInTransaction && c.MessageName == "Update" &&
            c.PrimaryEntityName == Guard.Request && c.PrimaryEntityId != Guid.Empty &&
            c.OwningExtension?.LogicalName == "sdkmessageprocessingstep" && c.OwningExtension.Name == Step;
        internal static IPluginExecutionContext Parent(IPluginExecutionContext c) {
            for (var p = c.ParentContext; p != null; p = p.ParentContext)
                if (Envelope(p) && p.InitiatingUserId == c.InitiatingUserId) return p;
            return null;
        }
        internal static Guid Identity(object value) => Guid.TryParse(Convert.ToString(value), out var id) ? id : Guid.Empty;
        internal static EntityReference Ref(Entity row, string field, string table) {
            var r = row.GetAttributeValue<EntityReference>(field);
            if (r == null || r.Id == Guid.Empty || r.LogicalName != table) throw Guard.Error("Invalid saved " + field + " reference.");
            return r;
        }
        internal static Entity ApprovedRequest(IOrganizationService s, IPluginExecutionContext c, bool nestedWrite = false) {
            if (!Envelope(c) || !c.PreEntityImages.Contains(PreImage) ||
                c.PreEntityImages[PreImage].GetAttributeValue<OptionSetValue>("cr2c9_status")?.Value != 0 ||
                !(c.InputParameters["Target"] is Entity t) || t.Id != c.PrimaryEntityId ||
                t.GetAttributeValue<OptionSetValue>("cr2c9_status")?.Value != 1)
                throw Guard.Error("Annual deductions require a new authorized approval transaction.");
            if (!nestedWrite && c.UserId != c.InitiatingUserId)
                throw Guard.Error("Annual accounting must run as the original approval caller.");
            var r = s.Retrieve(Guard.Request, c.PrimaryEntityId, new ColumnSet("cr2c9_status", "cr2c9_employeename", "cr2c9_leavetypename",
                "cr2c9_startdate", "cr2c9_enddate", TrainingPolicy.Category,
                "cr2c9_decisionbyentraid", "cr2c9_decisionon"));
            // SYSTEM SDK writes can expose SYSTEM as the nested parent's
            // InitiatingUserId. The non-impersonated registered approval step
            // retains its original UserId, which is checked against the
            // server-stamped decision and the employee's current Superior.
            var actorId = nestedWrite ? c.UserId : c.InitiatingUserId;
            var actor = s.Retrieve("systemuser", actorId, new ColumnSet("azureactivedirectoryobjectid", "isdisabled"));
            var identity = Identity(actor.GetAttributeValue<object>("azureactivedirectoryobjectid"));
            if (actor.GetAttributeValue<bool>("isdisabled") || identity == Guid.Empty)
                throw Guard.Error("The approval caller must have an active verified Entra identity.");
            if (r.GetAttributeValue<OptionSetValue>("cr2c9_status")?.Value != 1)
                throw Guard.Error("The saved request must be Approved before annual accounting.");
            if (Identity(r.GetAttributeValue<object>("cr2c9_decisionbyentraid")) != identity)
                throw Guard.Error("The saved decision identity must match the approval caller.");
            if (!r.Contains("cr2c9_decisionon")) throw Guard.Error("The server approval timestamp is required.");
            var employee = s.Retrieve(Guard.Employee, Ref(r,"cr2c9_employeename",Guard.Employee).Id,
                new ColumnSet("cr2c9_isactive", "statecode", "cr2c9_entrauserid", "cr2c9_superior"));
            var employeeIdentity = Identity(employee.GetAttributeValue<object>("cr2c9_entrauserid"));
            if (!employee.GetAttributeValue<bool>("cr2c9_isactive") || employee.GetAttributeValue<OptionSetValue>("statecode")?.Value != 0 ||
                employeeIdentity == Guid.Empty || employeeIdentity == identity)
                throw Guard.Error("An active employee and a different authorized Superior are required.");
            if (!DecisionAuthority.IsHr(s, actorId)) {
                var managerRef = Ref(employee,"cr2c9_superior",Guard.Employee);
                var manager = s.Retrieve(Guard.Employee, managerRef.Id, new ColumnSet("cr2c9_isactive", "statecode", "cr2c9_entrauserid"));
                if (manager.Id == employee.Id || !manager.GetAttributeValue<bool>("cr2c9_isactive") ||
                    manager.GetAttributeValue<OptionSetValue>("statecode")?.Value != 0 ||
                    Identity(manager.GetAttributeValue<object>("cr2c9_entrauserid")) != identity)
                    throw Guard.Error("Only the latest synced Superior or a verified HR member can approve annual leave.");
            }
            var q = new QueryExpression(History) { ColumnSet = new ColumnSet("cr2c9_outcome", "cr2c9_actorentraid", "cr2c9_decidedon", "cr2c9_operationkey"), TopCount = 2 };
            q.Criteria.AddCondition("cr2c9_leaverequest", ConditionOperator.Equal, r.Id);
            var h = s.RetrieveMultiple(q).Entities;
            if (h.Count != 1 || h[0].GetAttributeValue<OptionSetValue>("cr2c9_outcome")?.Value != 1 ||
                Identity(h[0].GetAttributeValue<object>("cr2c9_actorentraid")) != identity ||
                h[0].GetAttributeValue<DateTime?>("cr2c9_decidedon") != r.GetAttributeValue<DateTime?>("cr2c9_decisionon") ||
                h[0].GetAttributeValue<string>("cr2c9_operationkey") != r.Id.ToString("D") + ":decision")
                throw Guard.Error("The matching server decision history is required before deducting leave.");
            return r;
        }
        internal static AnnualLeaveDate[] ReadDates(IOrganizationService s, Entity request) {
            var start = request.GetAttributeValue<DateTime?>("cr2c9_startdate");
            var end = request.GetAttributeValue<DateTime?>("cr2c9_enddate");
            if (!start.HasValue || !end.HasValue || start.Value.TimeOfDay != TimeSpan.Zero ||
                end.Value.TimeOfDay != TimeSpan.Zero || end < start) throw Guard.Error("Annual leave needs a valid saved Start Date and End Date.");
            var q = new QueryExpression(Dates) { ColumnSet = new ColumnSet("cr2c9_leaverequest", "cr2c9_leavedate", "cr2c9_duration", "statecode"),
                PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 } };
            q.Criteria.AddCondition("cr2c9_leaverequest", ConditionOperator.Equal, request.Id);
            q.AddOrder(Dates + "id", OrderType.Ascending);
            var rows = s.RetrieveMultiple(q);
            if (rows.MoreRecords || rows.Entities.Count == 0) throw Guard.Error("Complete per-date entries are required before approving annual leave (maximum 5000 dates).");
            var dates = rows.Entities.Select(row => {
                var day = row.GetAttributeValue<DateTime?>("cr2c9_leavedate");
                var duration = row.GetAttributeValue<OptionSetValue>("cr2c9_duration")?.Value;
                if (Ref(row,"cr2c9_leaverequest",Guard.Request).Id != request.Id || row.GetAttributeValue<OptionSetValue>("statecode")?.Value != 0 ||
                    !day.HasValue || day.Value.TimeOfDay != TimeSpan.Zero || day < start || day > end ||
                    (duration != 290040000 && duration != 290040001))
                    throw Guard.Error("Every selected leave date must be active, within the request range, and Half Day (4 hours) or Full Day (8 hours).");
                return new AnnualLeaveDate(day.Value, duration == 290040000 ? 4 : 8);
            }).ToArray();
            if (dates.GroupBy(d => d.Date).Any(g => g.Count() != 1)) throw Guard.Error("Duplicate annual leave dates are not allowed.");
            return dates; // Gaps are intentional: never charge the inclusive range.
        }
        internal static BalancePeriod Period(Entity row, Guid employee, Guid? expectedType = null) {
            var start = row.GetAttributeValue<DateTime?>("cr2c9_periodstart");
            var end = row.GetAttributeValue<DateTime?>("cr2c9_periodend");
            var type = expectedType ?? GrantContract.AnnualType;
            if (Ref(row,"cr2c9_employee",Guard.Employee).Id != employee || Ref(row,"cr2c9_leavetype","cr2c9_leavetype").Id != type ||
                !start.HasValue || !end.HasValue || start.Value != new DateTime(start.Value.Year,1,1) || end.Value != new DateTime(start.Value.Year,12,31))
                throw Guard.Error("A matching calendar-year leave balance is required.");
            return new BalancePeriod(row.Id,employee,type,start.Value,end.Value);
        }
        internal static AnnualLeaveDate[] SickDates(IOrganizationService s, Entity request) {
            var dates = ReadDates(s,request).OrderBy(d=>d.Date).ToArray();
            var start = request.GetAttributeValue<DateTime?>("cr2c9_startdate");
            var end = request.GetAttributeValue<DateTime?>("cr2c9_enddate");
            if (!start.HasValue || !end.HasValue || (end.Value-start.Value).Days > 366)
                throw Guard.Error("Medical Leave needs a valid saved date range.");
            var expected = Enumerable.Range(0,(end.Value-start.Value).Days+1)
                .Select(offset=>start.Value.AddDays(offset))
                .Where(day=>day.DayOfWeek!=DayOfWeek.Saturday && day.DayOfWeek!=DayOfWeek.Sunday)
                .ToArray();
            if (expected.Length != dates.Length || expected.Where((day,index)=>day!=dates[index].Date).Any())
                throw Guard.Error("Every Medical Leave weekday in the request must have exactly one saved date entry.");
            if (dates.Select(d=>d.Hours).Distinct().Count()!=1)
                throw Guard.Error("Medical Leave must use one uniform 4- or 8-hour duration.");
            return dates;
        }
    }

    // PostOperation is synchronous: its SDK calls share the original approval
    // transaction, including the earlier request/employee locks and history row.
    public sealed class AnnualApprovalAccounting : IPlugin
    {
        public void Execute(IServiceProvider provider) {
            var c = (IPluginExecutionContext)provider.GetService(typeof(IPluginExecutionContext));
            if (!ApprovalContract.Envelope(c)) throw Guard.Error("Invalid annual approval accounting registration.");
            var target = c.InputParameters["Target"] as Entity;
            if (target?.GetAttributeValue<OptionSetValue>("cr2c9_status")?.Value != 1) return;
            var s = GrantContract.System(provider);
            var saved = s.Retrieve(Guard.Request,c.PrimaryEntityId,new ColumnSet("cr2c9_leavetypename",TrainingPolicy.Category));
            var leaveType = ApprovalContract.Ref(saved,"cr2c9_leavetypename","cr2c9_leavetype").Id;
            if (leaveType == SickContract.SickType) {
                ApplySick(s,c);
                return;
            }
            if (leaveType == HospitalContract.HospitalType) {
                ApplyHospital(s,c);
                return;
            }
            var emergency = leaveType != GrantContract.AnnualType &&
                EmergencyApprovalRules.IsEmergency(s,saved,true);
            var training = leaveType != GrantContract.AnnualType && !emergency &&
                TrainingPolicy.IsTraining(s,saved);
            if (training && !TrainingPolicy.ChargesAnnual(saved)) return;
            if (leaveType != GrantContract.AnnualType && !emergency && !training) return;
            var request = ApprovalContract.ApprovedRequest(s,c);
            var type = s.Retrieve("cr2c9_leavetype",GrantContract.AnnualType,new ColumnSet("statecode"));
            if (type.GetAttributeValue<OptionSetValue>("statecode")?.Value != 0) throw Guard.Error("The configured Annual Leave category is inactive.");
            var employee = ApprovalContract.Ref(request,"cr2c9_employeename",Guard.Employee).Id;
            // Emergency Leave is a separate request category, but its approval
            // consumes the exact same earned Annual Leave and carry-forward
            // buckets. Its saved rows must cover every weekday in the range.
            var dates = emergency || training ? ApprovalContract.SickDates(s,request) :
                ApprovalContract.ReadDates(s,request);
            var periods = dates.Select(d => d.Date.Year).Distinct().OrderBy(y => y).Select(year => {
                var rows = GrantContract.Periods(s,employee,new DateTime(year,1,1),new DateTime(year,12,31));
                if (rows.Length != 1) throw Guard.Error("Annual leave balance has not been granted for " + year + ". Complete the employee details and grant the balance before approval.");
                return ApprovalContract.Period(rows.Single(),employee);
            }).ToArray();
            // Calculate every cap from saved HR inputs and today's Malaysia
            // calendar date. The request's future date cannot earn future leave.
            var employment = s.Retrieve(Guard.Employee,employee,new ColumnSet(
                "cr2c9_datejoined", "cr2c9_employeelevel", "cr2c9_employmenttype",
                "cr2c9_isactive", "cr2c9_leavingdate", "statecode"));
            if (employment.GetAttributeValue<OptionSetValue>("statecode")?.Value != 0)
                throw Guard.Error("An active employee is required to approve annual leave.");
            var today = EntitlementCalculator.MalaysiaDate(DateTime.UtcNow);
            var earnedCaps = new Dictionary<Guid,int>();
            try {
                foreach (var period in periods)
                    earnedCaps.Add(period.Id, CalendarAnnualGrant.ApprovalEarnedCap(
                        employment.GetAttributeValue<DateTime?>("cr2c9_datejoined"),
                        employment.GetAttributeValue<OptionSetValue>("cr2c9_employeelevel")?.Value,
                        employment.GetAttributeValue<OptionSetValue>("cr2c9_employmenttype")?.Value,
                        employment.GetAttributeValue<bool?>("cr2c9_isactive"),
                        employment.GetAttributeValue<DateTime?>("cr2c9_leavingdate"),
                        period.Start,today));
                new BalancePersistence(s,c).ApplyApprovedAnnualLeaveWithCarry(employee,GrantContract.AnnualType,
                    request.Id,dates,periods,earnedCaps);
            }
            catch (InvalidOperationException e) { throw Guard.Error(e.Message); }
            catch (ArgumentException e) { throw Guard.Error(e.Message); }
        }
        static void ApplySick(IOrganizationService s,IPluginExecutionContext c) {
            var request = ApprovalContract.ApprovedRequest(s,c);
            var type = s.Retrieve("cr2c9_leavetype",SickContract.SickType,
                new ColumnSet("cr2c9_leavetypename","statecode"));
            if (type.GetAttributeValue<OptionSetValue>("statecode")?.Value!=0 ||
                !SickContract.IsMedicalName(type.GetAttributeValue<string>("cr2c9_leavetypename")))
                throw Guard.Error("The configured Medical Leave category is inactive or changed.");
            var employee = ApprovalContract.Ref(request,"cr2c9_employeename",Guard.Employee).Id;
            var dates = ApprovalContract.SickDates(s,request);
            var periods = dates.Select(d=>d.Date.Year).Distinct().OrderBy(y=>y).Select(year=>{
                var rows = GrantContract.Periods(s,employee,new DateTime(year,1,1),
                    new DateTime(year,12,31),SickContract.SickType);
                if (rows.Length!=1)
                    throw Guard.Error("Medical Leave balance has not been granted for " + year + ". Ask HR to grant it before approval.");
                return ApprovalContract.Period(rows.Single(),employee,SickContract.SickType);
            }).ToArray();
            try {
                new BalancePersistence(s,c).ApplyApprovedAnnualLeave(employee,SickContract.SickType,
                    request.Id,dates,periods);
            }
            catch (InvalidOperationException e) { throw Guard.Error(e.Message.Replace("annual leave","Medical Leave")); }
            catch (ArgumentException e) { throw Guard.Error(e.Message); }
        }
        static void ApplyHospital(IOrganizationService s,IPluginExecutionContext c) {
            var request = ApprovalContract.ApprovedRequest(s,c);
            var type = s.Retrieve("cr2c9_leavetype",HospitalContract.HospitalType,
                new ColumnSet("cr2c9_leavetypename","statecode"));
            if (type.GetAttributeValue<OptionSetValue>("statecode")?.Value != 0 ||
                type.GetAttributeValue<string>("cr2c9_leavetypename") != "Hospitalisation Leave")
                throw Guard.Error("The configured Hospitalisation Leave category is inactive or changed.");
            var employee = ApprovalContract.Ref(request,"cr2c9_employeename",Guard.Employee).Id;
            var dates = ApprovalContract.SickDates(s,request);
            var periods = dates.Select(d=>d.Date.Year).Distinct().OrderBy(y=>y).Select(year=>{
                var rows = GrantContract.Periods(s,employee,new DateTime(year,1,1),
                    new DateTime(year,12,31),HospitalContract.HospitalType);
                if (rows.Length != 1)
                    throw Guard.Error("Hospitalisation balance has not been granted for " + year + ". Ask HR to grant it before approval.");
                return ApprovalContract.Period(rows.Single(),employee,HospitalContract.HospitalType);
            }).ToArray();
            try {
                new BalancePersistence(s,c).ApplyApprovedAnnualLeave(employee,HospitalContract.HospitalType,
                    request.Id,dates,periods);
            }
            catch (InvalidOperationException e) { throw Guard.Error(e.Message.Replace("annual leave","Hospitalisation Leave")); }
            catch (ArgumentException e) { throw Guard.Error(e.Message); }
        }
    }
}
