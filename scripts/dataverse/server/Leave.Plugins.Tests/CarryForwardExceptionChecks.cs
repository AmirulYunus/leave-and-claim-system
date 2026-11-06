using System;
using Leave.Plugins;

static class CarryForwardExceptionChecks
{
    public static void Run()
    {
        var superior = Guid.NewGuid();
        CarryForwardExceptionPolicy.Validate(2026, new DateTime(2026, 12, 15),
            44, "HR/2026/CF-approval-1", superior);
        Reject(() => CarryForwardExceptionPolicy.Validate(2026,
            new DateTime(2026, 12, 16), 44, "HR/2026/CF-approval-1", superior));
        Reject(() => CarryForwardExceptionPolicy.Validate(2026,
            new DateTime(2026, 9, 30), 44, "HR/2026/CF-approval-1", superior));
        Reject(() => CarryForwardExceptionPolicy.Validate(2026,
            new DateTime(2026, 11, 15), 40, "HR/2026/CF-approval-1", superior));
        Reject(() => CarryForwardExceptionPolicy.Validate(2026,
            new DateTime(2026, 11, 15), 45, "HR/2026/CF-approval-1", superior));
        Reject(() => CarryForwardExceptionPolicy.Validate(2026,
            new DateTime(2026, 11, 15), 44, "", superior));
        Reject(() => CarryForwardExceptionPolicy.Validate(2026,
            new DateTime(2026, 11, 15), 44, "HR/2026/CF-approval-1", Guid.Empty));
        var withException = CarryForwardDraft.Calculate(80, 64, true);
        if (withException.CarryForwardHours != 64 || withException.SeparatelyRecordedUnusedHours != 16)
            throw new Exception("Written exception was not applied to the year-end draft.");
        Console.WriteLine("PASS written carry-forward exception deadline, evidence, half-days and limit.");
    }

    static void Reject(Action action)
    {
        try { action(); }
        catch (ArgumentException) { return; }
        catch (InvalidOperationException) { return; }
        throw new Exception("Invalid written carry-forward exception was accepted.");
    }
}
