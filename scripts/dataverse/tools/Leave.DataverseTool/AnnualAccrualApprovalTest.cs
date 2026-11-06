using System.ServiceModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

internal static partial class Development
{
    static string AccrualFixtureCondition(Guid id) =>
        "@not(equals(triggerOutputs()?['body/_cr2c9_employeename_value'], '" + id + "'))";

    static void ExcludeAccrualFixtureFromHrEmail(Guid id)
    {
        var flow = Client.Retrieve("workflow", HrNotifyFlow, new ColumnSet("clientdata", "statecode"));
        var json = JsonNode.Parse(flow.GetAttributeValue<string>("clientdata"))!;
        var trigger = json["properties"]!["definition"]!["triggers"]!.AsObject().Single().Value!;
        var conditions = trigger["conditions"] as JsonArray;
        if (conditions == null) throw new Exception("HR notification trigger conditions are missing; no test approval attempted.");
        if (!conditions.Any(x => x?["expression"]?.GetValue<string>() == FixtureCondition))
            throw new Exception("Existing fictional-accounting email exclusion is missing.");
        var condition = AccrualFixtureCondition(id);
        if (!conditions.Any(x => x?["expression"]?.GetValue<string>() == condition)) {
            conditions.Add(new JsonObject { ["expression"] = condition });
            Client.Update(new Entity("workflow", HrNotifyFlow) { ["statecode"] = new OptionSetValue(0) });
            Client.Update(new Entity("workflow", HrNotifyFlow) { ["clientdata"] = json.ToJsonString() });
            Client.Update(new Entity("workflow", HrNotifyFlow) { ["statecode"] = new OptionSetValue(1) });
        }
        var after = Client.Retrieve("workflow", HrNotifyFlow, new ColumnSet("clientdata", "statecode"));
        var saved = JsonNode.Parse(after.GetAttributeValue<string>("clientdata"))!;
        var savedConditions = saved["properties"]!["definition"]!["triggers"]!.AsObject().Single().Value!["conditions"] as JsonArray;
        if (after.GetAttributeValue<OptionSetValue>("statecode")?.Value != 1 ||
            savedConditions == null || !savedConditions.Any(x => x?["expression"]?.GetValue<string>() == condition) ||
            !savedConditions.Any(x => x?["expression"]?.GetValue<string>() == FixtureCondition))
            throw new Exception("Both fictional HR-email exclusions must be active before test approval.");
    }

