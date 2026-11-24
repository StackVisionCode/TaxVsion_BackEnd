using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
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

/// <summary>Mirrors clean CRM files into the tenant's Dropbox app folder; MinIO remains authoritative.</summary>
public sealed class DropboxFileMirror(
    CloudStorageDbContext db,
    IObjectStorage storage,
    IServiceProvider services,
    IHttpClientFactory clients,
    IOptions<CloudStorageOptions> storageOptions,
    IOptions<DropboxOptions> dropboxOptions,
    IPlatformSettings platform,
    ILogger<DropboxFileMirror> logger
) : IStoragePolicyProviderAdapter
{
    private const string TokenEndpoint = "https://api.dropbox.com/oauth2/token";
    private const string ContentEndpoint = "https://content.dropboxapi.com/2/files";
    private const int ChunkSize = 8 * 1024 * 1024;
    private const long SingleRequestLimit = 150L * 1024 * 1024;
    private const long MaxFileSize = 2_199_019_061_248L;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string ProviderId => DropboxConnectionService.ProviderId;

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
        if (file.SizeBytes < 0 || file.SizeBytes > MaxFileSize)
            throw new InvalidOperationException("The CRM file exceeds Dropbox's maximum supported upload size.");

        var protector = services.GetRequiredService<ISecretProtector>();
        if (!protector.TryUnprotect(connection.ProtectedRefreshToken, out var refreshToken, out _))
            throw new InvalidOperationException("Dropbox refresh token could not be decrypted; reconnect this tenant.");
        var config = dropboxOptions.Value;
        using var tokenResponse = await clients
            .CreateClient("dropbox-oauth")
            .PostAsync(
                TokenEndpoint,
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
        await EnsureSuccessAsync(tokenResponse, "refreshing the Dropbox access token", ct);
        var token =
            await tokenResponse.Content.ReadFromJsonAsync<AccessTokenResponse>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Dropbox did not return an access token.");
        if (!string.IsNullOrWhiteSpace(token.RefreshToken))
        {
            connection.UpdateProtectedRefreshToken(protector.Protect(token.RefreshToken));
            await db.SaveChangesAsync(ct);
        }

        // Ya copiado: se sobrescribe su misma ruta. Nuevo: {marca}/{categoría}/{año}, sin pisar otro con el mismo nombre.
        var target = existing?.RemotePath is { Length: > 0 } knownPath
            ? new DropboxTarget(knownPath, "overwrite", Autorename: false)
            : new DropboxTarget(
                "/"
                    + CloudMirrorPath
                        .For(platform.Current.BrandName, file.FolderType, file.TaxYear, file.OriginalName)
                        .FullPath,
                "add",
                Autorename: true
            );
        var tempPath = Path.Combine(Path.GetTempPath(), $"taxvision-dropbox-{Guid.NewGuid():N}.tmp");
        try
        {
            await using var stream = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.None,
                128 * 1024,
                FileOptions.Asynchronous | FileOptions.DeleteOnClose
            );
            await storage.DownloadAsync(storageOptions.Value.MainBucket, file.ObjectKey, stream, ct);
            stream.Position = 0;
            var uploaded = await UploadAsync(token.AccessToken, target, file.SizeBytes, stream, ct);
            if (existing is not null)
                db.CloudFileCopies.Remove(existing);
            db.CloudFileCopies.Add(
                TenantCloudFileCopy.Create(
                    message.TenantId,
                    file.Id,
                    ProviderId,
                    uploaded.Id!,
                    message.ChecksumSha256,
                    uploaded.PathDisplay
                )
            );
            await db.SaveChangesAsync(ct);
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
                logger.LogWarning(ex, "Could not remove temporary Dropbox staging file.");
            }
        }
    }

    /// <summary>Ruta dentro de la carpeta de la app y cómo escribir: "add" (renombra si existe) u "overwrite".</summary>
    private sealed record DropboxTarget(string Path, string Mode, bool Autorename)
    {
        public DropboxCommitArg Commit() => new(Path, Mode, Autorename, true, false);
    }

    // La carpeta de la app es la raíz de Dropbox para esta app; las carpetas intermedias las crea Dropbox.
    private async Task<DropboxFileMetadata> UploadAsync(
        string accessToken,
        DropboxTarget target,
        long size,
        Stream content,
        CancellationToken ct
    )
    {
        if (size <= SingleRequestLimit)
        {
            using var request = CreateContentRequest("upload", accessToken, target.Commit());
            request.Content = new StreamContent(content);
            request.Content.Headers.ContentLength = size;
            using var response = await clients
                .CreateClient("dropbox-content")
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            await EnsureSuccessAsync(response, "uploading a file to Dropbox", ct);
            var metadata = await response.Content.ReadFromJsonAsync<DropboxFileMetadata>(cancellationToken: ct);
            return metadata is { Id.Length: > 0 }
                ? metadata
                : throw new InvalidOperationException("Dropbox did not return the uploaded file ID.");
        }

        var first = await ReadChunkAsync(content, ChunkSize, ct);
        using var start = CreateContentRequest("upload_session/start", accessToken, new UploadStartArg(false));
        start.Content = new ByteArrayContent(first);
        start.Content.Headers.ContentLength = first.Length;
        using var started = await clients
            .CreateClient("dropbox-content")
            .SendAsync(start, HttpCompletionOption.ResponseHeadersRead, ct);
        await EnsureSuccessAsync(started, "starting a Dropbox upload session", ct);
        var session = await started.Content.ReadFromJsonAsync<UploadSessionStartResponse>(cancellationToken: ct);
        if (string.IsNullOrWhiteSpace(session?.SessionId))
            throw new InvalidOperationException("Dropbox did not return an upload session ID.");

        long offset = first.Length;
        while (size - offset > ChunkSize)
        {
            var chunk = await ReadChunkAsync(content, ChunkSize, ct);
            using var append = CreateContentRequest(
                "upload_session/append_v2",
                accessToken,
                new UploadSessionAppendArg(new UploadSessionCursor(session.SessionId, offset), false)
            );
            append.Content = new ByteArrayContent(chunk);
            append.Content.Headers.ContentLength = chunk.Length;
            using var appended = await clients
                .CreateClient("dropbox-content")
                .SendAsync(append, HttpCompletionOption.ResponseHeadersRead, ct);
            await EnsureSuccessAsync(appended, "uploading a Dropbox file chunk", ct);
            offset += chunk.Length;
        }

        var finalChunk = await ReadChunkAsync(content, checked((int)(size - offset)), ct);
        using var finish = CreateContentRequest(
            "upload_session/finish",
            accessToken,
            new UploadSessionFinishArg(new UploadSessionCursor(session.SessionId, offset), target.Commit())
        );
        finish.Content = new ByteArrayContent(finalChunk);
        finish.Content.Headers.ContentLength = finalChunk.Length;
        using var finished = await clients
            .CreateClient("dropbox-content")
            .SendAsync(finish, HttpCompletionOption.ResponseHeadersRead, ct);
        await EnsureSuccessAsync(finished, "finishing the Dropbox upload", ct);
        var result = await finished.Content.ReadFromJsonAsync<DropboxFileMetadata>(cancellationToken: ct);
        return result is { Id.Length: > 0 }
            ? result
            : throw new InvalidOperationException("Dropbox did not return the uploaded file ID.");
    }

    private static HttpRequestMessage CreateContentRequest<TArg>(string route, string accessToken, TArg arg)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"{ContentEndpoint}/{route}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.TryAddWithoutValidation("Dropbox-API-Arg", JsonSerializer.Serialize(arg, JsonOptions));
        return request;
    }

    private static async Task<byte[]> ReadChunkAsync(Stream stream, int length, CancellationToken ct)
    {
        var buffer = new byte[length];
        var read = 0;
        while (read < length)
        {
            var count = await stream.ReadAsync(buffer.AsMemory(read, length - read), ct);
            if (count == 0)
                throw new EndOfStreamException("The CRM file ended before the Dropbox upload completed.");
            read += count;
        }
        return buffer;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string operation, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
            return;
        var body = await response.Content.ReadAsStringAsync(ct);
        string? summary = null;
        try
        {
            using var json = JsonDocument.Parse(body);
            if (json.RootElement.TryGetProperty("error_summary", out var value))
                summary = value.GetString();
            else if (json.RootElement.TryGetProperty("error", out var error))
                summary = error.ToString();
        }
        catch (JsonException) { }
        var detail = string.IsNullOrWhiteSpace(summary)
            ? string.Empty
            : $" Dropbox error: {summary[..Math.Min(summary.Length, 400)]}.";
        throw new HttpRequestException(
            $"Dropbox returned {(int)response.StatusCode} ({response.StatusCode}) while {operation}.{detail}",
            null,
            response.StatusCode
        );
    }

    private sealed record AccessTokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken
    );

    private sealed record DropboxFileMetadata(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("path_display")] string? PathDisplay
    );

    private sealed record UploadSessionStartResponse([property: JsonPropertyName("session_id")] string? SessionId);

    private sealed record DropboxCommitArg(
        [property: JsonPropertyName("path")] string Path,
        [property: JsonPropertyName("mode")] string Mode,
        [property: JsonPropertyName("autorename")] bool Autorename,
        [property: JsonPropertyName("mute")] bool Mute,
        [property: JsonPropertyName("strict_conflict")] bool StrictConflict
    );

    private sealed record UploadStartArg([property: JsonPropertyName("close")] bool Close);

    private sealed record UploadSessionCursor(
        [property: JsonPropertyName("session_id")] string SessionId,
        [property: JsonPropertyName("offset")] long Offset
    );

    private sealed record UploadSessionAppendArg(
        [property: JsonPropertyName("cursor")] UploadSessionCursor Cursor,
        [property: JsonPropertyName("close")] bool Close
    );

    private sealed record UploadSessionFinishArg(
        [property: JsonPropertyName("cursor")] UploadSessionCursor Cursor,
        [property: JsonPropertyName("commit")] DropboxCommitArg Commit
    );
}
