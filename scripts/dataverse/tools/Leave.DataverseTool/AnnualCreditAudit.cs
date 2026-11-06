using System.Text.Json;
using Leave.Plugins;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

internal static partial class Development
{
    static void AnnualGrantPreflight()
    {
        var today = EntitlementCalculator.MalaysiaDate(DateTime.UtcNow);
        var start = new DateTime(today.Year,1,1);
        var end = new DateTime(today.Year,12,31);
        var annualType = Guid.Parse("959c93de-1197-4de4-a660-2e2c3266f0fe");
        var query = new QueryExpression(Employee) {
            ColumnSet = new ColumnSet("cr2c9_employeename","cr2c9_email","cr2c9_datejoined",
                "cr2c9_employeelevel","cr2c9_employmenttype","cr2c9_isactive","cr2c9_leavingdate","statecode"),
            PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 }
        };
        var employees = Client.RetrieveMultiple(query);
        if (employees.MoreRecords) throw new Exception("Grant preflight requires paging before more than 5000 employees.");
        var eligible = 0; var held = 0; var realCredits = 0; var testCredits = 0;
        foreach (var employee in employees.Entities.OrderBy(e=>e.GetAttributeValue<string>("cr2c9_employeename"))) {
            var name = employee.GetAttributeValue<string>("cr2c9_employeename") ?? employee.Id.ToString();
            var email = employee.GetAttributeValue<string>("cr2c9_email") ?? "";
            var synthetic = email.EndsWith("@example.invalid",StringComparison.OrdinalIgnoreCase);
            if (employee.GetAttributeValue<OptionSetValue>("statecode")?.Value != 0) continue;
            int cap;
            try {
                cap = CalendarAnnualGrant.ApprovalEarnedCap(
                    employee.GetAttributeValue<DateTime?>("cr2c9_datejoined"),
                    employee.GetAttributeValue<OptionSetValue>("cr2c9_employeelevel")?.Value,
                    employee.GetAttributeValue<OptionSetValue>("cr2c9_employmenttype")?.Value,
                    employee.Contains("cr2c9_isactive") ? employee.GetAttributeValue<bool>("cr2c9_isactive") : null,
                    employee.GetAttributeValue<DateTime?>("cr2c9_leavingdate"),start,today);
            } catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) {
                held++;
                Console.WriteLine("HOLD " + name + ": " + ex.Message);
                continue;
            }
            eligible++;
            var periods = Find(BalancePeriodTable,"cr2c9_employee",employee.Id,
                "cr2c9_leavetype","cr2c9_periodstart","cr2c9_periodend")
                .Where(p=>p.GetAttributeValue<EntityReference>("cr2c9_leavetype")?.Id==annualType &&
                    p.GetAttributeValue<DateTime>("cr2c9_periodstart")<=end &&
                    p.GetAttributeValue<DateTime>("cr2c9_periodend")>=start).ToArray();
            if (periods.Length>1 || periods.Length==1 &&
                (periods[0].GetAttributeValue<DateTime>("cr2c9_periodstart")!=start ||
                 periods[0].GetAttributeValue<DateTime>("cr2c9_periodend")!=end))
                throw new Exception("Overlapping or noncalendar annual period for " + name);
            var grants=periods.Length==0?0:Find(BalanceEntryTable,"cr2c9_balanceperiod",periods[0].Id,
                "cr2c9_entrykind","cr2c9_hours")
                .Where(e=>e.GetAttributeValue<OptionSetValue>("cr2c9_entrykind")?.Value==290040000)
                .Sum(e=>e.GetAttributeValue<int>("cr2c9_hours"));
            var delta=Math.Max(0,cap-grants);
            if(synthetic)testCredits+=delta;else realCredits+=delta;
            Console.WriteLine((synthetic?"TEST ":"REAL ")+name+": earned="+cap+"h, granted="+grants+"h, next credit="+delta+"h");
        }
        Console.WriteLine("Preflight only: eligible="+eligible+", held="+held+
            ", expected real credit="+realCredits+"h, expected fictional credit="+testCredits+"h. No grants run.");
    }

    static void AnnualCreditAudit()
    {
        var today = EntitlementCalculator.MalaysiaDate(DateTime.UtcNow);
        var annualType = Guid.Parse("959c93de-1197-4de4-a660-2e2c3266f0fe");
        var query = new QueryExpression(BalancePeriodTable) {
            ColumnSet = new ColumnSet("cr2c9_employee","cr2c9_leavetype","cr2c9_periodstart","cr2c9_periodend","cr2c9_name"),
            PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 }
        };
        query.Criteria.AddCondition("cr2c9_leavetype",ConditionOperator.Equal,annualType);
        var periods = Client.RetrieveMultiple(query);
        if (periods.MoreRecords) throw new Exception("Annual period audit requires paging before more than 5000 records.");
        var rows = new List<object>();
        foreach (var period in periods.Entities.OrderBy(p=>p.Id)) {
            var start = period.GetAttributeValue<DateTime>("cr2c9_periodstart");
            var end = period.GetAttributeValue<DateTime>("cr2c9_periodend");
            if (start.Year != today.Year) continue;
            var employeeId = period.GetAttributeValue<EntityReference>("cr2c9_employee")?.Id ?? Guid.Empty;
            if (employeeId == Guid.Empty) throw new Exception("Annual period has no Employee: " + period.Id);
            var employee = Client.Retrieve(Employee,employeeId,
                new ColumnSet("cr2c9_employeename","cr2c9_email","cr2c9_datejoined","cr2c9_employeelevel",
                    "cr2c9_employmenttype","cr2c9_isactive","cr2c9_leavingdate"));
            var entries = Find(BalanceEntryTable,"cr2c9_balanceperiod",period.Id,
                "cr2c9_hours","cr2c9_entrykind","cr2c9_operationkey","createdon");
            if (entries.Any(e=>string.IsNullOrWhiteSpace(e.GetAttributeValue<string>("cr2c9_operationkey"))) ||
                entries.GroupBy(e=>e.GetAttributeValue<string>("cr2c9_operationkey"),StringComparer.Ordinal)
                    .Any(group=>group.Count()!=1))
                throw new Exception("Missing or duplicate annual ledger operation key for period " + period.Id);
            var grants = entries.Where(e=>e.GetAttributeValue<OptionSetValue>("cr2c9_entrykind")?.Value==290040000)
                .Sum(e=>e.GetAttributeValue<int>("cr2c9_hours"));
            var grantEntries = entries.Where(e=>e.GetAttributeValue<OptionSetValue>("cr2c9_entrykind")?.Value==290040000)
                .OrderBy(e=>e.GetAttributeValue<DateTime>("createdon")).ToArray();
            var debits = entries.Where(e=>e.GetAttributeValue<OptionSetValue>("cr2c9_entrykind")?.Value==290040001)
                .Sum(e=>-e.GetAttributeValue<int>("cr2c9_hours"));
            var reversals = entries.Where(e=>e.GetAttributeValue<OptionSetValue>("cr2c9_entrykind")?.Value==290040002)
                .Sum(e=>e.GetAttributeValue<int>("cr2c9_hours"));
            if (entries.Any(e=>e.GetAttributeValue<OptionSetValue>("cr2c9_entrykind")?.Value is not (290040000 or 290040001 or 290040002)))
                throw new Exception("Unknown ledger kind for period " + period.Id);
            var used = debits-reversals;
            int? cap = null;
            string? hold = null;
            try {
                if (start != new DateTime(today.Year,1,1) || end != new DateTime(today.Year,12,31))
                    throw new InvalidOperationException("Period boundaries differ from the calendar year.");
                cap = CalendarAnnualGrant.ApprovalEarnedCap(
                    employee.GetAttributeValue<DateTime?>("cr2c9_datejoined"),
                    employee.GetAttributeValue<OptionSetValue>("cr2c9_employeelevel")?.Value,
                    employee.GetAttributeValue<OptionSetValue>("cr2c9_employmenttype")?.Value,
                    employee.Contains("cr2c9_isactive") ? employee.GetAttributeValue<bool>("cr2c9_isactive") : null,
                    employee.GetAttributeValue<DateTime?>("cr2c9_leavingdate"),start,today);
            } catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) {
                hold = ex.Message;
            }
            rows.Add(new {
                periodId=period.Id,employeeId,
                employeeName=employee.GetAttributeValue<string>("cr2c9_employeename"),
                employeeEmail=employee.GetAttributeValue<string>("cr2c9_email"),
                synthetic=(employee.GetAttributeValue<string>("cr2c9_email")??"").EndsWith("@example.invalid",StringComparison.OrdinalIgnoreCase),
                periodStart=start,periodEnd=end,grantedHours=grants,usedHours=used,
                recordedRemainingHours=grants-used,earnedCapHours=cap,
                excessCreditHours=cap.HasValue?(int?)Math.Max(0,grants-cap.Value):null,
                overusedHours=cap.HasValue?(int?)Math.Max(0,used-cap.Value):null,
                holdReason=hold,entryCount=entries.Length,
                grantEntries=grantEntries.Select(e=>new {
                    hours=e.GetAttributeValue<int>("cr2c9_hours"),
                    operationKey=e.GetAttributeValue<string>("cr2c9_operationkey"),
                    createdOn=e.GetAttributeValue<DateTime>("createdon")
                }).ToArray()
            });
        }
        var path=Path.Combine(Path.GetTempPath(),"annual-credit-audit-"+today.ToString("yyyy-MM-dd")+".json");
        File.WriteAllText(path,JsonSerializer.Serialize(new {assessmentDate=today,rows},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("Annual credit audit: "+rows.Count+" current-year periods. Saved "+path);
        Console.WriteLine(File.ReadAllText(path));
    }
}
