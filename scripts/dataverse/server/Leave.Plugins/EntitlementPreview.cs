using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Leave.Plugins
{
    // Rates only. This code has no balance writes, accrual, deductions or grants.
    public static class EntitlementCalculator
    {
        public const int FullTime = 290040000;
        public const int PartTime = 290040001;
        public const int Intern = 290040002;
        public const int FirstLevel = 290040000;
        public static DateTime MalaysiaDate(DateTime utc)
        {
            if (utc.Kind != DateTimeKind.Utc) throw new ArgumentException("UTC clock required.");
            return utc.AddHours(8).Date;
        }

        public static EntitlementPreview Calculate(DateTime? joined, int? level, int? employmentType, DateTime asOf)
        {
            if (asOf.TimeOfDay != TimeSpan.Zero) throw new ArgumentException("Assessment must be a calendar date.");
            var result = new EntitlementPreview {
                AssessmentDate = asOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Scope = "Full-year rates only; not a granted entitlement or available balance. Annual balance periods are January to December; grants, proration and carry-forward are separate."
            };
            var missing = new List<string>();
            if (!joined.HasValue) missing.Add("Date Joined");
            if (!employmentType.HasValue) missing.Add("Employment Type");
            var annualMissing = new List<string>(missing);
            if (!level.HasValue) annualMissing.Add("Employee Level");
            result.Annual = Pending("PendingHrDetails", "Pending HR details: " + string.Join(", ", annualMissing));
            result.Sick = Pending("PendingHrDetails", "Pending HR details: " + string.Join(", ", missing));
            result.Hospitalisation = Pending("PendingHrDetails", "Pending HR details: " + string.Join(", ", missing));

            if (joined.HasValue && (joined.Value.TimeOfDay != TimeSpan.Zero || joined.Value.Date > asOf))
            {
                result.Annual = result.Sick = result.Hospitalisation = Pending("InvalidDetails", "HR must correct Date Joined; it must be a calendar date on or before the assessment date.");
                return result;
            }
            if (employmentType.HasValue && employmentType.Value != FullTime && employmentType.Value != PartTime && employmentType.Value != Intern)
            {
                result.Annual = result.Sick = result.Hospitalisation = Pending("InvalidDetails", "HR must select a supported Employment Type.");
                return result;
            }
            if (missing.Count != 0) return result;
            if (employmentType != FullTime)
            {
                result.Sick = result.Hospitalisation = Pending("PendingPolicy", "Part-time/intern paid-leave rules require policy and statutory review; no zero entitlement is assigned.");
                if (annualMissing.Count == 0) result.Annual = Pending("PendingPolicy", "Part-time/intern annual-leave rules require policy and statutory review.");
                return result;
            }
            // A non-leap-year anniversary for 29 February requires a company decision.
            if (joined.Value.Month == 2 && joined.Value.Day == 29)
            {
                result.Annual = result.Sick = result.Hospitalisation = Pending("PendingPolicy", "29 February service-anniversary handling requires confirmation.");
                return result;
            }
            var years = asOf.Year - joined.Value.Year;
            if (asOf.Month < joined.Value.Month || (asOf.Month == joined.Value.Month && asOf.Day < joined.Value.Day)) years--;
            result.CompletedYears = years;
            result.Sick = Calculated(years < 2 ? 14 : years < 5 ? 18 : 22);
            result.Hospitalisation = Calculated(60);
            if (annualMissing.Count != 0) return result;
            if (level.Value < FirstLevel || level.Value > FirstLevel + 3)
            {
                result.Annual = Pending("InvalidDetails", "HR must select a supported Employee Level.");
                return result;
            }
            if (years >= 8)
            {
                result.Annual = Pending("PendingPolicy", "Annual service reward from eight completed years onward is on hold.");
                return result;
            }
            var bases = new[] { 14, 14, 15, 18 };
            var baseDays = bases[level.Value - FirstLevel];
            var reward = years < 2 ? 0 : years < 4 ? 1 : years < 6 ? 2 : 3;
            var floor = years < 2 ? 8 : years < 5 ? 12 : 16;
            result.Annual = Calculated(Math.Max(baseDays + reward, floor));
            result.Annual.BaseDays = baseDays;
            result.Annual.RewardDays = reward;
            result.Annual.StatutoryMinimumDays = floor;
            result.Annual.Hours = result.Annual.Days.Value * 8;
            return result;
        }
        static EntitlementRate Pending(string status, string reason) => new EntitlementRate { Status = status, Reason = reason };
        static EntitlementRate Calculated(int days) => new EntitlementRate { Status = "Calculated", Reason = "Full-year rate", Days = days, Hours = checked(days * 8) };
    }

    [DataContract]
    public sealed class EntitlementRate
    {
        [DataMember] public string Status { get; set; }
        [DataMember] public string Reason { get; set; }
        [DataMember] public int? Days { get; set; }
        [DataMember] public int? Hours { get; set; }
        [DataMember] public int? BaseDays { get; set; }
        [DataMember] public int? RewardDays { get; set; }
        [DataMember] public int? StatutoryMinimumDays { get; set; }
    }
    [DataContract]
    public sealed class EntitlementPreview
    {
        [DataMember] public string AssessmentDate { get; set; }
        [DataMember] public string Scope { get; set; }
        [DataMember] public int? CompletedYears { get; set; }
        [DataMember] public EntitlementRate Annual { get; set; }
        [DataMember] public EntitlementRate Sick { get; set; }
        [DataMember] public EntitlementRate Hospitalisation { get; set; }
        public string ToJson()
        {
            using (var stream = new MemoryStream()) {
                new DataContractJsonSerializer(typeof(EntitlementPreview)).WriteObject(stream, this);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }
    }

    public sealed class EmployeeEntitlementPreview : IPlugin
    {
        public const string ApiName = "cr2c9_GetEntitlementPreview";
        public void Execute(IServiceProvider provider)
        {
            var context = (IPluginExecutionContext)provider.GetService(typeof(IPluginExecutionContext));
            if (context.MessageName != ApiName || context.InitiatingUserId == Guid.Empty ||
                !context.InputParameters.Contains("Target") || !(context.InputParameters["Target"] is EntityReference target) ||
                target.LogicalName != "cr2c9_employee" || target.Id == Guid.Empty)
                throw new InvalidPluginExecutionException("A saved Employee record is required.");
            var factory = (IOrganizationServiceFactory)provider.GetService(typeof(IOrganizationServiceFactory));
            // Enforce the requesting user's record/field read access. Never elevate.
            var service = factory.CreateOrganizationService(context.InitiatingUserId);
            var employee = service.Retrieve(target.LogicalName, target.Id,
                new ColumnSet("cr2c9_datejoined", "cr2c9_employeelevel", "cr2c9_employmenttype"));
            var joined = employee.GetAttributeValue<DateTime?>("cr2c9_datejoined");
            var result = EntitlementCalculator.Calculate(joined,
                employee.GetAttributeValue<OptionSetValue>("cr2c9_employeelevel")?.Value,
                employee.GetAttributeValue<OptionSetValue>("cr2c9_employmenttype")?.Value,
                EntitlementCalculator.MalaysiaDate(DateTime.UtcNow));
            context.OutputParameters["PreviewJson"] = result.ToJson();
        }
    }
}
