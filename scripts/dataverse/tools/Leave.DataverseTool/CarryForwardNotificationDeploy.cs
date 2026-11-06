using System.Text.Json.Nodes;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

internal static partial class Development
{
    const string CarryForwardNoticeName = "Notify employee and HR after annual carry-forward confirmation";

    static void CarryForwardNotificationDeploy()
    {
        var source = Client.Retrieve("workflow", HrNotifyFlow,
            new ColumnSet("category", "statecode", "clientdata"));
        if (source.GetAttributeValue<OptionSetValue>("category")?.Value != 5 ||
            source.GetAttributeValue<OptionSetValue>("statecode")?.Value != 1)
            throw new Exception("The active leave decision mail flow is required as a connection template.");
        var flow = JsonNode.Parse(source.GetAttributeValue<string>("clientdata") ?? "")!;
        var definition = flow["properties"]?["definition"]
            ?? throw new Exception("The mail flow definition is missing.");
        var trigger = definition["triggers"]?["When_a_row_is_added,_modified_or_deleted"]
            ?? throw new Exception("The Dataverse row trigger is missing.");
        var mail = definition["actions"]?["Send_an_email_(V2)"]
            ?? throw new Exception("The Outlook mail action is missing.");
        if (mail["inputs"]?["host"]?["operationId"]?.ToString() != "SendEmailV2" ||
            flow["properties"]?["connectionReferences"]?["shared_office365"] == null)
            throw new Exception("The verified Outlook connection is unavailable.");
        trigger["inputs"]!["parameters"]!["subscriptionRequest/message"] = 1;
        trigger["inputs"]!["parameters"]!["subscriptionRequest/entityname"] = CarryForwardCreditTable;
        trigger["inputs"]!["parameters"]!.AsObject().Remove("subscriptionRequest/filteringattributes");
        trigger["inputs"]!["parameters"]!["subscriptionRequest/filterexpression"] = "cr2c9_hours gt 0";
        trigger.AsObject().Remove("conditions");
        var actions = definition["actions"]!.AsObject();
        actions.Clear();
        actions["Get_Employee"] = ListRows("cr2c9_employees",
            "@concat('cr2c9_employeeid eq ', triggerOutputs()?['body/_cr2c9_employee_value'])",
            "cr2c9_name,cr2c9_email,cr2c9_isactive");
        actions["Check_Employee"] = Branch(
            "@equals(length(body('Get_Employee')?['value']),1)",
            new JsonObject {
                ["Send_Employee_Carry_Forward_Email"] = Mail(mail,
                    "@{first(body('Get_Employee')?['value'])?['cr2c9_email']}",
                    "Your Annual Leave carry-forward is confirmed",
                    "HR confirmed your Annual Leave carry-forward. Credited: @{triggerOutputs()?['body/cr2c9_hours']} hours (8 hours = 1 day). " +
                    "Use by: @{triggerOutputs()?['body/cr2c9_useby']}. " +
                    "It is used before current-year Annual Leave. View your balance in <a href='" + EmployeeAppUrl + "'>Employee self service</a>."),
                ["Send_HR_Carry_Forward_Email"] = Mail(mail,
                    "@parameters('" + HrRecipients + "')",
                    "Annual Leave carry-forward confirmed",
                    "Carry-forward was confirmed for @{first(body('Get_Employee')?['value'])?['cr2c9_name']}. " +
                    "Credited: @{triggerOutputs()?['body/cr2c9_hours']} hours (8 hours = 1 day). " +
                    "Review the record in HR leave operations.")
            },
            new JsonObject { ["Missing_Employee"] = Failure("Carry-forward credit must have exactly one linked Employee.") });
        actions["Check_Employee"]!["runAfter"] = JsonNode.Parse("{\"Get_Employee\":[\"Succeeded\"]}");

        var existing = Find("workflow", "name", CarryForwardNoticeName,
            "category", "statecode", "clientdata");
        if (existing.Length > 1) throw new Exception("Duplicate carry-forward notification flows.");
        Guid id;
        if (existing.Length == 0) {
            id = Client.Create(new Entity("workflow") {
                ["name"] = CarryForwardNoticeName, ["category"] = new OptionSetValue(5),
                ["type"] = new OptionSetValue(1), ["primaryentity"] = "none",
                ["description"] = "One employee and HR email after a confirmed carry-forward credit is created; no medical or leave reasons.",
                ["clientdata"] = flow.ToJsonString()
            });
            Add(id, 29);
        } else {
            var saved = existing.Single(); id = saved.Id;
            if (saved.GetAttributeValue<OptionSetValue>("category")?.Value != 5 ||
                JsonNode.Parse(saved.GetAttributeValue<string>("clientdata") ?? "")?
                    ["properties"]?["definition"]?["actions"]?["Check_Employee"] == null)
                throw new Exception("Existing carry-forward notification flow differs.");
        }
        if (Client.Retrieve("workflow", id, new ColumnSet("statecode"))
                .GetAttributeValue<OptionSetValue>("statecode")?.Value != 1)
            Client.Update(new Entity("workflow", id) {
                ["statecode"] = new OptionSetValue(1), ["statuscode"] = new OptionSetValue(2)
            });
        var verified = Client.Retrieve("workflow", id, new ColumnSet("clientdata", "statecode"));
        if (verified.GetAttributeValue<OptionSetValue>("statecode")?.Value != 1 ||
            JsonNode.Parse(verified.GetAttributeValue<string>("clientdata") ?? "")?
                ["properties"]?["definition"]?["actions"]?["Check_Employee"] == null)
            throw new Exception("Carry-forward employee notification flow did not activate.");
        Console.WriteLine("Active employee and HR carry-forward credit notification flow: " + id + ". No message sent.");
    }
}
