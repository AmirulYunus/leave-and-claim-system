using Leave.Plugins;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using System.Reflection;

static class BalancePersistenceChecks
{
    public static void Run()
    {
        var count=0;
        void Check(string name, Action<BalanceStoreFixture> test) { test(new BalanceStoreFixture()); count++; Console.WriteLine("PASS balance persistence: " + name); }
        void Eq(object actual, object expected) { if(!Equals(actual,expected))throw new Exception($"Expected {expected}; got {actual}"); }
        void Denied(Action action) { try {action();} catch(InvalidPluginExecutionException){return;} catch(ArgumentException){return;} catch(InvalidOperationException){return;} throw new Exception("Expected rejection"); }
        EntitlementRate Rate(int days=14)=>new(){Status="Calculated",Days=days,Hours=days*8};
        Check("reject execution outside a server transaction before SDK writes", f => {((ContextProxy)(object)f.Context).Values["IsInTransaction"]=false;Denied(()=>f.Store());Eq(f.Calls.Count,0);});
        Check("reject asynchronous execution", f => {((ContextProxy)(object)f.Context).Values["Mode"]=1;Denied(()=>f.Store());});
        Check("reject PreValidation execution", f => {((ContextProxy)(object)f.Context).Values["Stage"]=10;Denied(()=>f.Store());});
        Check("grant persists signed hours and period ownership", f => {var plan=f.Store().ApplyGrant(f.Period,Rate());Eq(plan.Entry.Hours,112);var row=f.Entries.Single();Eq(row["cr2c9_hours"],112);Eq(row.GetAttributeValue<EntityReference>("ownerid").Id,f.Owner);Eq(row.Contains("cr2c9_leaverequest"),false);});
        Check("repeat grant persists only once", f => {f.Store().ApplyGrant(f.Period,Rate());Eq(f.Store().ApplyGrant(f.Period,Rate()).Status,"AlreadyGranted");Eq(f.Entries.Count(),1);});
        Check("anniversary delta preserves consumed leave", f => {f.Add(f.Period,112);f.Add(f.Period,-12,f.Request);var plan=f.Store().ApplyGrant(f.Period,Rate(15));Eq(plan.ExistingAvailableHours,100);Eq(plan.Entry.Hours,8);Eq(f.Entries.Sum(e=>e.GetAttributeValue<int>("cr2c9_hours")),108);});
        Check("missing HR rate does not overwrite existing credits", f => {f.Add(f.Period,112);var plan=f.Store().ApplyGrant(f.Period,null);Eq(plan.Status,"PendingHrDetails");Eq(plan.ExistingAvailableHours,112);Eq(f.Entries.Count(),1);});
        Check("lower rate preserves the ledger for review", f => {f.Add(f.Period,128);Eq(f.Store().ApplyGrant(f.Period,Rate()).Status,"PendingAdjustment");Eq(f.Entries.Count(),1);});
        Check("approval uses half/full dates and inherits period owner", f => {f.Add(f.Period,112);var plan=f.Approve();Eq(plan.Single().Hours,-12);var row=f.Entries.Single(e=>e.GetAttributeValue<int>("cr2c9_hours")<0);Eq(row.GetAttributeValue<EntityReference>("cr2c9_leaverequest").Id,f.Request);Eq(row.GetAttributeValue<EntityReference>("ownerid").Id,f.Owner);});
        Check("approval retry adds no second debit", f => {f.Add(f.Period,112);f.Approve();Eq(f.Approve().Length,0);Eq(f.Entries.Count(),2);});
        Check("changed retry amount fails", f => {f.Add(f.Period,112);f.Approve();Denied(()=>f.Approve(new[]{f.Dates[0]}));Eq(f.Entries.Count(),2);});
        Check("cross-period approval writes both date buckets", f => {f.Add(f.Period,112);f.Add(f.Next,112);var plan=f.CrossApprove();Eq(plan.Length,2);Eq(plan.Single(p=>p.PeriodId==f.Period.Id).Hours,-4);Eq(plan.Single(p=>p.PeriodId==f.Next.Id).Hours,-8);});
        Check("approved cancellation links the exact debit and restores availability", f => {f.Add(f.Period,112);f.Approve();var cancellation=Guid.NewGuid();var plan=f.Store().ApplyApprovedAnnualCancellation(f.Employee,f.Type,f.Request,cancellation,f.Dates,new[]{f.Period});Eq(plan.Single().Hours,12);var reversal=f.Entries.Single(e=>e.GetAttributeValue<OptionSetValue>("cr2c9_entrykind").Value==290040002);Eq(reversal.GetAttributeValue<EntityReference>("cr2c9_reverses").Id,f.Entries.Single(e=>e.GetAttributeValue<int>("cr2c9_hours")==-12).Id);Eq(f.Entries.Sum(e=>e.GetAttributeValue<int>("cr2c9_hours")),112);});
        Check("approved cancellation cannot reverse twice", f => {f.Add(f.Period,112);f.Approve();f.Store().ApplyApprovedAnnualCancellation(f.Employee,f.Type,f.Request,Guid.NewGuid(),f.Dates,new[]{f.Period});Denied(()=>f.Store().ApplyApprovedAnnualCancellation(f.Employee,f.Type,f.Request,Guid.NewGuid(),f.Dates,new[]{f.Period}));Eq(f.Entries.Count(),3);});
        Check("carry-ready adapter preserves annual-only approval when no credit exists", f => {
            f.Add(f.Period,112);
            var plans=f.Store().ApplyApprovedAnnualLeaveWithCarry(f.Employee,f.Type,f.Request,f.Dates,
                new[]{f.Period},new Dictionary<Guid,int>{{f.Period.Id,112}});
            Eq(plans.Length,1);Eq(plans[0].CarryEntry,null);Eq(plans[0].AnnualEntry.Hours,-12);
            Eq(f.Entries.Count(),2);
        });
        Check("January approval waits for HR when prior-year leave remains unused", f => {
            f.Add(f.Period,64);f.Add(f.Next,80);
            var day=new[]{new AnnualLeaveDate(new DateTime(2027,1,12),8)};
            Denied(()=>f.Store().ApplyApprovedAnnualLeaveWithCarry(f.Employee,f.Type,f.Request,
                day,new[]{f.Next},new Dictionary<Guid,int>{{f.Next.Id,80}}));
            Eq(f.CreateCalls,0);Eq(f.Entries.Count(),2);
        });
        Check("January approval proceeds when prior-year leave is fully used", f => {
            f.Add(f.Next,80);
            var day=new[]{new AnnualLeaveDate(new DateTime(2027,1,12),8)};
            var plan=f.Store().ApplyApprovedAnnualLeaveWithCarry(f.Employee,f.Type,f.Request,
                day,new[]{f.Next},new Dictionary<Guid,int>{{f.Next.Id,80}});
            Eq(plan.Single().AnnualEntry.Hours,-8);
        });
        Check("April approval does not wait for the prior-year carry-forward window", f => {
            f.Add(f.Period,64);f.Add(f.Next,80);
            var day=new[]{new AnnualLeaveDate(new DateTime(2027,4,1),8)};
            var plan=f.Store().ApplyApprovedAnnualLeaveWithCarry(f.Employee,f.Type,f.Request,
                day,new[]{f.Next},new Dictionary<Guid,int>{{f.Next.Id,80}});
            Eq(plan.Single().AnnualEntry.Hours,-8);
        });
        Check("carry-ready adapter reverses annual-only approval exactly", f => {
            f.Add(f.Period,112);
            f.Store().ApplyApprovedAnnualLeaveWithCarry(f.Employee,f.Type,f.Request,f.Dates,
                new[]{f.Period},new Dictionary<Guid,int>{{f.Period.Id,112}});
            var plans=f.Store().ApplyApprovedAnnualCancellationWithCarry(f.Employee,f.Type,
                f.Request,Guid.NewGuid(),f.Dates,new[]{f.Period});
            Eq(plans.Length,1);Eq(plans[0].CarryEntry,null);Eq(plans[0].AnnualEntry.Hours,12);
            Eq(f.Entries.Sum(e=>e.GetAttributeValue<int>("cr2c9_hours")),112);
        });
        Check("carry-ready adapter charges carry first and reverses the saved split", f => {
            f.Add(f.Next,80);f.AddCarryCredit(32);
            var days=new[]{new AnnualLeaveDate(new DateTime(2027,3,31),8),
                new AnnualLeaveDate(new DateTime(2027,4,1),8)};
            var approval=f.Store().ApplyApprovedAnnualLeaveWithCarry(f.Employee,f.Type,f.Request,
                days,new[]{f.Next},new Dictionary<Guid,int>{{f.Next.Id,80}});
            Eq(approval.Single().CarryEntry.Hours,8);
            Eq(approval.Single().AnnualEntry.Hours,-8);
            Eq(f.Usage.Count(),1);
            var cancellation=f.Store().ApplyApprovedAnnualCancellationWithCarry(f.Employee,f.Type,
                f.Request,Guid.NewGuid(),days,new[]{f.Next});
            Eq(cancellation.Single().CarryEntry.Hours,8);
            Eq(cancellation.Single().AnnualEntry.Hours,8);
            Eq(f.Usage.Count(),2);
            Eq(f.Entries.Sum(e=>e.GetAttributeValue<int>("cr2c9_hours")),80);
            Denied(()=>f.Store().ApplyApprovedAnnualCancellationWithCarry(f.Employee,f.Type,
                f.Request,Guid.NewGuid(),days,new[]{f.Next}));
        });
        Check("carry-ready adapter rolls back both buckets on a later write failure", f => {
            f.Add(f.Next,80);f.AddCarryCredit(32);f.FailCreateNumber=2;
            var days=new[]{new AnnualLeaveDate(new DateTime(2027,3,31),8),
                new AnnualLeaveDate(new DateTime(2027,4,1),8)};
            Denied(()=>f.Transaction(()=>f.Store().ApplyApprovedAnnualLeaveWithCarry(f.Employee,f.Type,
                f.Request,days,new[]{f.Next},new Dictionary<Guid,int>{{f.Next.Id,80}})));
            Eq(f.Usage.Count(),0);Eq(f.Entries.Count(),1);
        });
        Check("cross-period insufficiency creates no partial debit", f => {f.Add(f.Period,112);f.Add(f.Next,4);Denied(()=>f.CrossApprove());Eq(f.Entries.Count(),2);Eq(f.CreateCalls,0);});
        Check("later persistence fault propagates and simulated transaction rolls back", f => {f.Add(f.Period,112);f.Add(f.Next,112);f.FailCreateNumber=2;Denied(()=>f.Transaction(()=>f.CrossApprove()));Eq(f.Entries.Count(),2);Eq(f.Rows[(BalancePersistence.PeriodTable,f.Period.Id)].RowVersion,"1");Eq(f.Rows[(BalancePersistence.PeriodTable,f.Next.Id)].RowVersion,"1");});
        Check("period CAS conflict stops before history or debit", f => {f.Add(f.Period,112);f.Conflict=true;Denied(()=>f.Approve());Eq(f.QueryCalls,0);Eq(f.CreateCalls,0);});
        Check("missing row version stops before update", f => {f.Rows[(BalancePersistence.PeriodTable,f.Period.Id)].RowVersion=null;Denied(()=>f.Store().ApplyGrant(f.Period,Rate()));Eq(f.UpdateCalls,0);});
        Check("foreign employee scope is rejected before locking", f => {var wrong=new BalancePeriod(f.Period.Id,Guid.NewGuid(),f.Type,f.Period.Start,f.Period.End);Denied(()=>f.Store().ApplyGrant(wrong,Rate()));Eq(f.UpdateCalls,0);});
        Check("changed stored boundaries are rejected", f => {f.Rows[(BalancePersistence.PeriodTable,f.Period.Id)]["cr2c9_periodend"]=new DateTime(2026,11,30);Denied(()=>f.Approve());Eq(f.CreateCalls,0);});
        Check("missing ledger hours cannot become zero", f => {var row=f.Add(f.Period,112);row.Attributes.Remove("cr2c9_hours");Denied(()=>f.Approve());Eq(f.CreateCalls,0);});
        Check("unknown ledger kind cannot become a grant", f => {var row=f.Add(f.Period,112);row["cr2c9_entrykind"]=new OptionSetValue(9);Denied(()=>f.Approve());});
        Check("all history pages are consumed before calculating availability", f => {f.Add(f.Period,112);f.Add(f.Period,-12,Guid.NewGuid());f.PageSize=1;var r=f.Store().ApplyGrant(f.Period,Rate());Eq(r.ExistingAvailableHours,100);Eq(f.QueryCalls,2);Eq(f.LastCookie,"page-1");});
        Check("missing paging cookie fails rather than using partial history", f => {f.Add(f.Period,112);f.Add(f.Period,-12,Guid.NewGuid());f.PageSize=1;f.MissingCookie=true;Denied(()=>f.Approve());Eq(f.CreateCalls,0);});
        Check("duplicate paging rows fail", f => {f.Add(f.Period,112);f.DuplicateQueryRow=true;Denied(()=>f.Approve());Eq(f.CreateCalls,0);});
        Check("orphan reversal fails", f => {var row=f.Add(f.Period,12,f.Request);row["cr2c9_entrykind"]=new OptionSetValue(290040002);row["cr2c9_reverses"]=new EntityReference(BalancePersistence.EntryTable,Guid.NewGuid());Denied(()=>f.Approve());});
        Check("valid reversal cannot be deducted again", f => {f.Add(f.Period,112);var debit=f.Approve().Single();var saved=f.Entries.Single(e=>e.GetAttributeValue<int>("cr2c9_hours")<0);var reversal=f.Add(f.Period,12,f.Request);reversal["cr2c9_entrykind"]=new OptionSetValue(290040002);reversal["cr2c9_reverses"]=saved.ToEntityReference();Denied(()=>f.Approve());});
        Check("period locks use deterministic identity order", f => {f.Add(f.Period,112);f.Add(f.Next,112);f.CrossApprove();Eq(string.Join(",",f.LockOrder),string.Join(",",new[]{f.Period.Id,f.Next.Id}.Order()));});
        Check("lock precedes paged history and history precedes writes", f => {f.Add(f.Period,112);f.Approve();Eq(string.Join(",",f.Calls),"read,lock,history,create");});
        Check("held first-year assessment preserves previously recorded use", f => {
            f.Add(f.Period,112);f.Add(f.Period,-12,f.Request);
            var assessment=CalendarAnnualGrant.Assess(new DateTime(2026,1,1),EntitlementCalculator.FirstLevel,EntitlementCalculator.FullTime,true,null,new DateTime(2026,10,10));
            var plan=f.Store().ApplyGrant(f.Period,assessment.Annual);Eq(plan.Status,"PendingPolicy");Eq(plan.ExistingAvailableHours,100);Eq(f.Entries.Count(),2);
        });
        Check("new-year grant never resets the earlier year's remaining hours", f => {
            f.Add(f.Period,112);f.Add(f.Period,-12,f.Request);
            var assessment=CalendarAnnualGrant.Assess(new DateTime(2024,10,11),EntitlementCalculator.FirstLevel,EntitlementCalculator.FullTime,true,null,new DateTime(2027,1,1));
            f.Store().ApplyGrant(f.Next,assessment.Annual);
            Eq(f.Entries.Where(e=>e.GetAttributeValue<EntityReference>("cr2c9_balanceperiod").Id==f.Period.Id).Sum(e=>e.GetAttributeValue<int>("cr2c9_hours")),100);
            Eq(f.Entries.Where(e=>e.GetAttributeValue<EntityReference>("cr2c9_balanceperiod").Id==f.Next.Id).Sum(e=>e.GetAttributeValue<int>("cr2c9_hours")),120);
            Eq(f.Rows[(BalancePersistence.PeriodTable,f.Period.Id)].RowVersion,"1");
        });
        Console.WriteLine($"{count} SDK balance persistence checks passed. Transaction rollback is simulated; registered live integration and concurrency testing remain required.");
    }
}

