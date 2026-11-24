using System.Net;
using System.Text.Json;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Application.Configuration;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Infrastructure.Storage;
using Xunit;

namespace TaxVision.CloudStorage.Tests.Storage;

/// <summary>
/// Las copias en el proveedor van ordenadas: {marca}/{categoría}/{año}/{nombre}, sin GUIDs. Un archivo que cambia
/// actualiza su misma copia; uno nuevo con el mismo nombre no pisa al anterior.
/// </summary>
public sealed class CloudFileMirrorFolderTests
{
    private static FileAvailableIntegrationEvent Available(CloudMirrorTestHost host, FileObject file) =>
        new()
        {
            TenantId = host.TenantId,
            FileId = file.Id,
            ObjectKey = file.ObjectKey,
            ContentType = "application/pdf",
            SizeBytes = file.SizeBytes,
            ChecksumSha256 = CloudMirrorTestHost.Checksum,
            CreatedBy = file.CreatedBy,
        };

    // ---------- Dropbox ----------

    private static DropboxFileMirror Dropbox(CloudMirrorTestHost host) =>
        new(
            host.Db,
            host.Storage,
            host.Services,
            host.Clients,
            Options.Create(new CloudStorageOptions { MainBucket = "main" }),
            Options.Create(new DropboxOptions { ClientId = "id", ClientSecret = "secret" }),
            host.Platform,
            NullLogger<DropboxFileMirror>.Instance
        );

    private static CloudMirrorTestHost DropboxHost()
    {
        var host = new CloudMirrorTestHost(DropboxConnectionService.ProviderId);
        host.Http.OnJson(r => r.RequestUri!.Host == "api.dropbox.com", """{"access_token":"at"}""")
            .On(
                r => r.RequestUri!.AbsolutePath.EndsWith("/files/upload"),
                r =>
                {
                    var arg = JsonDocument.Parse(r.Headers.GetValues("Dropbox-API-Arg").First()).RootElement;
                    var path = arg.GetProperty("path").GetString();
                    return ScriptedHttpHandler.Json($$"""{"id":"id:remote1","path_display":"{{path}}"}""");
                }
            );
        return host;
    }

    private static JsonElement DropboxArg(CloudMirrorTestHost host) =>
        JsonDocument.Parse(host.Http.Sent.Single(x => x.Uri.EndsWith("/files/upload")).DropboxArg!).RootElement;

    [Fact]
    public async Task Dropbox_new_file_goes_to_brand_category_and_year_without_overwriting()
    {
        var host = DropboxHost();
        var file = await host.AddFileAsync();

        await Dropbox(host).CopyAvailableFileAsync(Available(host, file), CancellationToken.None);

        var arg = DropboxArg(host);
        Assert.Equal("/TaxOffix/Documents/2025/w2.pdf", arg.GetProperty("path").GetString());
        Assert.Equal("add", arg.GetProperty("mode").GetString());
        Assert.True(arg.GetProperty("autorename").GetBoolean());
        var copy = await host.Db.CloudFileCopies.IgnoreQueryFilters().SingleAsync();
        Assert.Equal("/TaxOffix/Documents/2025/w2.pdf", copy.RemotePath);
    }

    [Fact]
    public async Task Dropbox_changed_file_overwrites_its_own_copy()
    {
        var host = DropboxHost();
        var file = await host.AddFileAsync();
        await host.AddStaleCopyAsync(
            file.Id,
            DropboxConnectionService.ProviderId,
            "id:remote1",
            "/TaxOffix/Documents/2025/w2 (1).pdf"
        );

        await Dropbox(host).CopyAvailableFileAsync(Available(host, file), CancellationToken.None);

        var arg = DropboxArg(host);
        Assert.Equal("/TaxOffix/Documents/2025/w2 (1).pdf", arg.GetProperty("path").GetString());
        Assert.Equal("overwrite", arg.GetProperty("mode").GetString());
    }

    [Fact]
    public async Task Dropbox_old_copy_without_path_is_uploaded_into_the_folders()
    {
        var host = DropboxHost();
        var file = await host.AddFileAsync();
        await host.AddStaleCopyAsync(file.Id, DropboxConnectionService.ProviderId, "id:legacy", null);

        await Dropbox(host).CopyAvailableFileAsync(Available(host, file), CancellationToken.None);

        Assert.Equal("/TaxOffix/Documents/2025/w2.pdf", DropboxArg(host).GetProperty("path").GetString());
    }

    // ---------- OneDrive ----------

    private static OneDriveFileMirror OneDrive(CloudMirrorTestHost host) =>
        new(
            host.Db,
            host.Storage,
            host.Services,
            host.Clients,
            Options.Create(new CloudStorageOptions { MainBucket = "main" }),
            Options.Create(new OneDriveOptions { ClientId = "id", ClientSecret = "secret" }),
            host.Platform,
            NullLogger<OneDriveFileMirror>.Instance
        );

