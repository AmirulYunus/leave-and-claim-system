using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

internal static partial class Development
{
    static void SuperiorAccessInspect()
    {
        foreach (var table in new[] { Request, "cr2c9_leaverequestdate", History,
                     "cr2c9_leavecancellation", "annotation" })
        {
            var meta = ((RetrieveEntityResponse)Client.Execute(new RetrieveEntityRequest {
                LogicalName = table, EntityFilters = EntityFilters.Entity | EntityFilters.Relationships,
                RetrieveAsIfPublished = true
            })).EntityMetadata;
            Console.WriteLine(table + " ownership=" + meta.OwnershipType);
            foreach (var relation in meta.ManyToOneRelationships ?? Array.Empty<OneToManyRelationshipMetadata>())
            {
                if (relation.ReferencedEntity != Request) continue;
                Console.WriteLine("  " + relation.SchemaName + " lookup=" + relation.ReferencingAttribute +
                    " share=" + relation.CascadeConfiguration?.Share +
                    " unshare=" + relation.CascadeConfiguration?.Unshare);
            }
        }
        foreach (var table in new[] { Request, "cr2c9_leavecancellation" })
        {
            var query = new QueryExpression("savedquery") {
                ColumnSet = new ColumnSet("name", "fetchxml", "querytype", "statecode")
            };
            query.Criteria.AddCondition("returnedtypecode", ConditionOperator.Equal, table);
            foreach (var view in Client.RetrieveMultiple(query).Entities)
            {
                Console.WriteLine("VIEW " + table + " " + view.Id + " " + view.GetAttributeValue<string>("name") +
                    " type=" + view.GetAttributeValue<int?>("querytype") +
                    " state=" + view.GetAttributeValue<Microsoft.Xrm.Sdk.OptionSetValue>("statecode")?.Value);
                Console.WriteLine(view.GetAttributeValue<string>("fetchxml"));
            }
        }
    }
}
