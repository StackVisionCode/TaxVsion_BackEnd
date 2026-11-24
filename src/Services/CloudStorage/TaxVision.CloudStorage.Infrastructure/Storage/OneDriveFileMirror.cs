using System.Net.Http.Headers;
using System.Net.Http.Json;
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

/// <summary>Mirrors clean CRM files to the tenant OneDrive app folder; MinIO stays authoritative.</summary>
public sealed class OneDriveFileMirror(
    CloudStorageDbContext db,
    IObjectStorage storage,
    IServiceProvider services,
    IHttpClientFactory clients,
    IOptions<CloudStorageOptions> storageOptions,
    IOptions<OneDriveOptions> driveOptions,
    IPlatformSettings platform,
    ILogger<OneDriveFileMirror> logger
) : IStoragePolicyProviderAdapter
{
    private const string TokenEndpoint = "https://login.microsoftonline.com/common/oauth2/v2.0/token";
    private const string GraphEndpoint = "https://graph.microsoft.com/v1.0";
    private const int ChunkSize = 10 * 320 * 1024;
    public string ProviderId => OneDriveConnectionService.ProviderId;

    public Task CopyAvailableFileAsync(FileAvailableIntegrationEvent message, CancellationToken ct) =>
        CopyAsync(FileToMirror.From(message), ct);

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
            await CopyAsync(new FileToMirror(tenantId, file.Id, file.ChecksumSha256 ?? string.Empty), ct);
    }

    private async Task CopyAsync(FileToMirror message, CancellationToken ct)
    {
        var connection = await db
            .CloudConnections.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.TenantId == message.TenantId && x.Provider == ProviderId, ct);
        if (connection is null)
            return;
        var existing = await db
            .CloudFileCopies.IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                x => x.TenantId == message.TenantId && x.FileId == message.FileId && x.Provider == ProviderId,
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

        var protector = services.GetRequiredService<ISecretProtector>();
        if (!protector.TryUnprotect(connection.ProtectedRefreshToken, out var refreshToken, out _))
            throw new InvalidOperationException(
                "OneDrive refresh token could not be decrypted; reconnect this tenant."
            );
        var config = driveOptions.Value;
        using var tokenResponse = await clients
            .CreateClient("onedrive-oauth")
            .PostAsync(
                TokenEndpoint,
                new FormUrlEncodedContent(
                    new Dictionary<string, string>
                    {
                        ["client_id"] = config.ClientId,
                        ["client_secret"] = config.ClientSecret,
                        ["refresh_token"] = refreshToken,
                        ["grant_type"] = "refresh_token",
                        ["scope"] = OneDriveConnectionService.Scopes,
                    }
                ),
                ct
            );
        tokenResponse.EnsureSuccessStatusCode();
        var token =
            await tokenResponse.Content.ReadFromJsonAsync<AccessTokenResponse>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Microsoft did not return an access token.");
        if (!string.IsNullOrWhiteSpace(token.RefreshToken))
        {
            connection.UpdateProtectedRefreshToken(protector.Protect(token.RefreshToken));
            await db.SaveChangesAsync(ct);
        }

        var path = CloudMirrorPath.For(platform.Current.BrandName, file.FolderType, file.TaxYear, file.OriginalName);
        var tempPath = Path.Combine(Path.GetTempPath(), $"taxvision-onedrive-{Guid.NewGuid():N}.tmp");
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
                var remoteId = await UploadAsync(
                    token.AccessToken,
                    existing?.RemoteFileId,
                    path,
                    file.SizeBytes,
                    stream,
                    ct
                );
                if (existing is not null)
                    db.CloudFileCopies.Remove(existing);
                db.CloudFileCopies.Add(
                    TenantCloudFileCopy.Create(message.TenantId, file.Id, ProviderId, remoteId, message.ChecksumSha256)
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
                logger.LogWarning(ex, "Could not remove temporary OneDrive staging file.");
            }
        }
    }

    /// <summary>
    /// Ya copiado: se reemplaza el contenido de su mismo elemento (por id). Nuevo, o si el usuario lo borró:
    /// {marca}/{categoría}/{año} dentro de la carpeta de la app, renombrando si ya hay uno con ese nombre.
    /// </summary>
    private async Task<string> UploadAsync(
        string accessToken,
        string? existingItemId,
        CloudMirrorPath path,
        long size,
        Stream content,
        CancellationToken ct
    )
    {
        if (!string.IsNullOrWhiteSpace(existingItemId))
        {
            var item = $"{GraphEndpoint}/me/drive/items/{Uri.EscapeDataString(existingItemId)}";
            var replaced = await TryUploadAsync(
                accessToken,
                $"{item}/content",
                $"{item}/createUploadSession",
                renameOnConflict: false,
                size,
                content,
                ct
            );
            if (replaced is not null)
                return replaced;
        }

        // Alias estable de la carpeta de la app: los ids de approot traen '!' y Graph los rechaza escapados en la ruta.
        var segments = string.Join('/', path.Folders.Append(path.FileName).Select(Uri.EscapeDataString));
        var target = $"{GraphEndpoint}/me/drive/special/approot:/{segments}:";
        return await TryUploadAsync(
                accessToken,
                $"{target}/content?@microsoft.graph.conflictBehavior=rename",
                $"{target}/createUploadSession",
                renameOnConflict: true,
                size,
                content,
                ct
            ) ?? throw new InvalidOperationException("Microsoft Graph could not find the OneDrive app folder.");
    }

    /// <summary>Sube por PUT (vacío) o por sesión. Null si el destino no existe (404), antes de enviar el contenido.</summary>
    private async Task<string?> TryUploadAsync(
        string accessToken,
        string simpleUrl,
        string sessionUrl,
        bool renameOnConflict,
        long size,
        Stream content,
        CancellationToken ct
    )
    {
        if (size == 0)
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, simpleUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Content = new ByteArrayContent(Array.Empty<byte>());
            using var response = await clients.CreateClient("onedrive-graph").SendAsync(request, ct);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;
            await EnsureGraphSuccessAsync(response, "uploading an empty file", ct);
            var item = await response.Content.ReadFromJsonAsync<DriveItemResponse>(cancellationToken: ct);
            return item?.Id
                ?? throw new InvalidOperationException("Microsoft Graph did not return the uploaded OneDrive file ID.");
        }

        using var create = new HttpRequestMessage(HttpMethod.Post, sessionUrl);
        create.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        create.Content = JsonContent.Create(
            new UploadSessionRequest(new UploadSessionItem(renameOnConflict ? "rename" : "replace"))
        );
        using var created = await clients.CreateClient("onedrive-graph").SendAsync(create, ct);
        if (created.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        await EnsureGraphSuccessAsync(created, "creating an upload session", ct);
        var session = await created.Content.ReadFromJsonAsync<UploadSessionResponse>(cancellationToken: ct);
        if (
            !Uri.TryCreate(session?.UploadUrl, UriKind.Absolute, out var uploadUri)
            || uploadUri.Scheme != Uri.UriSchemeHttps
        )
            throw new InvalidOperationException("Microsoft Graph did not return a valid OneDrive upload session.");

        var buffer = new byte[ChunkSize];
        long offset = 0;
        var uploadClient = clients.CreateClient("onedrive-upload");
        while (offset < size)
        {
            var expected = (int)Math.Min(buffer.Length, size - offset);
            var read = 0;
            while (read < expected)
            {
                var count = await content.ReadAsync(buffer.AsMemory(read, expected - read), ct);
                if (count == 0)
                    throw new EndOfStreamException("The CRM file ended before the OneDrive upload completed.");
                read += count;
            }
            using var chunk = new HttpRequestMessage(HttpMethod.Put, uploadUri);
            chunk.Content = new ByteArrayContent(buffer, 0, read);
            chunk.Content.Headers.ContentLength = read;
            chunk.Content.Headers.ContentRange = new ContentRangeHeaderValue(offset, offset + read - 1, size);
            using var uploadedResponse = await uploadClient.SendAsync(
                chunk,
                HttpCompletionOption.ResponseHeadersRead,
                ct
            );
            await EnsureGraphSuccessAsync(uploadedResponse, "uploading a file chunk", ct);
            offset += read;
            if (offset == size)
            {
                var item = await uploadedResponse.Content.ReadFromJsonAsync<DriveItemResponse>(cancellationToken: ct);
                return item?.Id
                    ?? throw new InvalidOperationException(
                        "Microsoft Graph did not return the uploaded OneDrive file ID."
                    );
            }
        }
        throw new InvalidOperationException("The OneDrive upload session ended without completing the file.");
    }

    private static async Task EnsureGraphSuccessAsync(
        HttpResponseMessage response,
        string operation,
        CancellationToken ct
    )
    {
        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync(ct);
        string? graphCode = null;
        string? graphMessage = null;
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error", out var error))
            {
                if (error.TryGetProperty("code", out var code))
                    graphCode = code.GetString();
                if (error.TryGetProperty("message", out var message))
                    graphMessage = message.GetString();
            }
        }
        catch (System.Text.Json.JsonException) { }

        var details =
            string.IsNullOrWhiteSpace(graphCode) && string.IsNullOrWhiteSpace(graphMessage)
                ? string.Empty
                : $" Graph error: {graphCode}: {graphMessage}";
        throw new HttpRequestException(
            $"Microsoft Graph returned {(int)response.StatusCode} ({response.StatusCode}) while {operation}.{details}",
            null,
            response.StatusCode
        );
    }

    private sealed record UploadSessionRequest(
        [property: System.Text.Json.Serialization.JsonPropertyName("item")] UploadSessionItem Item
    );

    private sealed record UploadSessionItem(
        [property: System.Text.Json.Serialization.JsonPropertyName("@microsoft.graph.conflictBehavior")]
            string ConflictBehavior
    );

    private sealed record UploadSessionResponse(
        [property: System.Text.Json.Serialization.JsonPropertyName("uploadUrl")] string UploadUrl
    );

    private sealed record DriveItemResponse(
        [property: System.Text.Json.Serialization.JsonPropertyName("id")] string Id
    );

    private sealed record AccessTokenResponse(
        [property: System.Text.Json.Serialization.JsonPropertyName("access_token")] string AccessToken,
        [property: System.Text.Json.Serialization.JsonPropertyName("refresh_token")] string? RefreshToken
    );
}
