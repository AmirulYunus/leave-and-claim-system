using Leave.Plugins;

static class EntitlementChecks
{
    public static void Run()
    {
        var checks = 0;
        void Check(string name, Action check) { check(); checks++; Console.WriteLine("PASS entitlement: " + name); }
        void Eq(object actual, object expected) { if (!Equals(actual, expected)) throw new Exception($"Expected {expected}; got {actual}"); }
        var assessment = new DateTime(2026, 10, 9);
        EntitlementPreview Calc(DateTime? joined, int? level = 290040000, int? type = 290040000) => EntitlementCalculator.Calculate(joined, level, type, assessment);
        foreach (var row in new[] { (0,14), (1,14), (2,15), (3,18) })
            Check("documented level " + row.Item1, () => Eq(Calc(new DateTime(2025,10,9),290040000+row.Item1).Annual.Days,row.Item2));
        foreach (var row in new[] { (1,14), (2,15), (3,15), (4,16), (5,16), (6,17), (7,17) })
            Check("completed service " + row.Item1, () => Eq(Calc(new DateTime(2026-row.Item1,10,9)).Annual.Days,row.Item2));
        Check("anniversary increase starts on the day", () => { var r=Calc(new DateTime(2024,10,10)); Eq(r.CompletedYears,1); Eq(r.Annual.Days,14); r=Calc(new DateTime(2024,10,9)); Eq(r.CompletedYears,2); Eq(r.Annual.Days,15); });
        Check("fifth-year statutory minimum", () => Eq(Calc(new DateTime(2021,10,9)).Annual.StatutoryMinimumDays,16));
        Check("eighth year remains held while medical is calculable", () => { var r=Calc(new DateTime(2018,10,9)); Eq(r.Annual.Status,"PendingPolicy"); Eq(r.Annual.Days,null); Eq(r.Sick.Days,22); });
        Check("missing joining date gives null, never zero", () => { var r=Calc(null); Eq(r.Annual.Status,"PendingHrDetails"); Eq(r.Annual.Days,null); Eq(r.Sick.Days,null); Eq(r.Hospitalisation.Days,null); });
        Check("missing type skips all three", () => { var r=Calc(new DateTime(2024,10,9),type:null); Eq(r.Annual.Days,null); Eq(r.Sick.Days,null); Eq(r.Hospitalisation.Days,null); });
        Check("missing level skips annual only", () => { var r=Calc(new DateTime(2024,10,9),null); Eq(r.Annual.Days,null); Eq(r.Sick.Days,18); Eq(r.Hospitalisation.Days,60); });
        Check("all missing inputs are listed", () => { var r=Calc(null,null,null); if (!r.Annual.Reason.Contains("Date Joined") || !r.Annual.Reason.Contains("Employee Level") || !r.Annual.Reason.Contains("Employment Type")) throw new Exception("Missing inputs not listed"); });
        foreach (var type in new[]{290040001,290040002}) Check("non-full-time type " + type + " never becomes zero", () => { var r=Calc(new DateTime(2020,1,1),type:type); Eq(r.Annual.Days,null); Eq(r.Sick.Status,"PendingPolicy"); Eq(r.Hospitalisation.Days,null); });
        Check("invalid level cannot fall back", () => { var r=Calc(new DateTime(2024,10,9),42); Eq(r.Annual.Status,"InvalidDetails"); Eq(r.Annual.Days,null); });
        Check("invalid employment type cannot fall back", () => Eq(Calc(new DateTime(2024,10,9),type:42).Sick.Status,"InvalidDetails"));
        Check("future joining date is flagged", () => { var r=Calc(new DateTime(2026,10,10)); Eq(r.Annual.Status,"InvalidDetails"); Eq(r.Annual.Days,null); });
        Check("time-bearing joining date is flagged", () => Eq(Calc(new DateTime(2024,10,9,8,0,0)).Annual.Status,"InvalidDetails"));
        Check("leap-day anniversary is held", () => Eq(Calc(new DateTime(2024,2,29)).Annual.Status,"PendingPolicy"));
        Check("same-day hire has zero completed years, full-year rate only", () => { var r=Calc(assessment); Eq(r.CompletedYears,0); Eq(r.Annual.Days,14); if (!r.Scope.Contains("not a granted entitlement")) throw new Exception("Rate scope absent"); });
        Check("medical thresholds are independent of annual rewards", () => { Eq(Calc(new DateTime(2024,10,10)).Sick.Days,14); Eq(Calc(new DateTime(2024,10,9)).Sick.Days,18); Eq(Calc(new DateTime(2021,10,10)).Sick.Days,18); Eq(Calc(new DateTime(2021,10,9)).Sick.Days,22); });
        Check("hospitalisation is separate sixty", () => Eq(Calc(new DateTime(2024,10,9)).Hospitalisation.Days,60));
        Check("company hospitalisation figure includes Medical Leave used", () => {
            var balance=MedicalHospitalBalance.Calculate(14*8,14*8,0);
            Eq(balance.CompanyCombinedRemainingHours,46*8);
            Eq(balance.StatutoryHospitalisationRemainingHours,60*8);
        });
        Check("company figure cannot remove separate statutory hospitalisation days", () => {
            var balance=MedicalHospitalBalance.Calculate(14*8,14*8,46*8);
            Eq(balance.CompanyCombinedRemainingHours,0);
            Eq(balance.StatutoryHospitalisationRemainingHours,14*8);
        });
        Check("combined figure and statutory hospitalisation recover after reversals", () => {
            var balance=MedicalHospitalBalance.Calculate(14*8,8*8,4*8);
            Eq(balance.CompanyCombinedRemainingHours,48*8);
            Eq(balance.StatutoryHospitalisationRemainingHours,56*8);
        });
        Check("combined figure rejects unverified or overdrawn use", () => {
            foreach(var pair in new[]{(-4,0),(14*8+4,0),(0,60*8+4),(2,0)}) {
                var rejected=false;
                try { MedicalHospitalBalance.Calculate(14*8,pair.Item1,pair.Item2); }
                catch(ArgumentException) { rejected=true; }
                if(!rejected)throw new Exception("Invalid Medical or Hospitalisation usage was accepted.");
            }
        });
        Check("annual hours retain eight-hour standard", () => Eq(Calc(new DateTime(2024,10,9)).Annual.Hours,120));
        Check("Malaysia date rolls over at UTC sixteen", () => { Eq(EntitlementCalculator.MalaysiaDate(new DateTime(2026,10,8,15,59,0,DateTimeKind.Utc)),new DateTime(2026,10,8)); Eq(EntitlementCalculator.MalaysiaDate(new DateTime(2026,10,8,16,0,0,DateTimeKind.Utc)),assessment); });
        Check("JSON serializes missing rates as null", () => { var json=Calc(null).ToJson(); if (!json.Contains("\"Days\":null")) throw new Exception("Missing rate was not null"); });
        Console.WriteLine($"{checks} entitlement checks passed.");
        EntitlementApiChecks.Run();
    }
}
