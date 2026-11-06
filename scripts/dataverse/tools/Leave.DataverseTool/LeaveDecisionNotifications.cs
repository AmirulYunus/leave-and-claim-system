using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

internal static partial class Development
{
    const string CancellationNoticeName = "Notify HR and employee after leave cancellation decision";
    const string HrRecipients = "HR Notification Emails (cr2c9_HRNotificationEmails)";
    const string EmployeeAppUrl = "https://apps.powerapps.com/play/e/0482c6ce-0b4e-e985-a55d-d19616dc4891/a/a1f9f752-5a55-44d0-8027-8546b13743d9";

    static JsonNode Clone(JsonNode node) => JsonNode.Parse(node.ToJsonString())!;

    static JsonObject ListRows(string table, string filter, string? select = null)
    {
        var parameters = new JsonObject {
            ["entityName"] = table,
            ["$filter"] = filter,
            ["$top"] = 2
        };
        if (select != null) parameters["$select"] = select;
        return new JsonObject {
            ["type"] = "OpenApiConnection",
            ["runAfter"] = new JsonObject(),
            ["inputs"] = new JsonObject {
                ["host"] = new JsonObject {
                    ["apiId"] = "/providers/Microsoft.PowerApps/apis/shared_commondataserviceforapps",
                    ["operationId"] = "ListRecords",
                    ["connectionName"] = "shared_commondataserviceforapps"
                },
                ["parameters"] = parameters,
                ["authentication"] = "@parameters('$authentication')"
            }
        };
    }

    static JsonObject Mail(JsonNode existingMail, string recipient, string subject, string body)
    {
        var email = Clone(existingMail).AsObject();
        email["runAfter"] = new JsonObject();
        var p = email["inputs"]!["parameters"]!;
        p["emailMessage/To"] = recipient;
        p["emailMessage/Subject"] = subject;
        p["emailMessage/Body"] = "<p>" + body + "</p>";
        return email;
    }

    static JsonObject Branch(string expression, JsonObject onTrue, JsonObject? onFalse = null)
    {
        return new JsonObject {
            ["type"] = "If",
            ["expression"] = expression,
            ["actions"] = onTrue,
            ["else"] = new JsonObject { ["actions"] = onFalse ?? new JsonObject() },
            ["runAfter"] = new JsonObject()
        };
    }

    static JsonObject Failure(string message) => new() {
        ["type"] = "Terminate",
        ["inputs"] = new JsonObject {
            ["runStatus"] = "Failed",
            ["runError"] = new JsonObject {
                ["code"] = "LeaveNotificationRecipientMissing",
                ["message"] = message
            }
        }
    };

    static string BackupFlow(Entity flow)
    {
        var original = flow.GetAttributeValue<string>("clientdata")
            ?? throw new InvalidOperationException("Cloud flow has no definition.");
        var path = Path.Combine(Path.GetTempPath(), "leave-notification-before-" + flow.Id + "-" +
            DateTime.UtcNow.ToString("yyyyMMddHHmmss") + ".json");
        File.WriteAllText(path, original);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        Console.WriteLine("Backed up flow " + flow.Id + " at " + path);
        return original;
    }

