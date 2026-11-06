using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

internal static partial class Development
{
    // Creates one explicitly synthetic, internally consistent Development-only
    // cancellation fixture. No human approval is represented and no flow runs.
    static void CancellationOwnTestFixture()
    {
        var ownId = Guid.Parse("6938b1f2-22c2-f111-aaad-000d3a826b69");
        var annualId = Guid.Parse("959c93de-1197-4de4-a660-2e2c3266f0fe");
        var day = new DateTime(2026, 11, 16);
        const string marker = "SYNTHETIC DEVELOPMENT CANCELLATION TEST — no Superior decision; not a real leave approval.";
        var who = (WhoAmIResponse)Client.Execute(new WhoAmIRequest());
        var account = Client.Retrieve("systemuser", who.UserId,
            new ColumnSet("azureactivedirectoryobjectid", "isdisabled"));
        var employee = Client.Retrieve(Employee, ownId,
            new ColumnSet("cr2c9_entrauserid", "cr2c9_isactive", "statecode"));
        if (account.GetAttributeValue<bool>("isdisabled") || !employee.GetAttributeValue<bool>("cr2c9_isactive") ||
            employee.GetAttributeValue<OptionSetValue>("statecode")?.Value != 0 ||
            !Guid.TryParse(employee.GetAttributeValue<string>("cr2c9_entrauserid"), out var employeeIdentity) ||
            employeeIdentity != account.GetAttributeValue<Guid>("azureactivedirectoryobjectid"))
            throw new Exception("Signed-in development identity does not match the active Employee fixture.");
        if (day <= DateTime.UtcNow.AddHours(8).Date)
            throw new Exception("The cancellation fixture date must still be in the future.");
        if (Find(BalancePeriodTable, "cr2c9_employee", ownId).Length != 0 ||
            Find(Request, "cr2c9_reason", marker).Length != 0)
            throw new Exception("Own balance or cancellation fixture already exists; inspect before retrying.");
        var flow = Client.Retrieve("workflow", HrNotifyFlow, new ColumnSet("clientdata", "statecode"));
        var definition = flow.GetAttributeValue<string>("clientdata") ?? "";
        if (flow.GetAttributeValue<OptionSetValue>("statecode")?.Value != 1 ||
            !definition.Contains("cr2c9_decisionbyentraid ne null") ||
            !definition.Contains("cr2c9_decisionon ne null"))
            throw new Exception("Approval notification flow preflight differs; fixture cannot guarantee no email.");

        var owner = new EntityReference("systemuser", who.UserId);
        var employeeRef = new EntityReference(Employee, ownId);
        var typeRef = new EntityReference("cr2c9_leavetype", annualId);
        var periodId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var periodRef = new EntityReference(BalancePeriodTable, periodId);
        var requestRef = new EntityReference(Request, requestId);
        var period = new Entity(BalancePeriodTable, periodId) {
            ["cr2c9_name"] = "SYNTHETIC cancellation test 2026",
            ["cr2c9_employee"] = employeeRef,
            ["cr2c9_leavetype"] = typeRef,
            ["cr2c9_periodstart"] = new DateTime(2026, 1, 1),
            ["cr2c9_periodend"] = new DateTime(2026, 12, 31),
            ["ownerid"] = owner
        };
        var grantKey = periodId.ToString("D") + ":annual-grant:8";
        var grant = new Entity(BalanceEntryTable) {
            ["cr2c9_name"] = grantKey,
            ["cr2c9_operationkey"] = grantKey,
            ["cr2c9_balanceperiod"] = periodRef,
            ["cr2c9_entrykind"] = new OptionSetValue(290040000),
            ["cr2c9_hours"] = 8,
            ["ownerid"] = owner
        };
        var request = new Entity(Request, requestId) {
            ["cr2c9_employeename"] = employeeRef,
            ["cr2c9_leavetypename"] = typeRef,
            ["cr2c9_status"] = new OptionSetValue(1),
            ["cr2c9_startdate"] = day,
            ["cr2c9_enddate"] = day,
            ["cr2c9_reason"] = marker,
            ["ownerid"] = owner
        };
        var date = new Entity("cr2c9_leaverequestdate") {
            ["cr2c9_entryname"] = "SYNTHETIC cancellation test date",
            ["cr2c9_leaverequest"] = requestRef,
            ["cr2c9_leavedate"] = day,
            ["cr2c9_duration"] = new OptionSetValue(290040001),
            ["ownerid"] = owner
        };
        var debitKey = requestId.ToString("D") + ":approved:" + periodId.ToString("D");
        var debit = new Entity(BalanceEntryTable) {
            ["cr2c9_name"] = debitKey,
            ["cr2c9_operationkey"] = debitKey,
            ["cr2c9_balanceperiod"] = periodRef,
            ["cr2c9_leaverequest"] = requestRef,
            ["cr2c9_entrykind"] = new OptionSetValue(290040001),
            ["cr2c9_hours"] = -8,
            ["ownerid"] = owner
        };
        CreateRequest Suppressed(Entity row) {
            var operation = new CreateRequest { Target = row };
            operation.Parameters.Add("BypassBusinessLogicExecution", "CustomSync");
            operation.Parameters.Add("SuppressCallbackRegistrationExpanderJob", true);
            return operation;
        }
        Client.Execute(new ExecuteTransactionRequest {
            Requests = new OrganizationRequestCollection {
                Suppressed(period), Suppressed(grant), Suppressed(request),
                Suppressed(date), Suppressed(debit)
            },
            ReturnResponses = false
        });

        var savedRequest = Client.Retrieve(Request, requestId,
            new ColumnSet("cr2c9_reason", "cr2c9_status", "cr2c9_startdate", "cr2c9_enddate",
                "cr2c9_employeename", "cr2c9_leavetypename", "cr2c9_decisionon"));
        var savedDate = Find("cr2c9_leaverequestdate", "cr2c9_leaverequest", requestId,
            "cr2c9_leavedate", "cr2c9_duration");
        var entries = Find(BalanceEntryTable, "cr2c9_balanceperiod", periodId,
            "cr2c9_hours", "cr2c9_entrykind", "cr2c9_leaverequest", "cr2c9_operationkey");
        if (savedRequest.GetAttributeValue<OptionSetValue>("cr2c9_status")?.Value != 1 ||
            savedRequest.GetAttributeValue<string>("cr2c9_reason") != marker ||
            savedRequest.GetAttributeValue<EntityReference>("cr2c9_employeename")?.Id != ownId ||
            savedRequest.GetAttributeValue<EntityReference>("cr2c9_leavetypename")?.Id != annualId ||
            savedRequest.Contains("cr2c9_decisionon") || savedDate.Length != 1 ||
            savedDate[0].GetAttributeValue<DateTime>("cr2c9_leavedate") != day ||
            savedDate[0].GetAttributeValue<OptionSetValue>("cr2c9_duration")?.Value != 290040001 ||
            entries.Length != 2 || entries.Sum(e => e.GetAttributeValue<int>("cr2c9_hours")) != 0 ||
            entries.Count(e => e.GetAttributeValue<OptionSetValue>("cr2c9_entrykind")?.Value == 290040000 && e.GetAttributeValue<int>("cr2c9_hours") == 8) != 1 ||
            entries.Count(e => e.GetAttributeValue<OptionSetValue>("cr2c9_entrykind")?.Value == 290040001 &&
                e.GetAttributeValue<int>("cr2c9_hours") == -8 &&
                e.GetAttributeValue<EntityReference>("cr2c9_leaverequest")?.Id == requestId &&
                e.GetAttributeValue<string>("cr2c9_operationkey") == debitKey) != 1)
            throw new Exception("Synthetic fixture postflight failed; inspect request " + requestId);
        Console.WriteLine("PASS synthetic own-account future cancellation fixture; request=" + requestId +
            "; period=" + periodId + "; date=2026-11-16; grant=8; debit=-8; available=0; no human decision stamp or notification.");
    }
}
