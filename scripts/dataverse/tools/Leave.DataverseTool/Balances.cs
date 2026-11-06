using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Crm.Sdk.Messages;

internal static partial class Development
{
    const string BalancePeriodTable = "cr2c9_leavebalanceperiod", BalanceEntryTable = "cr2c9_leavebalanceentry";
    static void BalanceInspect()
    {
        var all = ((RetrieveAllEntitiesResponse)Client.Execute(new RetrieveAllEntitiesRequest {
            EntityFilters = EntityFilters.Entity, RetrieveAsIfPublished = true
        })).EntityMetadata;
        foreach (var m in all.Where(x => x.LogicalName.StartsWith("cr2c9_") &&
                     (x.LogicalName.Contains("balance") || x.LogicalName.Contains("entitlement") || x.LogicalName.Contains("holiday"))))
            Console.WriteLine("Existing schema: " + m.LogicalName + " " + m.DisplayName?.UserLocalizedLabel?.Label);
        foreach (var table in new[]{Employee,Request,"cr2c9_leavetype"}) {
            Console.WriteLine("Relevant schema: " + table);
            foreach (var a in Meta(table).Attributes.Where(x => x.LogicalName.StartsWith("cr2c9_"))) {
                Console.WriteLine("  " + a.LogicalName + " " + a.AttributeType + " " + a.DisplayName?.UserLocalizedLabel?.Label);
                if (a is PicklistAttributeMetadata choice)
                    Console.WriteLine("    choices " + string.Join(", ",choice.OptionSet.Options.Select(x => x.Value + "=" + x.Label?.UserLocalizedLabel?.Label)));
            }
        }
        foreach (var table in new[]{BalancePeriodTable,BalanceEntryTable}.Where(t=>all.Any(x=>x.LogicalName==t))) {
            var metadata = ((RetrieveEntityResponse)Client.Execute(new RetrieveEntityRequest {LogicalName=table,EntityFilters=EntityFilters.All,RetrieveAsIfPublished=true})).EntityMetadata;
            Console.WriteLine(table + " concurrency=" + metadata.IsOptimisticConcurrencyEnabled + "; keys=" + string.Join(",",metadata.Keys.Select(x=>x.LogicalName+":"+x.EntityKeyIndexStatus)));
        }
        Console.WriteLine("Read-only balance schema inspection; no employee or request records exported.");
    }
    static void BalanceSchema()
    {
        var all = ((RetrieveAllEntitiesResponse)Client.Execute(new RetrieveAllEntitiesRequest {EntityFilters=EntityFilters.Entity,RetrieveAsIfPublished=true})).EntityMetadata;
        void Table(string name,string display,string plural,string description) {
            var existing = all.SingleOrDefault(x=>x.LogicalName==name);
            if (existing != null) {
                if (existing.OwnershipType != OwnershipTypes.UserOwned || existing.IsActivity == true) throw new Exception("Existing balance table contract differs.");
                Console.WriteLine("Verified existing " + name); return;
            }
            var r=(CreateEntityResponse)Client.Execute(new CreateEntityRequest {SolutionUniqueName=Solution,
                Entity=new EntityMetadata {SchemaName=name,DisplayName=L(display),DisplayCollectionName=L(plural),Description=L(description),OwnershipType=OwnershipTypes.UserOwned,IsActivity=false},
                PrimaryAttribute=Text("cr2c9_name","Name",120,true),HasActivities=false,HasNotes=false});
            Console.WriteLine("Created " + name + " " + r.EntityId);
        }
        void Lookup(string table,string field,string display,string target,bool required) {
            if(Meta(table).Attributes.Any(a=>a.LogicalName==field)) {
                var a=Meta(table).Attributes.Single(a=>a.LogicalName==field) as LookupAttributeMetadata;
                if(a==null || a.Targets.Length!=1 || a.Targets[0]!=target) throw new Exception("Existing lookup contract differs: " + field);
                return;
            }
            Client.Execute(new CreateOneToManyRequest {SolutionUniqueName=Solution,
                Lookup=new LookupAttributeMetadata {SchemaName=field,DisplayName=L(display),RequiredLevel=new AttributeRequiredLevelManagedProperty(required?AttributeRequiredLevel.ApplicationRequired:AttributeRequiredLevel.None)},
                OneToManyRelationship=new OneToManyRelationshipMetadata {SchemaName=table+"_"+field,ReferencedEntity=target,ReferencingEntity=table,
                    CascadeConfiguration=new CascadeConfiguration {Assign=CascadeType.NoCascade,Delete=CascadeType.Restrict,Merge=CascadeType.NoCascade,Reparent=CascadeType.NoCascade,Share=CascadeType.NoCascade,Unshare=CascadeType.NoCascade}}});
            Console.WriteLine("Created restrictive relationship " + table + "." + field);
        }
        void Date(string field,string display) => Attribute(BalancePeriodTable,new DateTimeAttributeMetadata {
            SchemaName=field,DisplayName=L(display),DateTimeBehavior=DateTimeBehavior.DateOnly,Format=DateTimeFormat.DateOnly,
            RequiredLevel=new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.ApplicationRequired)});
        void Key(string table,string name,params string[] fields) {
            var meta=((RetrieveEntityResponse)Client.Execute(new RetrieveEntityRequest {LogicalName=table,EntityFilters=EntityFilters.All,RetrieveAsIfPublished=true})).EntityMetadata;
            var existing=meta.Keys.SingleOrDefault(k=>k.LogicalName==name);
            if(existing!=null) {
                if(!existing.KeyAttributes.Order().SequenceEqual(fields.Order())) throw new Exception("Existing balance key contract differs.");
                Console.WriteLine("Key " + name + " " + existing.EntityKeyIndexStatus); return;
            }
            Client.Execute(new CreateEntityKeyRequest {SolutionUniqueName=Solution,EntityName=table,EntityKey=new EntityKeyMetadata {SchemaName=name,DisplayName=L(name),KeyAttributes=fields}});
            Console.WriteLine("Created key " + name + "; must be Active before processing.");
        }
        Table(BalancePeriodTable,"Leave Balance Period","Leave Balance Periods","Explicit employee/type balance periods. Grant policy and processing are not active yet.");
        Table(BalanceEntryTable,"Leave Balance Entry","Leave Balance Entries","Server ledger of grants, approved deductions and exact reversals. Processing is not active yet.");
        Lookup(BalancePeriodTable,"cr2c9_employee","Employee",Employee,true);
        Lookup(BalancePeriodTable,"cr2c9_leavetype","Leave Type","cr2c9_leavetype",true);
        Date("cr2c9_periodstart","Period Start"); Date("cr2c9_periodend","Period End");
        Attribute(BalancePeriodTable,Text("cr2c9_reviewlocktoken","Server Balance Lock",36));
        Lookup(BalanceEntryTable,"cr2c9_balanceperiod","Balance Period",BalancePeriodTable,true);
        Lookup(BalanceEntryTable,"cr2c9_leaverequest","Leave Request",Request,false);
        Lookup(BalanceEntryTable,"cr2c9_reverses","Reverses Entry",BalanceEntryTable,false);
        Attribute(BalanceEntryTable,Text("cr2c9_operationkey","Operation Key",200,true));
        Attribute(BalanceEntryTable,new IntegerAttributeMetadata {SchemaName="cr2c9_hours",DisplayName=L("Hours"),MinValue=-100000,MaxValue=100000,Format=IntegerFormat.None,
            RequiredLevel=new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.ApplicationRequired)});
        Attribute(BalanceEntryTable,new PicklistAttributeMetadata {SchemaName="cr2c9_entrykind",DisplayName=L("Entry Kind"),
            OptionSet=new OptionSetMetadata {IsGlobal=false,OptionSetType=OptionSetType.Picklist,Options={new OptionMetadata(L("Grant"),290040000),new OptionMetadata(L("Approved Leave"),290040001),new OptionMetadata(L("Reversal"),290040002)}},
            RequiredLevel=new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.ApplicationRequired)});
        Key(BalancePeriodTable,"cr2c9_uniquebalanceperiod","cr2c9_employee","cr2c9_leavetype","cr2c9_periodstart");
        Key(BalanceEntryTable,"cr2c9_uniquebalanceoperation","cr2c9_operationkey");
        Client.Execute(new PublishXmlRequest {ParameterXml="<importexportxml><entities><entity>"+BalancePeriodTable+"</entity><entity>"+BalanceEntryTable+"</entity></entities></importexportxml>"});
        Console.WriteLine("Published balance schema only. No records, credits, deductions, registrations, roles or flows changed. Processing must remain inactive until policy and write guards are deployed.");
    }
}