    static void LeaveDecisionNotificationsDeploy()
    {
        var source = Client.Retrieve("workflow", HrNotifyFlow,
            new ColumnSet("name", "category", "statecode", "clientdata", "type", "primaryentity"));
        if (source.GetAttributeValue<OptionSetValue>("category")?.Value != 5 ||
            source.GetAttributeValue<OptionSetValue>("statecode")?.Value != 1)
            throw new InvalidOperationException("Expected the active HR notification cloud flow.");
        var existing = JsonNode.Parse(source.GetAttributeValue<string>("clientdata") ?? "")!;
        var definition = existing["properties"]?["definition"]
            ?? throw new InvalidOperationException("Flow definition missing.");
        var originalTrigger = definition["triggers"]?["When_a_row_is_added,_modified_or_deleted"]
            ?? throw new InvalidOperationException("Expected the decision webhook.");
        var originalMail = definition["actions"]?["Send_an_email_(V2)"]
            ?? throw new InvalidOperationException("Existing HR mail action missing.");
        if (originalTrigger["inputs"]?["parameters"]?["subscriptionRequest/entityname"]?.ToString() != Request ||
            originalMail["inputs"]?["host"]?["operationId"]?.ToString() != "SendEmailV2" ||
            originalMail["inputs"]?["parameters"]?["emailMessage/To"]?.ToString() !=
                "@parameters('HR Notification Emails (cr2c9_HRNotificationEmails)')")
            throw new InvalidOperationException("Notification flow contract changed; inspect it before deployment.");
        if (definition["actions"]!.AsObject().Count != 1)
            throw new InvalidOperationException("Notification flow has additional actions; inspect before deployment.");
        var references = existing["properties"]?["connectionReferences"];
        if (references?["shared_commondataserviceforapps"] == null || references["shared_office365"] == null)
            throw new InvalidOperationException("Required Dataverse and Outlook connection references are missing.");

        // The same guarded status transition handles every leave category. The
        // trigger runs after commit, so a failed approval sends no notice.
        var approval = Clone(existing);
        var approvalDef = approval["properties"]!["definition"]!;
        var approvalTrigger = approvalDef["triggers"]!["When_a_row_is_added,_modified_or_deleted"]!;
        approvalTrigger["inputs"]!["parameters"]!["subscriptionRequest/filterexpression"] =
            "(cr2c9_status eq 1 or cr2c9_status eq 3) and cr2c9_decisionbyentraid ne null and cr2c9_decisionon ne null";
        var approved = "equals(triggerOutputs()?['body/cr2c9_status'],1)";
        var decision = "@{if(" + approved + ",'approved','rejected')}";
        var approvalActions = approvalDef["actions"]!.AsObject();
        approvalActions["Send_an_email_(V2)"] = Mail(originalMail,
            "@parameters('" + HrRecipients + "')",
            "Leave request " + decision,
            "Leave request @{triggerOutputs()?['body/cr2c9_leaverequestnumber']} was " + decision +
            ".<br>Decision by: @{triggerOutputs()?['body/cr2c9_decisionbyname']}" +
            "<br>Decision on: @{triggerOutputs()?['body/cr2c9_decisionon']}" +
            "<br>Open the request in the HR or Superior app.");
        approvalActions["Get_Employee_for_Notice"] = ListRows("cr2c9_employees",
            "@concat('cr2c9_employeeid eq ', triggerOutputs()?['body/_cr2c9_employeename_value'])",
            "cr2c9_email,cr2c9_entrauserid,cr2c9_isactive");
        approvalActions["Check_Employee_for_Notice"] = Branch(
            "@equals(length(body('Get_Employee_for_Notice')?['value']),1)",
            new JsonObject {
                ["Send_Employee_Decision_Email"] = Mail(originalMail,
                    "@{first(body('Get_Employee_for_Notice')?['value'])?['cr2c9_email']}",
                    "Your leave request was " + decision,
                    "Your leave request @{triggerOutputs()?['body/cr2c9_leaverequestnumber']} was " + decision +
                    ".<br>Decision on: @{triggerOutputs()?['body/cr2c9_decisionon']}" +
                    "<br>View the request in <a href='" + EmployeeAppUrl + "'>Employee self service</a>.")
            },
            new JsonObject { ["Missing_Employee"] = Failure("Exactly one linked Employee is required for a leave decision email.") });
        approvalActions["Check_Employee_for_Notice"]!["runAfter"] =
            JsonNode.Parse("{\"Get_Employee_for_Notice\":[\"Succeeded\"]}");

        // Cancellation is a separate table and a separate final decision.
        var cancellation = Clone(existing);
        var cancelDef = cancellation["properties"]!["definition"]!;
        var cancelTrigger = cancelDef["triggers"]!["When_a_row_is_added,_modified_or_deleted"]!;
        cancelTrigger["inputs"]!["parameters"]!["subscriptionRequest/entityname"] = Cancellation;
        cancelTrigger["inputs"]!["parameters"]!["subscriptionRequest/filterexpression"] =
            "(cr2c9_status eq 1 or cr2c9_status eq 3) and cr2c9_decisionbyentraid ne null and cr2c9_decisionon ne null";
        cancelTrigger.AsObject().Remove("conditions");
        var cancellationActions = cancelDef["actions"]!.AsObject();
        cancellationActions.Clear();
        cancellationActions["Get_Cancelled_Request"] = ListRows("cr2c9_leaverequests",
            "@concat('cr2c9_leaverequestid eq ', triggerOutputs()?['body/_cr2c9_leaverequest_value'])");
        var cancelResult = "@{if(equals(triggerOutputs()?['body/cr2c9_status'],1),'approved','rejected')}";
        var fixtureIds = new[] {
            "6dd608e3-45c5-4b8b-93a2-5eec5758f62b",
            "a3a2b544-41de-4385-bacf-9859c45656c5",
            "8dbe5da0-aac7-f111-a05a-000d3a826b69"
        };
        var validEmployee = Branch(
            "@not(contains(createArray('" + string.Join("','", fixtureIds) +
                "'),string(first(body('Get_Cancelled_Request')?['value'])?['_cr2c9_employeename_value'])))",
            new JsonObject {
                ["Send_HR_Cancellation_Email"] = Mail(originalMail,
                    "@parameters('" + HrRecipients + "')",
                    "Leave cancellation " + cancelResult,
                    "Cancellation of leave request @{first(body('Get_Cancelled_Request')?['value'])?['cr2c9_leaverequestnumber']} was " +
                    cancelResult + ".<br>Decision by: @{triggerOutputs()?['body/cr2c9_decisionbyname']}" +
                    "<br>Decision on: @{triggerOutputs()?['body/cr2c9_decisionon']}."),
                ["Get_Cancellation_Employee"] = ListRows("cr2c9_employees",
                    "@concat('cr2c9_employeeid eq ', first(body('Get_Cancelled_Request')?['value'])?['_cr2c9_employeename_value'])",
                    "cr2c9_email,cr2c9_entrauserid,cr2c9_isactive")
            });
        var validActions = validEmployee["actions"]!.AsObject();
        validActions["Check_Cancellation_Employee"] = Branch(
            "@equals(length(body('Get_Cancellation_Employee')?['value']),1)",
            new JsonObject {
                ["Send_Employee_Cancellation_Email"] = Mail(originalMail,
                    "@{first(body('Get_Cancellation_Employee')?['value'])?['cr2c9_email']}",
                    "Your leave cancellation was " + cancelResult,
                    "Your request to cancel leave request @{first(body('Get_Cancelled_Request')?['value'])?['cr2c9_leaverequestnumber']} was " +
                    cancelResult + ".<br>@{if(equals(triggerOutputs()?['body/cr2c9_status'],1),'The original leave is cancelled.','The original leave remains approved.')}" +
                    "<br>View the request in <a href='" + EmployeeAppUrl + "'>Employee self service</a>.")
            },
            new JsonObject { ["Missing_Employee"] = Failure("Exactly one linked Employee is required for a cancellation email.") });
        validActions["Check_Cancellation_Employee"]!["runAfter"] =
            JsonNode.Parse("{\"Get_Cancellation_Employee\":[\"Succeeded\"]}");
        cancellationActions["Check_Cancelled_Request"] = Branch(
            "@equals(length(body('Get_Cancelled_Request')?['value']),1)",
            new JsonObject { ["Skip_Fictional_Employees"] = validEmployee },
            new JsonObject { ["Missing_Request"] = Failure("Exactly one original request is required for a cancellation email.") });
        cancellationActions["Check_Cancelled_Request"]!["runAfter"] =
            JsonNode.Parse("{\"Get_Cancelled_Request\":[\"Succeeded\"]}");

        var existingCancellation = Find("workflow", "name", CancellationNoticeName,
            "name", "category", "statecode", "clientdata");
        if (existingCancellation.Length > 1)
            throw new InvalidOperationException("Duplicate cancellation notification flows.");
        // Persist the new cancellation flow first. If its activation fails,
        // the existing approval notification is left untouched. A retry can
        // activate the same verified draft instead of creating a duplicate.
        Guid cancelId;
        if (existingCancellation.Length == 0) {
            cancelId = Client.Create(new Entity("workflow") {
                ["name"] = CancellationNoticeName,
                ["category"] = new OptionSetValue(5),
                ["type"] = new OptionSetValue(1),
                ["primaryentity"] = "none",
                ["description"] = "Notify HR and the linked employee once an authorized leave cancellation is approved or rejected.",
                ["clientdata"] = cancellation.ToJsonString()
            });
            Add(cancelId, 29);
        } else {
            var prior = existingCancellation.Single();
            if (prior.GetAttributeValue<OptionSetValue>("category")?.Value != 5 ||
                JsonNode.Parse(prior.GetAttributeValue<string>("clientdata") ?? "")?
                    ["properties"]?["definition"]?["actions"]?["Check_Cancelled_Request"] == null)
                throw new InvalidOperationException("Existing cancellation notification flow differs.");
            cancelId = prior.Id;
        }
        if (Client.Retrieve("workflow", cancelId, new ColumnSet("statecode"))
                .GetAttributeValue<OptionSetValue>("statecode")?.Value != 1)
            Client.Update(new Entity("workflow", cancelId) {
                ["statecode"] = new OptionSetValue(1), ["statuscode"] = new OptionSetValue(2)
            });
        var cancelSaved = Client.Retrieve("workflow", cancelId, new ColumnSet("clientdata", "statecode"));
        if (cancelSaved.GetAttributeValue<OptionSetValue>("statecode")?.Value != 1 ||
            JsonNode.Parse(cancelSaved.GetAttributeValue<string>("clientdata") ?? "")?
                ["properties"]?["definition"]?["actions"]?["Check_Cancelled_Request"] == null)
            throw new InvalidOperationException("Cancellation notification flow was not activated as expected.");
        Console.WriteLine("Active cancellation notifications: " + cancelId);

        var original = BackupFlow(source);
        Client.Update(new Entity("workflow", source.Id) { ["clientdata"] = approval.ToJsonString(),
            ["name"] = "Notify HR and employee after leave decision" });
        var saved = Client.Retrieve("workflow", source.Id, new ColumnSet("name", "clientdata", "statecode"));
        if (saved.GetAttributeValue<OptionSetValue>("statecode")?.Value != 1)
            throw new InvalidOperationException("Approval notification flow is not active.");
        var actualActions = JsonNode.Parse(saved.GetAttributeValue<string>("clientdata") ?? "")?
            ["properties"]?["definition"]?["actions"];
        if (actualActions?["Check_Employee_for_Notice"]?["actions"]?["Send_Employee_Decision_Email"] == null)
            throw new InvalidOperationException("Employee approval email was not saved.");
        Console.WriteLine("Active leave decision notifications: " + source.Id +
            "; previous definition SHA-256 " + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(original))));
    }
}