sealed class BalanceStoreFixture : IOrganizationService
{
    internal readonly Guid Employee=Guid.NewGuid(),Type=Guid.NewGuid(),Request=Guid.NewGuid(),Owner=Guid.NewGuid();
    internal readonly BalancePeriod Period,Next;
    internal readonly AnnualLeaveDate[] Dates={new(new DateTime(2026,10,19),4),new(new DateTime(2026,10,20),8)};
    internal Dictionary<(string,Guid),Entity> Rows=new();
    internal readonly List<string> Calls=new(); internal readonly List<Guid> LockOrder=new();
    internal int CreateCalls,UpdateCalls,QueryCalls,FailCreateNumber,PageSize=5000;
    internal bool Conflict,MissingCookie,DuplicateQueryRow;
    internal string LastCookie;
    internal readonly IPluginExecutionContext Context;
    internal IEnumerable<Entity> Entries=>Rows.Values.Where(e=>e.LogicalName==BalancePersistence.EntryTable);
    internal IEnumerable<Entity> Usage=>Rows.Values.Where(e=>e.LogicalName=="cr2c9_carryforwardusage");
    internal BalanceStoreFixture()
    {
        Context=DispatchProxy.Create<IPluginExecutionContext,ContextProxy>();
        var values=((ContextProxy)(object)Context).Values;values["Stage"]=20;values["Mode"]=0;values["IsInTransaction"]=true;
        Period=new(Guid.NewGuid(),Employee,Type,new DateTime(2026,1,1),new DateTime(2026,12,31));
        Next=new(Guid.NewGuid(),Employee,Type,new DateTime(2027,1,1),new DateTime(2027,12,31));
        foreach(var p in new[]{Period,Next}) {
            var row=new Entity(BalancePersistence.PeriodTable,p.Id){RowVersion="1"};row["cr2c9_employee"]=new EntityReference(Guard.Employee,Employee);
            row["cr2c9_leavetype"]=new EntityReference("cr2c9_leavetype",Type);row["cr2c9_periodstart"]=p.Start;row["cr2c9_periodend"]=p.End;
            row["ownerid"]=new EntityReference("systemuser",Owner);Rows[(row.LogicalName,row.Id)]=row;
        }
    }
    internal BalancePersistence Store()=>new(this,Context);
    internal BalanceEntry[] Approve(AnnualLeaveDate[] dates=null)=>Store().ApplyApprovedAnnualLeave(Employee,Type,Request,dates??Dates,new[]{Period});
    internal BalanceEntry[] CrossApprove()=>Store().ApplyApprovedAnnualLeave(Employee,Type,Request,
        new[]{new AnnualLeaveDate(new DateTime(2026,12,31),4),new AnnualLeaveDate(new DateTime(2027,1,4),8)},new[]{Next,Period});
    internal Entity Add(BalancePeriod period,int hours,Guid request=default)
    {
        var row=new Entity(BalancePersistence.EntryTable,Guid.NewGuid());row["cr2c9_balanceperiod"]=new EntityReference(BalancePersistence.PeriodTable,period.Id);
        row["cr2c9_operationkey"]=row.Id+":fixture";row["cr2c9_hours"]=hours;row["cr2c9_entrykind"]=new OptionSetValue(hours<0?290040001:290040000);
        if(request!=Guid.Empty)row["cr2c9_leaverequest"]=new EntityReference(Guard.Request,request);
        Rows[(row.LogicalName,row.Id)]=row;return row;
    }
    internal Entity AddCarryCredit(int hours)
    {
        var review=new Entity("cr2c9_carryforwardreview",Guid.NewGuid());
        review["cr2c9_reviewstatus"]=new OptionSetValue(290040002);
        review["cr2c9_sourceperiod"]=new EntityReference(BalancePersistence.PeriodTable,Period.Id);
        review["cr2c9_targetperiod"]=new EntityReference(BalancePersistence.PeriodTable,Next.Id);
        review["cr2c9_reviewedcarryhours"]=hours;
        review["cr2c9_operationkey"]="fixture-carry-operation";
        Rows[(review.LogicalName,review.Id)]=review;
        var credit=new Entity("cr2c9_carryforwardcredit",Guid.NewGuid()){RowVersion="1"};
        credit["cr2c9_employee"]=new EntityReference(Guard.Employee,Employee);
        credit["cr2c9_sourceperiod"]=new EntityReference(BalancePersistence.PeriodTable,Period.Id);
        credit["cr2c9_targetperiod"]=new EntityReference(BalancePersistence.PeriodTable,Next.Id);
        credit["cr2c9_review"]=review.ToEntityReference();
        credit["cr2c9_hours"]=hours;credit["cr2c9_operationkey"]="fixture-carry-operation";
        credit["cr2c9_useby"]=new DateTime(2027,3,31);
        credit["ownerid"]=new EntityReference("systemuser",Owner);
        Rows[(credit.LogicalName,credit.Id)]=credit;
        return credit;
    }
    static Entity Copy(Entity e){var r=new Entity(e.LogicalName,e.Id){RowVersion=e.RowVersion};foreach(var a in e.Attributes)r[a.Key]=a.Value;return r;}
    internal void Transaction(Action action){var snapshot=Rows.ToDictionary(p=>p.Key,p=>Copy(p.Value));try{action();}catch{Rows=snapshot;throw;}}
    public Entity Retrieve(string table,Guid id,ColumnSet columns){Calls.Add("read");return Copy(Rows[(table,id)]);}
    public OrganizationResponse Execute(OrganizationRequest request)
    {
        if(request is not UpdateRequest update)throw new Exception("Unexpected SDK batch/message");
        UpdateCalls++;Calls.Add("lock");var row=Rows[(update.Target.LogicalName,update.Target.Id)];
        if(Conflict || update.ConcurrencyBehavior!=ConcurrencyBehavior.IfRowVersionMatches || update.Target.RowVersion!=row.RowVersion)
            throw new InvalidPluginExecutionException("Refresh/retry concurrency conflict");
        var field=row.LogicalName=="cr2c9_carryforwardcredit"?"cr2c9_locktoken":Guard.LockField;
        if(update.Target.Attributes.Count!=1 || !update.Target.Contains(field))throw new Exception("Unexpected lock write");
        row[field]=update.Target[field];row.RowVersion=(int.Parse(row.RowVersion)+1).ToString();LockOrder.Add(row.Id);return new UpdateResponse();
    }
    public EntityCollection RetrieveMultiple(QueryBase query)
    {
        var q=(QueryExpression)query;QueryCalls++;Calls.Add("history");LastCookie=q.PageInfo.PagingCookie;
        if(q.EntityName=="cr2c9_carryforwardcredit" && q.TopCount==2) {
            var target=(Guid)q.Criteria.Conditions.Single().Values.Single();
            return new EntityCollection(Rows.Values.Where(e=>e.LogicalName==q.EntityName &&
                e.GetAttributeValue<EntityReference>("cr2c9_targetperiod").Id==target).Select(Copy).ToArray());
        }
        if(q.EntityName==BalancePersistence.PeriodTable && q.TopCount==2) {
            var conditions=q.Criteria.Conditions.ToDictionary(c=>c.AttributeName,c=>c.Values.Single());
            return new EntityCollection(Rows.Values.Where(e=>e.LogicalName==q.EntityName &&
                e.GetAttributeValue<EntityReference>("cr2c9_employee").Id==(Guid)conditions["cr2c9_employee"] &&
                e.GetAttributeValue<EntityReference>("cr2c9_leavetype").Id==(Guid)conditions["cr2c9_leavetype"] &&
                e.GetAttributeValue<DateTime>("cr2c9_periodstart")==((DateTime)conditions["cr2c9_periodstart"]) &&
                e.GetAttributeValue<DateTime>("cr2c9_periodend")==((DateTime)conditions["cr2c9_periodend"]))
                .Take(2).Select(Copy).ToArray());
        }
        if(q.EntityName=="cr2c9_carryforwardusage") {
            if(q.TopCount==2) {
                var key=(string)q.Criteria.Conditions.Single().Values.Single();
                return new EntityCollection(Usage.Where(e=>e.GetAttributeValue<string>("cr2c9_operationkey")==key)
                    .Take(2).Select(Copy).ToArray());
            }
            var credit=(Guid)q.Criteria.Conditions.Single().Values.Single();
            return new EntityCollection(Usage.Where(e=>e.GetAttributeValue<EntityReference>("cr2c9_credit").Id==credit)
                .OrderBy(e=>e.Id).Select(Copy).ToArray());
        }
        if(q.EntityName==BalancePersistence.EntryTable && q.TopCount==2 && q.Criteria.Conditions.Count==1 && q.Criteria.Conditions[0].AttributeName=="cr2c9_operationkey") {
            var key=(string)q.Criteria.Conditions[0].Values.Single();
            return new EntityCollection(Entries.Where(e=>e.GetAttributeValue<string>("cr2c9_operationkey")==key).Take(2).Select(Copy).ToArray());
        }
        if(q.EntityName!=BalancePersistence.EntryTable || q.Orders.Count!=1 || q.Orders[0].AttributeName!=BalancePersistence.EntryTable+"id")throw new Exception("Unstable history query");
        var id=(Guid)q.Criteria.Conditions.Single().Values.Single();
        var all=Entries.Where(e=>e.GetAttributeValue<EntityReference>("cr2c9_balanceperiod").Id==id).OrderBy(e=>e.Id).ToArray();
        if(q.PageInfo.PageNumber>1 && q.PageInfo.PagingCookie!="page-"+(q.PageInfo.PageNumber-1))throw new Exception("Wrong paging cookie");
        var rows=all.Skip((q.PageInfo.PageNumber-1)*PageSize).Take(PageSize).Select(Copy).ToList();
        if(DuplicateQueryRow && rows.Count>0)rows.Add(Copy(rows[0]));
        var result=new EntityCollection(rows){MoreRecords=q.PageInfo.PageNumber*PageSize<all.Length};
        result.PagingCookie=MissingCookie?null:"page-"+q.PageInfo.PageNumber;return result;
    }
    public Guid Create(Entity entity){CreateCalls++;Calls.Add("create");if(FailCreateNumber==CreateCalls)throw new InvalidPluginExecutionException("Injected persistence failure");entity.Id=Guid.NewGuid();Rows[(entity.LogicalName,entity.Id)]=Copy(entity);return entity.Id;}
    public void Update(Entity e)=>throw new NotSupportedException();public void Delete(string t,Guid id)=>throw new NotSupportedException();
    public void Associate(string t,Guid id,Relationship r,EntityReferenceCollection e)=>throw new NotSupportedException();public void Disassociate(string t,Guid id,Relationship r,EntityReferenceCollection e)=>throw new NotSupportedException();
}
