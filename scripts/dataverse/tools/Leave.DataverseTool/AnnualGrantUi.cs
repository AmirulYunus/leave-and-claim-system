using System.Xml.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Crm.Sdk.Messages;

internal static partial class Development {
    static void AnnualBalanceViewDeploy() {
        var names = new[] { "cr2c9_annualGrant.html", "cr2c9_employeeEntitlement.js" };
        var ids = new List<Guid>();
        foreach (var name in names) {
            var resource = Find("webresource", "name", name, "content").Single();
            var content = Convert.ToBase64String(File.ReadAllBytes(Root + "/" + name));
            if (resource.GetAttributeValue<string>("content") != content)
                Client.Update(new Entity("webresource", resource.Id) { ["content"] = content });
            ids.Add(resource.Id);
        }
        Client.Execute(new PublishXmlRequest { ParameterXml = "<importexportxml><webresources>" +
            string.Concat(ids.Select(id=>"<webresource>"+id+"</webresource>")) +
            "</webresources></importexportxml>" });
        Console.WriteLine("Published HR annual-balance summary and updated entitlement explanation. No grants or balance records changed.");
    }
    static void AnnualGrantUi() {
        Guid Resource(string name,string display,int type) {
            var row=new Entity("webresource"){["name"]=name,["displayname"]=display,["webresourcetype"]=new OptionSetValue(type),["content"]=Convert.ToBase64String(File.ReadAllBytes(Root+"/"+name))};
            var old=Find("webresource","name",name);Guid id;
            if(old.Length==0)id=CreateInSolution(row);else{row.Id=old.Single().Id;Client.Update(row);id=row.Id;}
            Add(id,61);return id;
        }
        var html=Resource("cr2c9_annualGrant.html","Annual leave balance grants",1);
        var script=Resource("cr2c9_annualGrantForm.js","Annual leave grant form control",3);
        var form=Client.Retrieve("systemform",EmployeeForm,new ColumnSet("formxml"));
        var xml=XDocument.Parse(form.GetAttributeValue<string>("formxml"));var root=xml.Root!;
        var libs=root.Element("formLibraries")??new XElement("formLibraries");if(libs.Parent==null)root.Add(libs);
        if(!libs.Elements("Library").Any(x=>(string?)x.Attribute("name")=="cr2c9_annualGrantForm.js"))libs.Add(new XElement("Library",new XAttribute("name","cr2c9_annualGrantForm.js"),new XAttribute("libraryUniqueId","{"+script+"}")));
        var onload=root.Element("events")!.Elements("event").Single(x=>(string?)x.Attribute("name")=="onload");
        var handlers=onload.Element("Handlers")!;
        if(!handlers.Elements("Handler").Any(x=>(string?)x.Attribute("functionName")=="AnnualGrantForm.onLoad"))handlers.Add(new XElement("Handler",new XAttribute("handlerUniqueId","{"+Guid.NewGuid()+"}"),new XAttribute("functionName","AnnualGrantForm.onLoad"),new XAttribute("libraryName","cr2c9_annualGrantForm.js"),new XAttribute("enabled","true"),new XAttribute("parameters",""),new XAttribute("passExecutionContext","true")));
        if(!xml.Descendants("control").Any(x=>(string?)x.Attribute("datafieldname")=="cr2c9_leavingdate")) {
            var source=xml.Descendants("control").Single(x=>(string?)x.Attribute("datafieldname")=="cr2c9_datejoined").Parent!;
            var cell=new XElement(source);cell.SetAttributeValue("id","{"+Guid.NewGuid()+"}");
            var control=cell.Element("control")!;control.SetAttributeValue("id","cr2c9_leavingdate");control.SetAttributeValue("datafieldname","cr2c9_leavingdate");control.SetAttributeValue("uniqueid","{"+Guid.NewGuid()+"}");
            foreach(var label in cell.Descendants("label"))label.SetAttributeValue("description","Leaving Date (if applicable)");
            source.Parent!.Parent!.Add(new XElement("row",cell));
        }
        if(!xml.Descendants("tab").Any(x=>(string?)x.Attribute("name")=="annual_balance_tab")) {
            var tab=XElement.Parse(@"<tab name='annual_balance_tab' showlabel='true' expanded='true' visible='true' locklevel='0'><labels><label description='Annual balance' languagecode='1033'/></labels><columns><column width='100%'><sections><section name='annual_balance_section' showlabel='false' showbar='false' columns='1' layout='varwidth' visible='true'><labels><label description='Annual leave balance' languagecode='1033'/></labels><rows><row><cell showlabel='false' rowspan='12' colspan='1'><labels><label description='Annual leave balance grants' languagecode='1033'/></labels><control id='WebResource_AnnualGrant' classid='{9FDF5F91-88B1-47F4-AD53-C11EFC01A01D}'><parameters><Url>cr2c9_annualGrant.html</Url><PassParameters>false</PassParameters><Security>false</Security><Scrolling>auto</Scrolling><Border>false</Border></parameters></control></cell></row></rows></section></sections></column></columns></tab>");
            foreach(var element in tab.DescendantsAndSelf().Where(x=>x.Name=="tab"||x.Name=="section"||x.Name=="cell"))element.SetAttributeValue("id","{"+Guid.NewGuid()+"}");
            tab.Descendants("control").Single().SetAttributeValue("uniqueid","{"+Guid.NewGuid()+"}");
            root.Element("tabs")!.Add(tab);
        }
        Client.Update(new Entity("systemform",EmployeeForm){["formxml"]=xml.ToString(SaveOptions.DisableFormatting)});
        Add(EmployeeForm,60);
        Client.Execute(new PublishXmlRequest{ParameterXml="<importexportxml><entities><entity>cr2c9_employee</entity></entities><webresources><webresource>"+html+"</webresource><webresource>"+script+"</webresource></webresources></importexportxml>"});
        Console.WriteLine("Published Employee Annual balance tab with administrative grant control and optional Leaving Date. HTML="+html+" JS="+script+". Existing rate preview handlers retained.");
    }
    static void AnnualGrantRegression() {
        var result=Grant(FixtureEmployee);
        if((string)result["Status"]!="AlreadyGranted" || (int)result["CreditedHours"]!=0 || (int)result["AvailableHours"]!=128)throw new Exception("Restored fixture must remain 128 hours with no repeat credit.");
        Console.WriteLine("PASS restored fictional fixture has 3 grant entries / 128 hours, repeated call credits zero.");
        var period=Find(BalancePeriodTable,"cr2c9_employee",FixtureEmployee).Single();
        var entry=Find(BalanceEntryTable,"cr2c9_balanceperiod",period.Id,"cr2c9_hours").First();
        foreach(var bulk in new[]{("CreateMultiple",new Entity(BalanceEntryTable){["cr2c9_balanceperiod"]=period.ToEntityReference(),["cr2c9_name"]="Rejected bulk probe",["cr2c9_operationkey"]="Rejected bulk probe",["cr2c9_hours"]=999,["cr2c9_entrykind"]=new OptionSetValue(290040000)}),
            ("UpdateMultiple",new Entity(BalanceEntryTable,entry.Id){["cr2c9_hours"]=999})})
            Reject("External ledger "+bulk.Item1,()=>Client.Execute(new OrganizationRequest(bulk.Item1){["Targets"]=new EntityCollection(new[]{bulk.Item2}){EntityName=BalanceEntryTable}}),"only by the authorized");
        var original=Client.Retrieve(Employee,FixtureEmployee,new ColumnSet("cr2c9_employeelevel"));
        Client.Update(new Entity(Employee,FixtureEmployee){["cr2c9_employeelevel"]=null});
        try {
            var missing=Grant(FixtureEmployee);
            if((string)missing["Status"]!="PendingHrDetails" || (int)missing["CreditedHours"]!=0 || (int)missing["AvailableHours"]!=128)throw new Exception("Missing HR must preserve 128 hours.");
            Console.WriteLine("PASS current fixture null HR skips and preserves all credits.");
        }finally {Client.Update(new Entity(Employee,FixtureEmployee){["cr2c9_employeelevel"]=original["cr2c9_employeelevel"]});}
        var entries=Find(BalanceEntryTable,"cr2c9_balanceperiod",period.Id,"cr2c9_hours");
        if(entries.Length!=3||entries.Sum(e=>e.GetAttributeValue<int>("cr2c9_hours"))!=128)throw new Exception("Rejected regression operations changed history.");
        Console.WriteLine("PASS external bulk writes rejected and history unchanged; no approvals or email test.");
    }
}