    private static CloudMirrorTestHost OneDriveHost()
    {
        var host = new CloudMirrorTestHost(OneDriveConnectionService.ProviderId);
        host.Http.OnJson(r => r.RequestUri!.Host == "login.microsoftonline.com", """{"access_token":"at"}""")
            .OnJson(
                r => r.RequestUri!.AbsolutePath.EndsWith("/createUploadSession"),
                """{"uploadUrl":"https://upload.example.test/session"}"""
            )
            .OnJson(r => r.RequestUri!.Host == "upload.example.test", """{"id":"item-1","name":"w2.pdf"}""");
        return host;
    }

    [Fact]
    public async Task OneDrive_new_file_goes_to_brand_category_and_year_and_renames_on_conflict()
    {
        var host = OneDriveHost();
        var file = await host.AddFileAsync();

        await OneDrive(host).CopyAvailableFileAsync(Available(host, file), CancellationToken.None);

        var session = host.Http.Sent.Single(x => x.Uri.EndsWith("/createUploadSession"));
        Assert.Equal(
            "https://graph.microsoft.com/v1.0/me/drive/special/approot:/TaxOffix/Documents/2025/w2.pdf:/createUploadSession",
            session.Uri
        );
        Assert.Contains("\"@microsoft.graph.conflictBehavior\":\"rename\"", session.Body);
        Assert.Equal("item-1", (await host.Db.CloudFileCopies.IgnoreQueryFilters().SingleAsync()).RemoteFileId);
    }

    [Fact]
    public async Task OneDrive_changed_file_replaces_its_own_item()
    {
        var host = OneDriveHost();
        var file = await host.AddFileAsync();
        await host.AddStaleCopyAsync(file.Id, OneDriveConnectionService.ProviderId, "item-1", null);

        await OneDrive(host).CopyAvailableFileAsync(Available(host, file), CancellationToken.None);

        Assert.Equal(
            "https://graph.microsoft.com/v1.0/me/drive/items/item-1/createUploadSession",
            host.Http.Sent.Single(x => x.Uri.EndsWith("/createUploadSession")).Uri
        );
    }

    // ---------- Google Drive ----------

    private static GoogleDriveFileMirror Drive(CloudMirrorTestHost host) =>
        new(
            host.Db,
            host.Storage,
            host.Services,
            host.Clients,
            Options.Create(new CloudStorageOptions { MainBucket = "main" }),
            Options.Create(new GoogleDriveOptions { ClientId = "id", ClientSecret = "secret" }),
            host.Platform,
            NullLogger<GoogleDriveFileMirror>.Instance
        );

    /// <summary>Drive guionizado: búsquedas de carpetas vacías (o con `existing`), creación con ids f1, f2… y subida.</summary>
    private static CloudMirrorTestHost DriveHost(Dictionary<string, string>? existing = null, int failFirstUploads = 0)
    {
        var host = new CloudMirrorTestHost(GoogleDriveConnectionService.ProviderId);
        var created = 0;
        var uploads = 0;
        host.Http.OnJson(r => r.RequestUri!.Host == "oauth2.googleapis.com", """{"access_token":"at"}""")
            .On(
                r => r.Method == HttpMethod.Get && r.RequestUri!.AbsolutePath == "/drive/v3/files",
                r =>
                {
                    var query = Uri.UnescapeDataString(r.RequestUri!.Query);
                    var hit = existing?.FirstOrDefault(x => query.Contains($"name='{x.Key}'"));
                    return ScriptedHttpHandler.Json(
                        hit is { Key: not null } found
                            ? $$"""{"files":[{"id":"{{found.Value}}"}]}"""
                            : """{"files":[]}"""
                    );
                }
            )
            .On(
                r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath == "/drive/v3/files",
                _ => ScriptedHttpHandler.Json($$"""{"id":"f{{++created}}"}""")
            )
            .On(
                r => r.RequestUri!.AbsolutePath.StartsWith("/upload/drive/v3/files"),
                _ =>
                {
                    if (++uploads <= failFirstUploads)
                        return ScriptedHttpHandler.Json("""{"error":{"code":404}}""", HttpStatusCode.NotFound);
                    var response = new HttpResponseMessage(HttpStatusCode.OK);
                    response.Headers.Location = new Uri("https://upload.example.test/drive");
                    return response;
                }
            )
            .OnJson(r => r.RequestUri!.Host == "upload.example.test", """{"id":"drive-file-1"}""");
        return host;
    }

