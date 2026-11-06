using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml;
using Microsoft.Identity.Client;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Crm.Sdk.Messages;

if (args.Length == 1 && args[0] == "develop") { await Development.Run(); return; }

// Read-only recovery tool. No token extraction, stored passwords, record writes,
// schema changes, role changes, or date conversion. Tokens stay in memory.
const string environment = "https://orgf7668341.crm5.dynamics.com";
const string expectedUser = "a8f9e5ea-402e-43a1-812c-dffee674d6f9";
if (args.Length != 2 || args[0] != "backup" || !Path.IsPathFullyQualified(args[1]))
    throw new ArgumentException("Usage: Leave.DataverseTool backup /absolute/private/output-directory");
var destination = Path.GetFullPath(args[1]);
if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
    throw new InvalidOperationException("Use an empty output directory; recovery files are never overwritten.");

// Microsoft's documented sample public client. A tenant consent request, if any,
// must be handled separately; this tool never grants consent or changes access.
var app = PublicClientApplicationBuilder.Create("51f81489-12ee-4a9e-aaae-a2591f45987d")
    .WithAuthority("https://login.microsoftonline.com/organizations")
    .WithRedirectUri("http://localhost").Build();
var scopes = new[] { environment + "/user_impersonation" };
var authentication = await app.AcquireTokenWithDeviceCode(scopes, code => {
    Console.WriteLine(code.Message); return Task.CompletedTask;
}).ExecuteAsync();
using var client = new ServiceClient(new Uri(environment), async _ => {
    authentication = await app.AcquireTokenSilent(scopes, authentication.Account).ExecuteAsync();
    return authentication.AccessToken;
}, true);
if (!client.IsReady) throw new InvalidOperationException("Dataverse connection failed.");
var who = (WhoAmIResponse)client.Execute(new WhoAmIRequest());
var identity = client.Retrieve("systemuser", who.UserId, new ColumnSet("azureactivedirectoryobjectid"));
if (!string.Equals(identity.GetAttributeValue<Guid>("azureactivedirectoryobjectid").ToString(), expectedUser,
    StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("Signed-in identity differs from the authorized development account.");

Directory.CreateDirectory(destination);
if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(destination, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
var tables = new[] { "cr2c9_employee", "cr2c9_leaverequest", "cr2c9_leaverequestdate", "cr2c9_leavetype", "cr2c9_leaveapproval" };
var manifest = new List<object>();
var serializer = new DataContractSerializer(typeof(EntityCollection));
foreach (var table in tables) {
    var query = new QueryExpression(table) { ColumnSet = new ColumnSet(true), PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 } };
    query.AddOrder(table + "id", OrderType.Ascending);
    var snapshot = new EntityCollection { EntityName = table };
    while (true) {
        var page = client.RetrieveMultiple(query);
        snapshot.Entities.AddRange(page.Entities);
        if (!page.MoreRecords) break;
        query.PageInfo.PageNumber++;
        query.PageInfo.PagingCookie = page.PagingCookie;
    }
    var path = Path.Combine(destination, table + ".xml");
    using (var writer = XmlWriter.Create(path, new XmlWriterSettings { Indent = true })) serializer.WriteObject(writer, snapshot);
    // Verify that the recovery file is readable and retains every record ID.
    using (var reader = XmlReader.Create(path)) {
        var restored = (EntityCollection)serializer.ReadObject(reader)!;
        if (!restored.Entities.Select(x => x.Id).SequenceEqual(snapshot.Entities.Select(x => x.Id)))
            throw new InvalidOperationException("Backup verification failed for " + table);
    }
    if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    manifest.Add(new { table, count = snapshot.Entities.Count, file = Path.GetFileName(path), sha256 = hash });
    Console.WriteLine($"Verified {table}: {snapshot.Entities.Count} records.");
    if (table == "cr2c9_leaverequest") {
        foreach (var record in snapshot.Entities.Where(r => r.GetAttributeValue<Guid>("cr2c9_supportingdocument") != Guid.Empty)) {
            var init = (InitializeFileBlocksDownloadResponse)client.Execute(new InitializeFileBlocksDownloadRequest {
                Target = record.ToEntityReference(), FileAttributeName = "cr2c9_supportingdocument"
            });
            var filePath = Path.Combine(destination, record.Id + "-supporting-document.bin");
            using (var output = File.Create(filePath)) {
                for (long offset = 0; offset < init.FileSizeInBytes;) {
                    var length = init.IsChunkingSupported ? Math.Min(4 * 1024 * 1024, init.FileSizeInBytes - offset) : init.FileSizeInBytes;
                    var block = (DownloadBlockResponse)client.Execute(new DownloadBlockRequest {
                        FileContinuationToken = init.FileContinuationToken, Offset = offset, BlockLength = length
                    });
                    if (block.Data.Length == 0) throw new IOException("Empty block in supporting-document backup.");
                    output.Write(block.Data); offset += block.Data.Length;
                }
            }
            if (new FileInfo(filePath).Length != init.FileSizeInBytes) throw new IOException("File backup length mismatch.");
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(filePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            manifest.Add(new { requestId = record.Id, column = "cr2c9_supportingdocument", originalName = init.FileName,
                file = Path.GetFileName(filePath), sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(filePath))).ToLowerInvariant() });
        }
    }
}
var manifestPath = Path.Combine(destination, "manifest.json");
File.WriteAllText(manifestPath, JsonSerializer.Serialize(new {
    environment, organizationId = who.OrganizationId, capturedAtUtc = DateTime.UtcNow,
    consistentAcrossTables = false,
    note = "Raw SDK values preserved; snapshots are sequential. No date conversion or record restore was performed. Existing Attachment text is retained as a reference; external files are outside this backup.",
    entries = manifest
}, new JsonSerializerOptions { WriteIndented = true }));
if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(manifestPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
Console.WriteLine("Backup completed: " + manifestPath);
