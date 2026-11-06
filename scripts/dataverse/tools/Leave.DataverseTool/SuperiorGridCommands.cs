using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

internal static partial class Development
{
    static void SuperiorGridScriptDeploy()
    {
        const string name = "cr2c9_leaveRequestGrid.js";
        var content = Convert.ToBase64String(File.ReadAllBytes(Root + "/" + name));
        var existing = Find("webresource", "name", name, "content");
        if (existing.Length > 1) throw new Exception("Duplicate grid command script.");
        Guid id;
        if (existing.Length == 0)
            id = CreateInSolution(new Entity("webresource") {
                ["name"] = name,
                ["displayname"] = "Superior leave request grid decisions",
                ["webresourcetype"] = new OptionSetValue(3),
                ["content"] = content
            });
        else {
            id = existing[0].Id;
            if (existing[0].GetAttributeValue<string>("content") != content)
                Client.Update(new Entity("webresource", id) { ["content"] = content });
        }
        Add(id, 61);
        Client.Execute(new PublishXmlRequest { ParameterXml =
            "<importexportxml><webresources><webresource>" + id +
            "</webresource></webresources></importexportxml>" });
        Console.WriteLine("Published " + name + " as " + id);
    }

    static void MaternityErrorTrace()
    {
        var q = new QueryExpression("plugintracelog") {
            ColumnSet = new ColumnSet("createdon", "typename", "messageblock", "exceptiondetails", "correlationid"),
            TopCount = 30
        };
        q.Criteria.AddCondition("createdon", ConditionOperator.OnOrAfter, new DateTime(2026, 10, 19, 2, 10, 0, DateTimeKind.Utc));
        q.Criteria.AddCondition("createdon", ConditionOperator.OnOrBefore, new DateTime(2026, 10, 19, 2, 28, 0, DateTimeKind.Utc));
        q.Orders.Add(new OrderExpression("createdon", OrderType.Ascending));
        foreach (var row in Client.RetrieveMultiple(q).Entities)
            Console.WriteLine($"TRACE {row.GetAttributeValue<DateTime>("createdon"):O} {row.GetAttributeValue<string>("typename")} {row.GetAttributeValue<Guid>("correlationid")}: {row.GetAttributeValue<string>("messageblock")} {row.GetAttributeValue<string>("exceptiondetails")}");
    }
}
