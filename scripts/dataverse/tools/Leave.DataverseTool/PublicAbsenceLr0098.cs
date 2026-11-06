using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

internal static partial class Development
{
    static void PublicAbsenceLr0098(bool backfill)
    {
        var requests = Find(Request, "cr2c9_leaverequestnumber", "LR-0098",
            "cr2c9_leaverequestnumber", "cr2c9_status", "cr2c9_startdate",
            "cr2c9_enddate", "cr2c9_hoursperday", "cr2c9_employeename");
        if (requests.Length != 1) throw new Exception("Expected one LR-0098 record; found " + requests.Length);
        var request = requests[0];
        var employeeRef = request.GetAttributeValue<EntityReference>("cr2c9_employeename")
            ?? throw new Exception("LR-0098 has no linked Employee.");
        var employee = Client.Retrieve(Employee, employeeRef.Id,
            new ColumnSet("cr2c9_employeename", "cr2c9_email", "cr2c9_entrauserid"));
        var status = request.GetAttributeValue<OptionSetValue>("cr2c9_status")?.Value;
        var start = request.GetAttributeValue<DateTime?>("cr2c9_startdate");
        var end = request.GetAttributeValue<DateTime?>("cr2c9_enddate");
        var rawHours = request.GetAttributeValue<object>("cr2c9_hoursperday");
        var hours = rawHours is OptionSetValue option ? option.Value : Convert.ToInt32(rawHours);
        var projectionRows = Find(PublicAbsenceTable, "cr2c9_publicabsenceid", request.Id,
            "cr2c9_name", "cr2c9_startdate", "cr2c9_enddate", "cr2c9_hoursperday");
        if (projectionRows.Length > 1) throw new Exception("Duplicate LR-0098 public absence rows.");
        var projection = projectionRows.SingleOrDefault();
        Console.WriteLine("LR-0098 id=" + request.Id + " status=" + status +
            " employee=" + employee.GetAttributeValue<string>("cr2c9_employeename") +
            " dates=" + start?.ToString("yyyy-MM-dd") + ".." + end?.ToString("yyyy-MM-dd") +
            " hours/day=" + hours + " projection=" + (projection == null ? "missing" : "present"));
        if (!backfill) return;
        if (status != 1 ||
            employee.GetAttributeValue<string>("cr2c9_employeename") != "Darwisyah Fakhira" ||
            start?.Date != new DateTime(2026, 10, 22) ||
            end?.Date != new DateTime(2026, 10, 23) ||
            (hours != 4 && hours != 8))
            throw new Exception("LR-0098 no longer matches the verified approved Darwisyah leave; no backfill made.");
        if (projection != null) {
            if (projection.GetAttributeValue<string>("cr2c9_name") != "Darwisyah Fakhira" ||
                projection.GetAttributeValue<DateTime>("cr2c9_startdate").Date != start.Value.Date ||
                projection.GetAttributeValue<DateTime>("cr2c9_enddate").Date != end.Value.Date ||
                projection.GetAttributeValue<int>("cr2c9_hoursperday") != hours)
                throw new Exception("Existing public absence differs from LR-0098; no overwrite made.");
            Console.WriteLine("LR-0098 public absence already matches; no change needed.");
            return;
        }
        Client.Create(new Entity(PublicAbsenceTable, request.Id) {
            ["cr2c9_name"] = "Darwisyah Fakhira",
            ["cr2c9_startdate"] = start.Value.Date,
            ["cr2c9_enddate"] = end.Value.Date,
            ["cr2c9_hoursperday"] = hours
        });
        var saved = Client.Retrieve(PublicAbsenceTable, request.Id,
            new ColumnSet("cr2c9_name", "cr2c9_startdate", "cr2c9_enddate", "cr2c9_hoursperday"));
        if (saved.GetAttributeValue<string>("cr2c9_name") != "Darwisyah Fakhira" ||
            saved.GetAttributeValue<DateTime>("cr2c9_startdate").Date != start.Value.Date ||
            saved.GetAttributeValue<DateTime>("cr2c9_enddate").Date != end.Value.Date ||
            saved.GetAttributeValue<int>("cr2c9_hoursperday") != hours)
            throw new Exception("LR-0098 public absence did not verify after creation.");
        Console.WriteLine("Backfilled and verified LR-0098 in the public absence calendar.");
    }
}
