using Leave.Plugins;
using Leave.BalanceGuards;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System.Reflection;

static class AnnualApprovalChecks {
    public static void Run() {
        int count=0;
        void Eq(object a,object b){if(!Equals(a,b))throw new Exception($"Expected {b}, got {a}");}
        void Check(string name,Action<Fixture> run){var f=new Fixture();f.SetupAnnual();try{run(f);count++;}catch(Exception e){throw new Exception(name,e);}}
        void Deny(Fixture f,Action action){var balance=f.Available;var history=f.HistoryCount;var version=f.Request.RowVersion;try{action();}catch(InvalidPluginExecutionException){Eq(f.Available,balance);Eq(f.HistoryCount,history);Eq(f.Request.RowVersion,version);return;}throw new Exception("Expected denial");}
        Check("mixed dates with gap debit exactly twelve hours",f=>{f.Approve();Eq(f.Available,116);Eq(f.Debits.Count(),1);Eq(f.Request.GetAttributeValue<OptionSetValue>("cr2c9_status").Value,1);Eq(f.HistoryCount,1);Eq(f.Debits.Single().GetAttributeValue<EntityReference>("ownerid").Id,f.EmployeeId);});
        Check("invalid legacy daily hours cannot publish an approved absence",f=>{f.Request["cr2c9_hoursperday"]=5;Deny(f,()=>f.Approve());});
        Check("retry cannot debit twice",f=>{f.Approve();Deny(f,()=>f.Approve());Eq(f.Debits.Count(),1);});
        Check("rejection creates history without debit",f=>{f.Approve(3);Eq(f.Available,128);Eq(f.Debits.Count(),0);Eq(f.HistoryCount,1);});
        Check("insufficiency rolls back status history and locks",f=>{f.Credit(8);Deny(f,()=>f.Approve());Eq(f.Request.GetAttributeValue<OptionSetValue>("cr2c9_status").Value,0);Eq(f.Employee.RowVersion,"1");});
        Check("exact balance can reach zero",f=>{f.Credit(12);f.Approve();Eq(f.Available,0);});
        Check("no dates blocks approval without side effects",f=>{f.ClearDates();Deny(f,()=>f.Approve());});
        Check("duplicate dates block approval",f=>{f.AddDate(new DateTime(2026,10,19),4);Deny(f,()=>f.Approve());});
        Check("out-of-range date blocks approval",f=>{f.AddDate(new DateTime(2026,11,1),8);Deny(f,()=>f.Approve());});
        Check("invalid duration blocks approval",f=>{f.DateRows.First()["cr2c9_duration"]=new OptionSetValue(5);Deny(f,()=>f.Approve());});
        Check("inactive date cannot silently reduce debit",f=>{f.DateRows.First()["statecode"]=new OptionSetValue(1);Deny(f,()=>f.Approve());});
        Check("incomplete paged dates fail closed",f=>{f.MoreDates=true;Deny(f,()=>f.Approve());});
        Check("missing period blocks approval",f=>{foreach(var e in f.Rows.Values.Where(e=>e.LogicalName==BalancePersistence.PeriodTable).ToArray())f.Rows.Remove((e.LogicalName,e.Id));Deny(f,()=>f.Approve());});
        Check("overlapping periods block approval",f=>{f.AddPeriod(2026,128);Deny(f,()=>f.Approve());});
        Check("inactive annual category blocks approval",f=>{f.LeaveType["statecode"]=new OptionSetValue(1);Deny(f,()=>f.Approve());});
        Check("inactive Employee record blocks approval",f=>{f.Employee["statecode"]=new OptionSetValue(1);Deny(f,()=>f.Approve());});
        Check("manager change blocks approval",f=>{f.Manager["cr2c9_entrauserid"]=Guid.NewGuid().ToString();Deny(f,()=>f.Approve());});
        Check("HR role can approve and deduct with no assigned Superior",f=>{f.AsHr();f.Employee.Attributes.Remove("cr2c9_superior");f.Approve();Eq(f.Available,116);Eq(f.Debits.Count(),1);});
        Check("HR role cannot approve own annual leave",f=>{f.AsHr();f.Employee["cr2c9_entrauserid"]=f.Actor.GetAttributeValue<Guid>("azureactivedirectoryobjectid").ToString();Deny(f,()=>f.Approve());});
        Check("self approval blocks before accounting",f=>{f.AsEmployee();Deny(f,()=>f.Approve());});
        Check("period CAS conflict rolls back decision and history",f=>{f.ConflictPeriod=true;Deny(f,()=>f.Approve());});
        Check("future balance year cannot be spent before it is earned",f=>{f.CrossYear(8);Deny(f,()=>f.Approve());Eq(f.Debits.Count(),0);});
        Check("cross-year insufficiency leaves both balances unchanged",f=>{f.CrossYear(4);Deny(f,()=>f.Approve());Eq(f.Debits.Count(),0);});
        Check("second ledger failure rolls back first debit and history",f=>{f.CrossYear(8);f.FailEntryNumber=2;Deny(f,()=>f.Approve());Eq(f.Debits.Count(),0);});
        Check("existing consumed balance governs later approval",f=>{f.Credit(20);f.Approve();f.NewPendingRequest();Deny(f,()=>f.Approve());Eq(f.Available,8);Eq(f.Debits.Count(),1);});
        Check("recorded full-year grant cannot bypass earned cap",f=>{
            f.Employee["cr2c9_datejoined"]=new DateTime(2026,2,20);
            f.Credit(128);
            var period=f.Rows.Values.Single(e=>e.LogicalName==BalancePersistence.PeriodTable);
            var used=new Entity(BalancePersistence.EntryTable,Guid.NewGuid());
            used["cr2c9_balanceperiod"]=period.ToEntityReference();
            used["cr2c9_operationkey"]="previous-approved";
            used["cr2c9_entrykind"]=new OptionSetValue(290040001);
            used["cr2c9_hours"]=-80;
            used["cr2c9_leaverequest"]=new EntityReference(Guard.Request,Guid.NewGuid());
            f.Rows[(used.LogicalName,used.Id)]=used;
            Deny(f,()=>f.Approve());
        });
        Check("different category cannot be approved without its own accounting",f=>{
            var other=new Entity("cr2c9_leavetype",Guid.NewGuid()){
                ["statecode"]=new OptionSetValue(0),["cr2c9_leavetypename"]="Unsupported Leave"
            };
            f.Rows[(other.LogicalName,other.Id)]=other;
            f.Request["cr2c9_leavetypename"]=other.ToEntityReference();
            Deny(f,()=>f.Approve());Eq(f.Available,128);Eq(f.Debits.Count(),0);
        });
        Check("emergency approval requires uploaded evidence",f=>{
            SetEmergency(f);
            DismissGap(f);
            Deny(f,()=>f.Approve());
            Eq(f.Available,128);
        });
        Check("emergency approval charges Annual Leave exactly once",f=>{
            SetEmergency(f);
            DismissGap(f);
            AddEmergencyEvidence(f);
            f.Approve();
            Eq(f.Available,116);
            Eq(f.Debits.Single().GetAttributeValue<int>("cr2c9_hours"),-12);
            Deny(f,()=>f.Approve());
            Eq(f.Debits.Count(),1);
        });
        Check("emergency approval rejects missing weekday rows",f=>{
            SetEmergency(f);
            AddEmergencyEvidence(f);
            Deny(f,()=>f.Approve());
            Eq(f.Debits.Count(),0);
        });
        Check("past emergency leave spends March carry-forward first",f=>{
            f.AddPeriod(2025,64);f.AddCarryCredit(32);
            f.ClearDates();
            f.Request["cr2c9_startdate"]=new DateTime(2026,3,31);
            f.Request["cr2c9_enddate"]=new DateTime(2026,3,31);
            f.Request["cr2c9_hoursperday"]=8m;
            f.AddDate(new DateTime(2026,3,31),8);
            SetEmergency(f);AddEmergencyEvidence(f);
            f.Approve();
            Eq(f.Rows.Values.Single(e=>e.LogicalName=="cr2c9_carryforwardusage")
                .GetAttributeValue<int>("cr2c9_hours"),8);
            Eq(f.Debits.Count(),0);
        });
        Check("untrusted postoperation registration fails closed",f=>{f.BadAccountingStep=true;Deny(f,()=>f.Approve());});
        Check("missing preimage fails closed",f=>{f.NoApprovalPreImage=true;Deny(f,()=>f.Approve());});
        Check("async context cannot deduct",f=>{f.AsyncAccounting=true;Deny(f,()=>f.Approve());});
        Check("missing decision history blocks accounting",f=>{f.RemoveHistoryBeforeAccounting=true;Deny(f,()=>f.Approve());});
        Check("external forged debit is denied",f=>Deny(f,()=>f.ExternalDebit()));
        Check("nested SYSTEM context retains verified approving user",f=>{f.SystemNestedContext=true;f.Approve();Eq(f.Available,116);});
        Check("nested actor mismatch cannot write a debit",f=>{f.ForgedNestedActor=true;Deny(f,()=>f.Approve());});
        Check("impersonated approval step fails closed",f=>{f.ImpersonatedAccounting=true;Deny(f,()=>f.Approve());});
        Check("posted carry-forward pays March date before earned Annual Leave",f=>{
            f.AddPeriod(2025,64);f.AddCarryCredit(32);
            f.ClearDates();f.Request["cr2c9_startdate"]=new DateTime(2026,3,31);
            f.Request["cr2c9_enddate"]=new DateTime(2026,4,1);
            f.AddDate(new DateTime(2026,3,31),8);f.AddDate(new DateTime(2026,4,1),8);
            f.Approve();
            Eq(f.Debits.Single().GetAttributeValue<int>("cr2c9_hours"),-8);
            Eq(f.Rows.Values.Single(e=>e.LogicalName=="cr2c9_carryforwardusage")
                .GetAttributeValue<int>("cr2c9_hours"),8);
        });
        Console.WriteLine($"{count} approval transaction and deduction checks passed.");
    }
    static void SetEmergency(Fixture f) {
        var type=new Entity("cr2c9_leavetype",Guid.NewGuid()) {
            ["statecode"]=new OptionSetValue(0),
            ["cr2c9_leavetypename"]="Emergency Leave"
        };
        f.Rows[(type.LogicalName,type.Id)]=type;
        f.Request["cr2c9_leavetypename"]=type.ToEntityReference();
    }
    static void DismissGap(Fixture f) {
        f.DateRows.Single(row=>row.GetAttributeValue<DateTime>("cr2c9_leavedate")==new DateTime(2026,10,21))
            ["cr2c9_duration"]=new OptionSetValue(290040000);
        f.AddDate(new DateTime(2026,10,20),4);
    }
    static void AddEmergencyEvidence(Fixture f) {
        var evidence=new Entity("annotation",Guid.NewGuid()) {
            ["objectid"]=f.RequestId,["isdocument"]=true,
            ["filesize"]=120,["filename"]="emergency-evidence.pdf"
        };
        f.Rows[(evidence.LogicalName,evidence.Id)]=evidence;
    }
}
public sealed partial class Fixture {
    public bool ConflictPeriod,MoreDates,BadAccountingStep,NoApprovalPreImage,AsyncAccounting,RemoveHistoryBeforeAccounting,SystemNestedContext,ForgedNestedActor,ImpersonatedAccounting;
    public int FailEntryNumber,EntryWrites;
    public IEnumerable<Entity> Debits=>Rows.Values.Where(e=>e.LogicalName==BalancePersistence.EntryTable && e.GetAttributeValue<int>("cr2c9_hours")<0);
    public IEnumerable<Entity> DateRows=>Rows.Values.Where(e=>e.LogicalName==ApprovalContract.Dates);
    public int Available=>Rows.Values.Where(e=>e.LogicalName==BalancePersistence.EntryTable).Sum(e=>e.GetAttributeValue<int>("cr2c9_hours"));
    public void SetupAnnual(){
        var old=LeaveType;Rows.Remove((old.LogicalName,old.Id));old.Id=GrantContract.AnnualType;Rows[(old.LogicalName,old.Id)]=old;
        Request["cr2c9_leavetypename"]=old.ToEntityReference();Employee["statecode"]=new OptionSetValue(0);Manager["statecode"]=new OptionSetValue(0);
        Employee["cr2c9_datejoined"]=new DateTime(2026,1,1);
        Employee["cr2c9_employeelevel"]=new OptionSetValue(EntitlementCalculator.FirstLevel);
        Employee["cr2c9_employmenttype"]=new OptionSetValue(EntitlementCalculator.FullTime);
        AddPeriod(2026,128);AddDate(new DateTime(2026,10,19),4);AddDate(new DateTime(2026,10,21),8);
    }
    public void Credit(int amount){Rows.Values.Single(e=>e.LogicalName==BalancePersistence.EntryTable)["cr2c9_hours"]=amount;}
    public void AddPeriod(int year,int hours){
        var p=new Entity(BalancePersistence.PeriodTable,Guid.NewGuid()){RowVersion="1"};p["cr2c9_employee"]=Employee.ToEntityReference();p["cr2c9_leavetype"]=LeaveType.ToEntityReference();p["cr2c9_periodstart"]=new DateTime(year,1,1);p["cr2c9_periodend"]=new DateTime(year,12,31);p["ownerid"]=new EntityReference("systemuser",EmployeeId);Rows[(p.LogicalName,p.Id)]=p;
        var e=new Entity(BalancePersistence.EntryTable,Guid.NewGuid()){RowVersion="1"};e["cr2c9_balanceperiod"]=p.ToEntityReference();e["cr2c9_operationkey"]=p.Id+":annual-grant:"+hours;e["cr2c9_entrykind"]=new OptionSetValue(290040000);e["cr2c9_hours"]=hours;Rows[(e.LogicalName,e.Id)]=e;
    }
    public void ClearDates(){foreach(var e in DateRows.ToArray())Rows.Remove((e.LogicalName,e.Id));}
    public void AddDate(DateTime day,int hours){var e=new Entity(ApprovalContract.Dates,Guid.NewGuid());e["cr2c9_leaverequest"]=Request.ToEntityReference();e["cr2c9_leavedate"]=day;e["cr2c9_duration"]=new OptionSetValue(hours==4?290040000:290040001);e["statecode"]=new OptionSetValue(0);Rows[(e.LogicalName,e.Id)]=e;}
    public void CrossYear(int nextCredit){ClearDates();Request["cr2c9_startdate"]=new DateTime(2026,12,31);Request["cr2c9_enddate"]=new DateTime(2027,1,1);AddPeriod(2027,nextCredit);AddDate(new DateTime(2026,12,31),4);AddDate(new DateTime(2027,1,1),8);}
    public void AddCarryCredit(int hours){
        var source=Rows.Values.Single(e=>e.LogicalName==BalancePersistence.PeriodTable &&
            e.GetAttributeValue<DateTime>("cr2c9_periodstart").Year==2025);
        var target=Rows.Values.Single(e=>e.LogicalName==BalancePersistence.PeriodTable &&
            e.GetAttributeValue<DateTime>("cr2c9_periodstart").Year==2026);
        var key=source.Id.ToString("D")+":carry-forward:"+target.Id.ToString("D");
        var review=new Entity("cr2c9_carryforwardreview",Guid.NewGuid());
        review["cr2c9_reviewstatus"]=new OptionSetValue(290040002);
        review["cr2c9_sourceperiod"]=source.ToEntityReference();
        review["cr2c9_targetperiod"]=target.ToEntityReference();
        review["cr2c9_reviewedcarryhours"]=hours;review["cr2c9_operationkey"]=key;
        Rows[(review.LogicalName,review.Id)]=review;
        var credit=new Entity("cr2c9_carryforwardcredit",Guid.NewGuid()){RowVersion="1"};
        credit["cr2c9_employee"]=Employee.ToEntityReference();
        credit["cr2c9_sourceperiod"]=source.ToEntityReference();
        credit["cr2c9_targetperiod"]=target.ToEntityReference();
        credit["cr2c9_review"]=review.ToEntityReference();
        credit["cr2c9_hours"]=hours;credit["cr2c9_operationkey"]=key;
        credit["cr2c9_useby"]=new DateTime(2026,3,31);
        credit["ownerid"]=new EntityReference("systemuser",EmployeeId);
        Rows[(credit.LogicalName,credit.Id)]=credit;
    }
    public void NewPendingRequest(){var r=Copy(Request);r.Id=Guid.NewGuid();RequestId=r.Id;r.RowVersion="1";r["cr2c9_status"]=new OptionSetValue(0);Rows[(r.LogicalName,r.Id)]=r;AddDate(new DateTime(2026,10,19),4);AddDate(new DateTime(2026,10,21),8);}
    public void Approve(int status=1){
        var snapshot=Rows.ToDictionary(x=>x.Key,x=>Copy(x.Value));var prior=context;var target=Decision(status);
        var before=Copy(Request);context=Ctx("Update",target);
        try{
            new LeaveRequestGuard().Execute(this);
            foreach(var a in target.Attributes)Request[a.Key]=a.Value;
            Request.RowVersion=(int.Parse(Request.RowVersion)+1).ToString();
            var c=Ctx("Update",target);var v=((ContextProxy)(object)c).Values;v["Stage"]=40;v["Mode"]=AsyncAccounting?1:0;
            if(ImpersonatedAccounting)v["UserId"]=Guid.NewGuid();
            v["OwningExtension"]=new EntityReference("sdkmessageprocessingstep",Guid.NewGuid()){Name=BadAccountingStep?"Untrusted":ApprovalContract.Step};
            v["PreEntityImages"]=NoApprovalPreImage?new EntityImageCollection():new EntityImageCollection{{ApprovalContract.PreImage,before}};
            if(RemoveHistoryBeforeAccounting)foreach(var h in Rows.Values.Where(e=>e.LogicalName==Guard.History).ToArray())Rows.Remove((h.LogicalName,h.Id));
            context=c;new AnnualApprovalAccounting().Execute(this);
        }catch{Rows=snapshot;throw;}finally{context=prior;}
    }
    public void NestedBalance(string message,Entity target){
        var prior=context;var parentValues=((ContextProxy)(object)prior).Values;
        var originalInitiator=parentValues["InitiatingUserId"];
        var originalUser=parentValues["UserId"];
        if(SystemNestedContext || ForgedNestedActor)parentValues["InitiatingUserId"]=Guid.NewGuid();
        if(ForgedNestedActor){var other=Guid.NewGuid();var row=new Entity("systemuser",other);row["azureactivedirectoryobjectid"]=Guid.NewGuid();row["isdisabled"]=false;Rows[("systemuser",other)]=row;parentValues["UserId"]=other;}
        var core=Ctx(message,target,prior);((ContextProxy)(object)core).Values["Stage"]=30;
        if(SystemNestedContext || ForgedNestedActor)((ContextProxy)(object)core).Values["InitiatingUserId"]=parentValues["InitiatingUserId"];
        try{
            var bulk=Ctx(message+"Multiple",target,core);((ContextProxy)(object)bulk).Values["InputParameters"]=new ParameterCollection{{"Targets",new EntityCollection(new[]{target})}};
            if(SystemNestedContext || ForgedNestedActor)((ContextProxy)(object)bulk).Values["InitiatingUserId"]=parentValues["InitiatingUserId"];
            context=bulk;new BalanceStagingGuard().Execute(this);
            context=Ctx(message,target,core);
            if(SystemNestedContext || ForgedNestedActor)((ContextProxy)(object)context).Values["InitiatingUserId"]=parentValues["InitiatingUserId"];
            new BalanceStagingGuard().Execute(this);
        }finally{context=prior;parentValues["InitiatingUserId"]=originalInitiator;parentValues["UserId"]=originalUser;}
    }
    public void NestedCarry(string message,Entity target){
        var prior=context;var core=Ctx(message,target,prior);
        ((ContextProxy)(object)core).Values["Stage"]=30;
        try {context=Ctx(message,target,core);new CarryForwardWriteGuard().Execute(this);}
        finally{context=prior;}
    }
    public void ExternalDebit(){Run(new BalanceStagingGuard(),"Create",new Entity(BalancePersistence.EntryTable){["cr2c9_hours"]=-8,["cr2c9_entrykind"]=new OptionSetValue(290040001)});}
    public ParameterCollection PreviewCarry(bool extraInput=false){
        var prior=context;context=Ctx(CarryForwardPreviewOperation.Api,Employee.ToEntityReference());
        var values=((ContextProxy)(object)context).Values;
        values["Stage"]=30;values["OutputParameters"]=new ParameterCollection();
        if(extraInput)context.InputParameters["Hours"]=100000;
        try{new CarryForwardPreviewOperation().Execute(this);return context.OutputParameters;}
        finally{context=prior;}
    }
    public ParameterCollection ConfirmCarry(bool extraInput=false){
        var prior=context;context=Ctx(CarryForwardConfirmContract.Api,Employee.ToEntityReference());
        var values=((ContextProxy)(object)context).Values;
        values["Stage"]=30;values["OutputParameters"]=new ParameterCollection();
        if(extraInput)context.InputParameters["Hours"]=40;
        try{new CarryForwardConfirmOperation().Execute(this);return context.OutputParameters;}
        finally{context=prior;}
    }
    public ParameterCollection ConfirmCarryAt(DateTime today){
        var prior=context;var snapshot=Rows.ToDictionary(x=>x.Key,x=>Copy(x.Value));
        context=Ctx(CarryForwardConfirmContract.Api,Employee.ToEntityReference());
        var values=((ContextProxy)(object)context).Values;
        values["Stage"]=30;values["OutputParameters"]=new ParameterCollection();
        SkipBalanceGuardForCarryCoreTest=true;
        try{new CarryForwardConfirmOperation().ExecuteAt(this,today);return context.OutputParameters;}
        catch{Rows=snapshot;throw;}
        finally{SkipBalanceGuardForCarryCoreTest=false;context=prior;}
    }
    EntityCollection QueryRows(QueryExpression q){
        var found=Rows.Values.Where(e=>e.LogicalName==q.EntityName);
        foreach(var c in q.Criteria.Conditions)found=found.Where(e=>{
            var value=c.AttributeName==e.LogicalName+"id" ? e.Id : e.GetAttributeValue<object>(c.AttributeName);if(value is EntityReference er)value=er.Id;
            if(value is OptionSetValue choice)value=choice.Value;
            if(c.Operator==ConditionOperator.Equal)return Equals(value,c.Values[0]);
            if(c.Operator==ConditionOperator.NotEqual)return !Equals(value,c.Values[0]);
            if(c.Operator==ConditionOperator.In)return c.Values.Any(candidate=>Equals(value,candidate));
            if(value is DateTime d && c.Values[0] is DateTime expected)return c.Operator==ConditionOperator.LessEqual?d<=expected:d>=expected;
            throw new NotSupportedException(c.Operator.ToString());});
        if(q.TopCount.HasValue)found=found.Take(q.TopCount.Value);
        return new EntityCollection(found.Select(Copy).ToArray()){MoreRecords=q.EntityName==ApprovalContract.Dates && MoreDates};
    }
}