    static void AnnualAccrualApprovalTest()
    {
        var fixturePath = Root + "/annual-accrual-fixture-2026-10-14.json";
        if (!File.Exists(fixturePath)) throw new Exception("Run the earned-grant fixture first.");
        using var fixture = JsonDocument.Parse(File.ReadAllText(fixturePath));
        var employeeId = fixture.RootElement.GetProperty("employeeId").GetGuid();
        var employee = Client.Retrieve(Employee, employeeId, new ColumnSet("cr2c9_employeename", "cr2c9_entrauserid", "cr2c9_superior"));
        if (employee.GetAttributeValue<string>("cr2c9_employeename") != "SYNTHETIC earned annual accrual test")
            throw new Exception("Fictional test Employee identity differs.");
        var period = Find(BalancePeriodTable,"cr2c9_employee",employeeId).Single();
        var entries = Find(BalanceEntryTable,"cr2c9_balanceperiod",period.Id,"cr2c9_hours");
        if (entries.Length != 1 || entries[0].GetAttributeValue<int>("cr2c9_hours") != 84)
            throw new Exception("Test requires the untouched 84-hour synthetic grant.");
        ExcludeAccrualFixtureFromHrEmail(employeeId);
        var requestsPath = Root + "/annual-accrual-approval-fixture-2026-10-14.json";
        Dictionary<string,Guid> requests;
        if (File.Exists(requestsPath)) {
            using var prior = JsonDocument.Parse(File.ReadAllText(requestsPath));
            if (prior.RootElement.GetProperty("employeeId").GetGuid() != employeeId)
                throw new Exception("Stored approval fixture belongs to another Employee.");
            requests = prior.RootElement.GetProperty("requests").EnumerateObject().ToDictionary(p => p.Name,p => p.Value.GetGuid());
        } else {
            var who = (Microsoft.Crm.Sdk.Messages.WhoAmIResponse)Client.Execute(new Microsoft.Crm.Sdk.Messages.WhoAmIRequest());
            var actor = Client.Retrieve("systemuser",who.UserId,new ColumnSet("azureactivedirectoryobjectid"));
            var actorIdentity = actor.GetAttributeValue<Guid>("azureactivedirectoryobjectid").ToString("D");
            var ownId = Guid.Parse("6938b1f2-22c2-f111-aaad-000d3a826b69");
            var own = Client.Retrieve(Employee,ownId,new ColumnSet("cr2c9_entrauserid","cr2c9_isactive","statecode"));
            if (own.GetAttributeValue<string>("cr2c9_entrauserid") != actorIdentity ||
                !own.GetAttributeValue<bool>("cr2c9_isactive") || own.GetAttributeValue<OptionSetValue>("statecode")?.Value != 0)
                throw new Exception("Signed-in Superior identity differs; no requests created.");
            var fictionalIdentity = employee.GetAttributeValue<string>("cr2c9_entrauserid");
            if (!Guid.TryParse(fictionalIdentity,out _)) throw new Exception("Fictional Employee identity is missing.");
            requests = new Dictionary<string,Guid> {
                ["above-earned"] = Guid.NewGuid(),
                ["within-earned"] = Guid.NewGuid()
            };
            File.WriteAllText(requestsPath,JsonSerializer.Serialize(new {employeeId,requests},new JsonSerializerOptions{WriteIndented=true}));
            var start = new DateTime(DateTime.UtcNow.AddHours(8).Year,11,2);
            void AddRequest(string label,int weekdays) {
                var days = Enumerable.Range(0,30).Select(i => start.AddDays(i))
                    .Where(d => d.DayOfWeek != DayOfWeek.Saturday && d.DayOfWeek != DayOfWeek.Sunday)
                    .Take(weekdays).ToArray();
                var id = requests[label];
                var existing = new QueryExpression(Request) { ColumnSet = new ColumnSet(false), TopCount = 1 };
                existing.Criteria.AddCondition(Request + "id",ConditionOperator.Equal,id);
                if (Client.RetrieveMultiple(existing).Entities.Count != 0) return;
                Client.Create(new Entity(Request,id) {
                    ["cr2c9_employeename"] = new EntityReference(Employee,employeeId),
                    ["cr2c9_leavetypename"] = new EntityReference("cr2c9_leavetype",Guid.Parse("959c93de-1197-4de4-a660-2e2c3266f0fe")),
                    ["cr2c9_status"] = new OptionSetValue(0),
                    ["cr2c9_startdate"] = days.First(),["cr2c9_enddate"] = days.Last(),
                    ["cr2c9_hoursperday"] = 8m,
                    ["cr2c9_reason"] = "SYNTHETIC earned-cap approval test: " + label
                });
            }
            var swap = new OrganizationRequestCollection {
                new UpdateRequest { Target = new Entity(Employee,ownId) { RowVersion = own.RowVersion,
                    ["cr2c9_entrauserid"] = Guid.NewGuid().ToString("D")
                }, ConcurrencyBehavior = ConcurrencyBehavior.IfRowVersionMatches },
                new UpdateRequest { Target = new Entity(Employee,employeeId) {
                    ["cr2c9_entrauserid"] = actorIdentity,
                    ["cr2c9_superior"] = new EntityReference(Employee,ownId)
                }}
            };
            Client.Execute(new ExecuteTransactionRequest { Requests = swap, ReturnResponses = false });
            try {
                AddRequest("above-earned",11); // 88h while only 84h earned.
                AddRequest("within-earned",1); // 8h.
            } finally {
                var restore = new OrganizationRequestCollection {
                    new UpdateRequest { Target = new Entity(Employee,employeeId) {
                        ["cr2c9_entrauserid"] = fictionalIdentity
                    }},
                    new UpdateRequest { Target = new Entity(Employee,ownId) {
                        ["cr2c9_entrauserid"] = actorIdentity
                    }}
                };
                Client.Execute(new ExecuteTransactionRequest { Requests = restore, ReturnResponses = false });
            }
            var restored = Client.Retrieve(Employee,ownId,new ColumnSet("cr2c9_entrauserid"));
            if (restored.GetAttributeValue<string>("cr2c9_entrauserid") != actorIdentity)
                throw new Exception("Developer identity did not restore; stop test.");
        }
        Entity Decision(Guid id) {
            var request = Client.Retrieve(Request,id,new ColumnSet("cr2c9_employeename","cr2c9_status"));
            if (request.GetAttributeValue<EntityReference>("cr2c9_employeename")?.Id != employeeId)
                throw new Exception("Approval target is not the fictional test Employee.");
            return new Entity(Request,id) {
                ["cr2c9_status"] = new OptionSetValue(1),
                ["cr2c9_decisionexpectedversion"] = request.RowVersion,
                ["cr2c9_decisioncomments"] = "SYNTHETIC earned-cap integration test; HR email excluded."
            };
        }
        var above = requests["above-earned"];
        var before = Client.Retrieve(Request,above,new ColumnSet("cr2c9_status"));
        if (before.GetAttributeValue<OptionSetValue>("cr2c9_status")?.Value != 0)
            throw new Exception("Above-earned request is no longer pending.");
        Reject("88-hour approval above 84-hour earned balance",()=>Client.Update(Decision(above)),"Insufficient annual leave balance");
        var after = Client.Retrieve(Request,above,new ColumnSet("cr2c9_status"));
        if (after.GetAttributeValue<OptionSetValue>("cr2c9_status")?.Value != 0 || after.RowVersion != before.RowVersion ||
            Find(History,"cr2c9_leaverequest",above).Length != 0 ||
            Find(BalanceEntryTable,"cr2c9_leaverequest",above).Length != 0)
            throw new Exception("Denied approval changed request, history or ledger.");
        var within = requests["within-earned"];
        var current = Client.Retrieve(Request,within,new ColumnSet("cr2c9_status"));
        if (current.GetAttributeValue<OptionSetValue>("cr2c9_status")?.Value == 0)
            Client.Update(Decision(within));
        var saved = Client.Retrieve(Request,within,new ColumnSet("cr2c9_status"));
        var debits = Find(BalanceEntryTable,"cr2c9_leaverequest",within,"cr2c9_hours");
        if (saved.GetAttributeValue<OptionSetValue>("cr2c9_status")?.Value != 1 ||
            debits.Length != 1 || debits[0].GetAttributeValue<int>("cr2c9_hours") != -8 ||
            Find(History,"cr2c9_leaverequest",within).Length != 1)
            throw new Exception("Within-earned approval did not commit exactly one 8-hour debit and decision.");
        Console.WriteLine("PASS live earned-cap approval: 88h denied with no changes; 8h approved once with -8h debit; fictional HR email excluded; employee="+employeeId);
    }
}
