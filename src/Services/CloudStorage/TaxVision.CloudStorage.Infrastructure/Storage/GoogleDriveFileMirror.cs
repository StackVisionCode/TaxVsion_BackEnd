using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Platform;
using BuildingBlocks.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Configuration;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Providers;
using TaxVision.CloudStorage.Infrastructure.Persistence;

namespace TaxVision.CloudStorage.Infrastructure.Storage;

public sealed class GoogleDriveFileMirror(
    CloudStorageDbContext db,
    IObjectStorage storage,
    IServiceProvider services,
    IHttpClientFactory clients,
    IOptions<CloudStorageOptions> storageOptions,
    IOptions<GoogleDriveOptions> driveOptions,
    IPlatformSettings platform,
    ILogger<GoogleDriveFileMirror> logger
) : IStoragePolicyProviderAdapter
{
    public string ProviderId => "google-drive";

    /// <summary>Espeja archivos ya disponibles; nunca intercepta upload, escaneo ni acceso interno MinIO.</summary>
    public Task CopyAvailableFileAsync(FileAvailableIntegrationEvent message, CancellationToken ct) =>
        CopyAvailableFileInternalAsync(FileToMirror.From(message), ct);

    public async Task BackupTenantAsync(Guid tenantId, CancellationToken ct)
    {
        var connected = await db
            .CloudConnections.IgnoreQueryFilters()
            .AnyAsync(x => x.TenantId == tenantId && x.Provider == ProviderId, ct);
        if (!connected)
            throw new InvalidOperationException("The selected cloud provider is not connected for this tenant.");
        var files = await db
            .Files.IgnoreQueryFilters()
            // Sin fotos de perfil ni pistas de grabación: su borrado (reemplazo/retención) nunca llegaría al proveedor externo.
            .Where(x =>
                x.TenantId == tenantId
                && x.Status == FileStatus.Available
                && x.FolderType != FolderType.Avatars
                && x.FolderType != FolderType.RecordingTracks
            )
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync(ct);
        foreach (var file in files)
        {
            await CopyAvailableFileInternalAsync(
                new FileToMirror(tenantId, file.Id, file.ChecksumSha256 ?? string.Empty),
                ct
            );
        }
    }

    private async Task CopyAvailableFileInternalAsync(FileToMirror message, CancellationToken ct)
    {
        var connection = await db
            .CloudConnections.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.TenantId == message.TenantId && x.Provider == "google-drive", ct);
        if (connection is null)
            return;

        var existing = await db
            .CloudFileCopies.IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                x => x.TenantId == message.TenantId && x.FileId == message.FileId && x.Provider == "google-drive",
                ct
            );
        if (existing?.ChecksumSha256 == message.ChecksumSha256)
            return;

        var file = await db
            .Files.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.TenantId == message.TenantId && x.Id == message.FileId, ct);
        if (
            file is null
            || !string.Equals(file.ChecksumSha256, message.ChecksumSha256, StringComparison.OrdinalIgnoreCase)
        )
            return;
        if (
            !services
                .GetRequiredService<ISecretProtector>()
                .TryUnprotect(connection.ProtectedRefreshToken, out var refreshToken, out _)
        )
            throw new InvalidOperationException(
                "Google Drive refresh token could not be decrypted; reconnect this tenant."
            );

        var config = driveOptions.Value;
        using var tokenResponse = await clients
            .CreateClient("google-drive-oauth")
            .PostAsync(
                "https://oauth2.googleapis.com/token",
                new FormUrlEncodedContent(
                    new Dictionary<string, string>
                    {
                        ["client_id"] = config.ClientId,
                        ["client_secret"] = config.ClientSecret,
                        ["refresh_token"] = refreshToken,
                        ["grant_type"] = "refresh_token",
                    }
                ),
                ct
            );
        tokenResponse.EnsureSuccessStatusCode();
        var token =
            await tokenResponse.Content.ReadFromJsonAsync<AccessTokenResponse>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Google did not return an access token.");

        // Stage on disk so large files do not consume application memory. The source of truth remains MinIO.
        var tempPath = Path.Combine(Path.GetTempPath(), $"taxvision-drive-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (
                var stream = new FileStream(
                    tempPath,
                    FileMode.CreateNew,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    128 * 1024,
                    FileOptions.Asynchronous | FileOptions.DeleteOnClose
                )
            )
            {
                await storage.DownloadAsync(storageOptions.Value.MainBucket, file.ObjectKey, stream, ct);
                stream.Position = 0;
                var path = CloudMirrorPath.For(
                    platform.Current.BrandName,
                    file.FolderType,
                    file.TaxYear,
                    file.OriginalName
                );
                var upload = new DriveUpload(
                    token.AccessToken,
                    file.DetectedContentType ?? file.DeclaredContentType,
                    file.SizeBytes,
                    stream
                );
                var remoteId = await UploadAsync(message.TenantId, existing?.RemoteFileId, path, upload, ct);
                if (existing is not null)
                    db.CloudFileCopies.Remove(existing);
                db.CloudFileCopies.Add(
                    TenantCloudFileCopy.Create(message.TenantId, file.Id, remoteId, message.ChecksumSha256)
                );
                await db.SaveChangesAsync(ct);
            }
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch (IOException ex)
            {
                logger.LogWarning(ex, "Could not remove temporary Google Drive staging file.");
            }
        }
    }

    private sealed record DriveUpload(string AccessToken, string ContentType, long Size, Stream Content);

    /// <summary>
    /// Un archivo que ya tiene copia se actualiza en su sitio; uno nuevo entra en {marca}/{categoría}/{año}. Si una
    /// carpeta guardada ya no existe en Drive (404), se olvidan y se vuelven a resolver una vez.
    /// </summary>
    private async Task<string> UploadAsync(
        Guid tenantId,
        string? existingRemoteId,
        CloudMirrorPath path,
        DriveUpload upload,
        CancellationToken ct
    )
    {
        var client = clients.CreateClient("google-drive-api");
        if (!string.IsNullOrWhiteSpace(existingRemoteId))
        {
            var updated = await TryUploadAsync(
                client,
                HttpMethod.Patch,
                $"https://www.googleapis.com/upload/drive/v3/files/{Uri.EscapeDataString(existingRemoteId)}?uploadType=resumable&fields=id",
                new DriveFileUpdate(),
                upload,
                ct
            );
            if (updated is not null)
                return updated;
        }

        var folders = new GoogleDriveFolders(db, client, upload.AccessToken);
        for (var attempt = 0; ; attempt++)
        {
            var parentId = await folders.ResolveAsync(tenantId, path.Folders, ct);
            var created = await TryUploadAsync(
                client,
                HttpMethod.Post,
                "https://www.googleapis.com/upload/drive/v3/files?uploadType=resumable&fields=id",
                new DriveFileMetadata(path.FileName, [parentId]),
                upload,
                ct
            );
            if (created is not null)
                return created;
            if (attempt > 0)
                throw new InvalidOperationException("The Google Drive folder for this copy could not be found.");
            await folders.ForgetAsync(tenantId, ct);
        }
    }

    /// <summary>Sesión reanudable de Drive. Null si el destino no existe (404), antes de enviar el contenido.</summary>
    private static async Task<string?> TryUploadAsync<TMetadata>(
        HttpClient client,
        HttpMethod method,
        string url,
        TMetadata metadata,
        DriveUpload upload,
        CancellationToken ct
    )
    {
        using var initiate = new HttpRequestMessage(method, url);
        initiate.Headers.Authorization = new AuthenticationHeaderValue("Bearer", upload.AccessToken);
        initiate.Headers.Add("X-Upload-Content-Type", upload.ContentType);
        initiate.Headers.Add(
            "X-Upload-Content-Length",
            upload.Size.ToString(System.Globalization.CultureInfo.InvariantCulture)
        );
        initiate.Content = JsonContent.Create(metadata);
        using var response = await client.SendAsync(initiate, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        var uploadUri =
            response.Headers.Location
            ?? throw new InvalidOperationException("Google Drive did not return an upload URL.");

        using var put = new HttpRequestMessage(HttpMethod.Put, uploadUri)
        {
            Content = new StreamContent(upload.Content, 128 * 1024),
        };
        put.Content.Headers.ContentType = new MediaTypeHeaderValue(upload.ContentType);
        put.Content.Headers.ContentLength = upload.Size;
        using var uploaded = await client.SendAsync(put, HttpCompletionOption.ResponseHeadersRead, ct);
        uploaded.EnsureSuccessStatusCode();
        var result = await uploaded.Content.ReadFromJsonAsync<DriveFileResponse>(cancellationToken: ct);
        return result?.Id
            ?? throw new InvalidOperationException("Google Drive upload response did not contain a file ID.");
    }

    private sealed record DriveFileMetadata(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("parents")] string[] Parents
    );

    // Actualizar solo el contenido: nombre y carpeta se quedan como estén en Drive.
    private sealed record DriveFileUpdate;

    private sealed record DriveFileResponse([property: JsonPropertyName("id")] string Id);

    private sealed record AccessTokenResponse([property: JsonPropertyName("access_token")] string AccessToken);
}
