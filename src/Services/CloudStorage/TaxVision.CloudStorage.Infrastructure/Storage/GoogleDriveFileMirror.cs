using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
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
                var remoteId = await UploadResumableAsync(
                    token.AccessToken,
                    file.OriginalName,
                    file.DetectedContentType ?? file.DeclaredContentType,
                    file.SizeBytes,
                    stream,
                    ct
                );
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

    private async Task<string> UploadResumableAsync(
        string accessToken,
        string name,
        string contentType,
        long size,
        Stream content,
        CancellationToken ct
    )
    {
        var client = clients.CreateClient("google-drive-api");
        using var initiate = new HttpRequestMessage(
            HttpMethod.Post,
            "https://www.googleapis.com/upload/drive/v3/files?uploadType=resumable&fields=id"
        );
        initiate.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        initiate.Headers.Add("X-Upload-Content-Type", contentType);
        initiate.Headers.Add(
            "X-Upload-Content-Length",
            size.ToString(System.Globalization.CultureInfo.InvariantCulture)
        );
        initiate.Content = JsonContent.Create(new DriveFileMetadata(name));
        using var response = await client.SendAsync(initiate, ct);
        response.EnsureSuccessStatusCode();
        var uploadUri =
            response.Headers.Location
            ?? throw new InvalidOperationException("Google Drive did not return an upload URL.");

        using var upload = new HttpRequestMessage(HttpMethod.Put, uploadUri)
        {
            Content = new StreamContent(content, 128 * 1024),
        };
        upload.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        upload.Content.Headers.ContentLength = size;
        using var uploaded = await client.SendAsync(upload, HttpCompletionOption.ResponseHeadersRead, ct);
        uploaded.EnsureSuccessStatusCode();
        var result = await uploaded.Content.ReadFromJsonAsync<DriveFileResponse>(cancellationToken: ct);
        return result?.Id
            ?? throw new InvalidOperationException("Google Drive upload response did not contain a file ID.");
    }

    private sealed record DriveFileMetadata([property: JsonPropertyName("name")] string Name);

    private sealed record DriveFileResponse([property: JsonPropertyName("id")] string Id);

    private sealed record AccessTokenResponse([property: JsonPropertyName("access_token")] string AccessToken);
}
