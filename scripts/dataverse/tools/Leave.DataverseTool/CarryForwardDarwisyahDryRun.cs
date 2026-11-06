using Leave.Plugins;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

internal static partial class Development
{
    // Read-only rehearsal using Darwisyah's real Annual Leave ledger. No
    // leave request, period, HR review, exception, or credit is created.
    static void CarryForwardDarwisyahDryRun()
    {
        const string email = "Darwisyah.Fakhira@Asteriskdigital.com.my";
        var today = EntitlementCalculator.MalaysiaDate(DateTime.UtcNow);
        var year = today.Year;
        var annualType = Guid.Parse("959c93de-1197-4de4-a660-2e2c3266f0fe");
        var employees = Find(Employee, "cr2c9_email", email,
            "cr2c9_employeename", "cr2c9_email", "cr2c9_isactive", "statecode");
        if (employees.Length != 1 ||
            employees[0].GetAttributeValue<string>("cr2c9_employeename") != "Darwisyah Fakhira" ||
            employees[0].GetAttributeValue<OptionSetValue>("statecode")?.Value != 0 ||
            !employees[0].GetAttributeValue<bool>("cr2c9_isactive"))
            throw new Exception("Darwisyah's active Employee identity did not verify.");
        var employee = employees[0];
        var periods = Find(BalancePeriodTable, "cr2c9_employee", employee.Id,
            "cr2c9_leavetype", "cr2c9_periodstart", "cr2c9_periodend")
            .Where(p => p.GetAttributeValue<EntityReference>("cr2c9_leavetype")?.Id == annualType &&
                p.GetAttributeValue<DateTime?>("cr2c9_periodstart") == new DateTime(year, 1, 1) &&
                p.GetAttributeValue<DateTime?>("cr2c9_periodend") == new DateTime(year, 12, 31))
            .ToArray();
        if (periods.Length != 1)
            throw new Exception("Exactly one current-year Annual Leave period is needed for the dry run.");
        var scope = new BalancePeriod(periods[0].Id, employee.Id, annualType,
            new DateTime(year, 1, 1), new DateTime(year, 12, 31));
        var query = new QueryExpression(BalanceEntryTable) {
            ColumnSet = new ColumnSet("cr2c9_balanceperiod", "cr2c9_operationkey", "cr2c9_entrykind",
                "cr2c9_hours", "cr2c9_leaverequest", "cr2c9_reverses"),
            PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 }
        };
        query.Criteria.AddCondition("cr2c9_balanceperiod", ConditionOperator.Equal, scope.Id);
        query.AddOrder(BalanceEntryTable + "id", OrderType.Ascending);
        var rows = new List<Entity>();
        while (true) {
            var page = Client.RetrieveMultiple(query);
            rows.AddRange(page.Entities);
            if (rows.Count > 20000) throw new Exception("The Annual Leave ledger is too large for this rehearsal.");
            if (!page.MoreRecords) break;
            if (string.IsNullOrWhiteSpace(page.PagingCookie) || page.Entities.Count == 0)
                throw new Exception("The complete Annual Leave ledger could not be read.");
            query.PageInfo.PageNumber++;
            query.PageInfo.PagingCookie = page.PagingCookie;
        }
        if (rows.GroupBy(r => r.Id).Any(g => g.Count() != 1))
            throw new Exception("Duplicate ledger rows require investigation.");
        var byId = rows.ToDictionary(r => r.Id);
        var entries = rows.Select(row => {
            if (row.GetAttributeValue<EntityReference>("cr2c9_balanceperiod")?.Id != scope.Id ||
                !(row.GetAttributeValue<object>("cr2c9_hours") is int hours))
                throw new Exception("A ledger row has an invalid balance or amount.");
            var kind = row.GetAttributeValue<OptionSetValue>("cr2c9_entrykind")?.Value;
            if (kind is not (290040000 or 290040001 or 290040002))
                throw new Exception("A ledger row has an unknown entry kind.");
            var reversal = row.GetAttributeValue<EntityReference>("cr2c9_reverses");
            string? reverses = null;
            if (reversal != null) {
                if (!byId.TryGetValue(reversal.Id, out var original))
                    throw new Exception("A reversal's original debit is missing.");
                reverses = original.GetAttributeValue<string>("cr2c9_operationkey");
            }
            return new BalanceEntry(scope.Id, row.GetAttributeValue<string>("cr2c9_operationkey"),
                (BalanceEntryKind)(kind.Value - 290040000), hours,
                row.GetAttributeValue<EntityReference>("cr2c9_leaverequest")?.Id ?? Guid.Empty,
                reverses);
        }).ToArray();
        var unused = BalanceLedger.AvailableHours(scope, entries);
        var exceptions = Find(CarryForwardExceptionTable, "cr2c9_sourceperiod", scope.Id,
            "cr2c9_employee", "cr2c9_approvedlimithours", "cr2c9_documentreference",
            "cr2c9_superiorentraid", "cr2c9_verifiedby", "cr2c9_verifiedon", "createdon");
        if (exceptions.Length > 1) throw new Exception("Duplicate written exceptions require investigation.");
        int? special = null;
        if (exceptions.Length == 1) {
            var written = exceptions[0];
            if (written.GetAttributeValue<EntityReference>("cr2c9_employee")?.Id != employee.Id ||
                !Guid.TryParse(written.GetAttributeValue<string>("cr2c9_superiorentraid"), out var superior) ||
                string.IsNullOrWhiteSpace(written.GetAttributeValue<string>("cr2c9_verifiedby")))
                throw new Exception("The written exception's verification is incomplete.");
            special = written.GetAttributeValue<int>("cr2c9_approvedlimithours");
            CarryForwardExceptionPolicy.Validate(year,
                EntitlementCalculator.MalaysiaDate(written.GetAttributeValue<DateTime>("cr2c9_verifiedon")),
                special.Value, written.GetAttributeValue<string>("cr2c9_documentreference"), superior);
            CarryForwardExceptionPolicy.Validate(year,
                EntitlementCalculator.MalaysiaDate(written.GetAttributeValue<DateTime>("createdon")),
                special.Value, written.GetAttributeValue<string>("cr2c9_documentreference"), superior);
        }
        var draft = CarryForwardDraft.Calculate(unused, special, special.HasValue);
        var reviews = Find(CarryForwardReviewTable, "cr2c9_sourceperiod", scope.Id);
        var credits = Find(CarryForwardCreditTable, "cr2c9_sourceperiod", scope.Id);
        Console.WriteLine("READ-ONLY CARRY-FORWARD REHEARSAL (balance as of " + today.ToString("yyyy-MM-dd") + ")");
        Console.WriteLine("Employee: Darwisyah Fakhira; closing year: " + year + ".");
        Console.WriteLine("Ledger entries: " + entries.Length + "; recorded unused: " + unused + " hours (" + unused / 8m + " days).");
        Console.WriteLine("Verified limit: " + draft.LimitHours + " hours (" + draft.LimitHours / 8m +
            " days); written exception: " + (special.HasValue ? "yes" : "no") + ".");
        Console.WriteLine("If this were the final unchanged year-end ledger, candidate credit: " +
            draft.CarryForwardHours + " hours (" + draft.CarryForwardHours / 8m +
            " days); separate unused: " + draft.SeparatelyRecordedUnusedHours + " hours (" +
            draft.SeparatelyRecordedUnusedHours / 8m + " days).");
        Console.WriteLine("Existing HR reviews: " + reviews.Length + "; credits: " + credits.Length +
            ". No records were written. This is not a year-end confirmation.");
    }
}
