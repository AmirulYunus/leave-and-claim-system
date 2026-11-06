using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

internal static partial class Development
{
    static Entity CheckedAnnualGrantFlow()
    {
        var flows = Find("workflow", "name", "Grant calendar annual leave balances",
            "name", "statecode", "category");
        if (flows.Length != 1)
            throw new Exception("Expected exactly one daily annual-grant flow; found " + flows.Length + ". No state changed.");
        var flow = flows[0];
        return flow;
    }

    static void AnnualAccrualFlowInspect()
    {
        var flow = CheckedAnnualGrantFlow();
        Console.WriteLine("Developer annual-grant flow " + flow.Id +
            "; state=" + flow.GetAttributeValue<OptionSetValue>("statecode")?.Value +
            "; category=" + flow.GetAttributeValue<OptionSetValue>("category")?.Value +
            ". Inspect deployed grant behavior separately.");
    }

    static void AnnualAccrualPauseFlow()
    {
        var flow = CheckedAnnualGrantFlow();
        var state = flow.GetAttributeValue<OptionSetValue>("statecode")?.Value;
        if (state == 0) { Console.WriteLine("Daily annual-grant flow is already off."); return; }
        if (state != 1) throw new Exception("Unexpected flow state; no change made.");
        Client.Update(new Entity("workflow", flow.Id) {
            ["statecode"] = new OptionSetValue(0)
        });
        var after = CheckedAnnualGrantFlow();
        if (after.GetAttributeValue<OptionSetValue>("statecode")?.Value != 0)
            throw new Exception("Daily annual-grant flow did not verify as off.");
        Console.WriteLine("Verified Developer daily annual-grant flow is off. No balance entry or grant action was changed.");
    }

    static void AnnualAccrualResumeFlow()
    {
        var flow = CheckedAnnualGrantFlow();
        if (flow.GetAttributeValue<OptionSetValue>("category")?.Value != 5 ||
            flow.GetAttributeValue<OptionSetValue>("statecode")?.Value != 0)
            throw new Exception("Expected the paused cloud flow; no state changed.");
        var definition = Client.Retrieve("workflow",flow.Id,new ColumnSet("clientdata"))
            .GetAttributeValue<string>("clientdata") ?? "";
        if (!definition.Contains("cr2c9_GrantCalendarAnnualLeave",StringComparison.Ordinal) ||
            !definition.Contains("cr2c9_employee",StringComparison.Ordinal))
            throw new Exception("Daily flow no longer targets the secured Employee annual-grant action.");
        Client.Update(new Entity("workflow",flow.Id) {
            ["statecode"] = new OptionSetValue(1)
        });
        var after = CheckedAnnualGrantFlow();
        if (after.GetAttributeValue<OptionSetValue>("statecode")?.Value != 1)
            throw new Exception("Daily annual-grant flow did not verify as on.");
        Console.WriteLine("Verified Developer daily grant flow is On and calls the secured earned-leave API. No manual run was started.");
    }
}