    [Fact]
    public async Task Drive_new_file_creates_brand_category_and_year_folders_and_uploads_into_the_last()
    {
        var host = DriveHost();
        var file = await host.AddFileAsync();

        await Drive(host).CopyAvailableFileAsync(Available(host, file), CancellationToken.None);

        var created = host
            .Http.Sent.Where(x =>
                x.Method == HttpMethod.Post && x.Uri.Contains("/drive/v3/files?") && !x.Uri.Contains("/upload/")
            )
            .ToList();
        Assert.Equal(3, created.Count);
        Assert.Contains("\"name\":\"TaxOffix\"", created[0].Body);
        Assert.Contains("\"parents\":[\"root\"]", created[0].Body);
        Assert.Contains("\"name\":\"Documents\"", created[1].Body);
        Assert.Contains("\"parents\":[\"f1\"]", created[1].Body);
        Assert.Contains("\"name\":\"2025\"", created[2].Body);
        var upload = host.Http.Sent.Single(x => x.Uri.Contains("/upload/drive/v3/files"));
        Assert.Contains("\"name\":\"w2.pdf\"", upload.Body);
        Assert.Contains("\"parents\":[\"f3\"]", upload.Body);
        var folders = await host.Db.CloudFolders.IgnoreQueryFilters().OrderBy(x => x.Path).ToListAsync();
        Assert.Equal(["TaxOffix", "TaxOffix/Documents", "TaxOffix/Documents/2025"], folders.Select(x => x.Path));
    }

    [Fact]
    public async Task Drive_second_file_reuses_the_folders_it_already_knows()
    {
        var host = DriveHost();
        var first = await host.AddFileAsync("a.pdf");
        var second = await host.AddFileAsync("b.pdf");
        var mirror = Drive(host);

        await mirror.CopyAvailableFileAsync(Available(host, first), CancellationToken.None);
        host.Http.Sent.Clear();
        await mirror.CopyAvailableFileAsync(Available(host, second), CancellationToken.None);

        Assert.DoesNotContain(host.Http.Sent, x => x.Uri.Contains("/drive/v3/files?") && !x.Uri.Contains("/upload/"));
    }

    [Fact]
    public async Task Drive_folder_that_already_exists_in_drive_is_reused()
    {
        var host = DriveHost(existing: new() { ["TaxOffix"] = "existing-root" });
        var file = await host.AddFileAsync();

        await Drive(host).CopyAvailableFileAsync(Available(host, file), CancellationToken.None);

        var created = host
            .Http.Sent.Where(x =>
                x.Method == HttpMethod.Post && x.Uri.Contains("/drive/v3/files?") && !x.Uri.Contains("/upload/")
            )
            .ToList();
        Assert.Equal(2, created.Count);
        Assert.Contains("\"parents\":[\"existing-root\"]", created[0].Body);
    }

    [Fact]
    public async Task Drive_changed_file_updates_its_own_copy()
    {
        var host = DriveHost();
        var file = await host.AddFileAsync();
        await host.AddStaleCopyAsync(file.Id, GoogleDriveConnectionService.ProviderId, "drive-file-1", null);

        await Drive(host).CopyAvailableFileAsync(Available(host, file), CancellationToken.None);

        var upload = host.Http.Sent.Single(x => x.Uri.Contains("/upload/drive/v3/files"));
        Assert.Equal(HttpMethod.Patch, upload.Method);
        Assert.Contains("/upload/drive/v3/files/drive-file-1?", upload.Uri);
    }

    // Si el usuario borró la carpeta en Drive, se olvida lo guardado y se vuelve a crear una vez.
    [Fact]
    public async Task Drive_missing_parent_folder_is_recreated_once()
    {
        var host = DriveHost(failFirstUploads: 1);
        var file = await host.AddFileAsync();

        await Drive(host).CopyAvailableFileAsync(Available(host, file), CancellationToken.None);

        var created = host.Http.Sent.Count(x =>
            x.Method == HttpMethod.Post && x.Uri.Contains("/drive/v3/files?") && !x.Uri.Contains("/upload/")
        );
        Assert.Equal(6, created);
        Assert.Equal("drive-file-1", (await host.Db.CloudFileCopies.IgnoreQueryFilters().SingleAsync()).RemoteFileId);
    }

    [Fact]
    public async Task Drive_disconnect_forgets_its_folders()
    {
        var host = new CloudMirrorTestHost(GoogleDriveConnectionService.ProviderId);
        host.Http.OnJson(r => r.RequestUri!.AbsolutePath == "/revoke", "{}");
        host.Db.CloudFolders.Add(
            TaxVision.CloudStorage.Domain.Providers.TenantCloudFolder.Create(
                host.TenantId,
                GoogleDriveConnectionService.ProviderId,
                "TaxOffix",
                "f1"
            )
        );
        await host.Db.SaveChangesAsync();
        var service = new GoogleDriveConnectionService(
            host.Db,
            host.Clients,
            host.Services,
            new CloudProviderOAuthStateStore(host.Db),
            Options.Create(new GoogleDriveOptions())
        );

        await service.DisconnectAsync(host.TenantId, CancellationToken.None);

        Assert.Empty(await host.Db.CloudFolders.IgnoreQueryFilters().ToListAsync());
    }
}
