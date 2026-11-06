using System.Text.Json.Nodes;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

internal static partial class Development
{
    const string CarryForwardReminderName = "Remind employees about Annual Leave carry-forward in October and November";

    static void CarryForwardReminderDeploy()
    {
        var source = Client.Retrieve("workflow", HrNotifyFlow,
            new ColumnSet("category", "statecode", "clientdata"));
        if (source.GetAttributeValue<OptionSetValue>("category")?.Value != 5 ||
            source.GetAttributeValue<OptionSetValue>("statecode")?.Value != 1)
            throw new Exception("The active leave email flow is required as a connection template.");
        var flow = JsonNode.Parse(source.GetAttributeValue<string>("clientdata") ?? "")!;
        var definition = flow["properties"]?["definition"]
            ?? throw new Exception("The flow definition is missing.");
        var mail = definition["actions"]?["Send_an_email_(V2)"]
            ?? throw new Exception("The Outlook mail action is missing.");
        if (mail["inputs"]?["host"]?["operationId"]?.ToString() != "SendEmailV2" ||
            flow["properties"]?["connectionReferences"]?["shared_office365"] == null ||
            flow["properties"]?["connectionReferences"]?["shared_commondataserviceforapps"] == null)
            throw new Exception("The verified Dataverse and Outlook connections are required.");
        definition["triggers"] = new JsonObject {
            ["Recurrence"] = new JsonObject {
                ["type"] = "Recurrence",
                ["recurrence"] = new JsonObject {
                    ["frequency"] = "Day", ["interval"] = 1,
                    ["startTime"] = "2026-10-24T06:30:00",
                    ["timeZone"] = "Singapore Standard Time",
                    ["schedule"] = new JsonObject {
                        ["hours"] = new JsonArray(6), ["minutes"] = new JsonArray(30)
                    }
                }
            }
        };
        var actions = definition["actions"]!.AsObject();
        actions.Clear();
        var employees = ListRows("cr2c9_employees",
            "statecode eq 0 and cr2c9_isactive eq true and cr2c9_email ne null",
            "cr2c9_employeeid,cr2c9_name,cr2c9_email");
        employees["inputs"]!["parameters"]!.AsObject().Remove("$top");
        employees["inputs"]!["runtimeConfiguration"] = new JsonObject {
            ["paginationPolicy"] = new JsonObject { ["minimumItemCount"] = 5000 }
        };
        var fixtureIds = new[] {
            "6dd608e3-45c5-4b8b-93a2-5eec5758f62b",
            "a3a2b544-41de-4385-bacf-9859c45656c5",
            "8dbe5da0-aac7-f111-a05a-000d3a826b69"
        };
        var onDate = new JsonObject {
            ["List_Active_Employees"] = employees,
            ["Filter_Real_Employees"] = new JsonObject {
                ["type"] = "Query",
                ["inputs"] = new JsonObject {
                    ["from"] = "@body('List_Active_Employees')?['value']",
                    ["where"] = "@and(not(endsWith(toLower(item()?['cr2c9_email']),'@example.invalid')),not(contains(createArray('" +
                        string.Join("','", fixtureIds) + "'),item()?['cr2c9_employeeid'])))"
                },
                ["runAfter"] = JsonNode.Parse("{\"List_Active_Employees\":[\"Succeeded\"]}")
            },
            ["For_Each_Employee"] = new JsonObject {
                ["type"] = "Foreach",
                ["foreach"] = "@body('Filter_Real_Employees')",
                ["actions"] = new JsonObject {
                    ["Send_Carry_Forward_Reminder"] = Mail(mail,
                        "@{items('For_Each_Employee')?['cr2c9_email']}",
                        "Annual Leave carry-forward reminder",
                        "Please review your planned Annual Leave before 15 December. The standard carry-forward limit is five days. " +
                        "For a higher limit, obtain your immediate Superior's written approval and submit it to HR by 15 December. " +
                        "HR calculates the final unused amount after 31 December and confirms any separate carry-forward credit. " +
                        "View your leave in <a href='" + EmployeeAppUrl + "'>Employee self service</a>.")
                },
                ["runAfter"] = JsonNode.Parse("{\"Filter_Real_Employees\":[\"Succeeded\"]}")
            }
        };
        actions["Only_On_Policy_Reminder_Dates"] = Branch(
            "@or(equals(formatDateTime(convertTimeZone(utcNow(),'UTC','Singapore Standard Time'),'MM-dd'),'10-01'),equals(formatDateTime(convertTimeZone(utcNow(),'UTC','Singapore Standard Time'),'MM-dd'),'11-30'))",
            onDate);
        var existing = Find("workflow", "name", CarryForwardReminderName,
            "category", "statecode", "clientdata");
        if (existing.Length > 1) throw new Exception("Duplicate carry-forward reminder flows.");
        Guid id;
        if (existing.Length == 0) {
            id = Client.Create(new Entity("workflow") {
                ["name"] = CarryForwardReminderName, ["category"] = new OptionSetValue(5),
                ["type"] = new OptionSetValue(1), ["primaryentity"] = "none",
                ["description"] = "06:30 Malaysia time daily check; send only on 1 October and 30 November to active real employees.",
                ["clientdata"] = flow.ToJsonString()
            });
            Add(id, 29);
        } else {
            var saved = existing.Single(); id = saved.Id;
            if (saved.GetAttributeValue<OptionSetValue>("category")?.Value != 5 ||
                JsonNode.Parse(saved.GetAttributeValue<string>("clientdata") ?? "")?
                    ["properties"]?["definition"]?["actions"]?["Only_On_Policy_Reminder_Dates"] == null)
                throw new Exception("Existing carry-forward reminder flow differs.");
        }
        if (Client.Retrieve("workflow", id, new ColumnSet("statecode"))
                .GetAttributeValue<OptionSetValue>("statecode")?.Value != 1)
            Client.Update(new Entity("workflow", id) {
                ["statecode"] = new OptionSetValue(1), ["statuscode"] = new OptionSetValue(2)
            });
        var verified = Client.Retrieve("workflow", id, new ColumnSet("clientdata", "statecode"));
        if (verified.GetAttributeValue<OptionSetValue>("statecode")?.Value != 1 ||
            JsonNode.Parse(verified.GetAttributeValue<string>("clientdata") ?? "")?
                ["properties"]?["definition"]?["actions"]?["Only_On_Policy_Reminder_Dates"] == null)
            throw new Exception("Carry-forward reminder flow did not activate.");
        Console.WriteLine("Active 1 October and 30 November reminders: " + id + ". No message sent today.");
    }
}
