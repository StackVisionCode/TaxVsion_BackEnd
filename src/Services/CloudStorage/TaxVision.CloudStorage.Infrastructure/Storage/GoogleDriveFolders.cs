using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using TaxVision.CloudStorage.Domain.Providers;
using TaxVision.CloudStorage.Infrastructure.Persistence;

namespace TaxVision.CloudStorage.Infrastructure.Storage;

/// <summary>
/// Carpetas de la copia en Google Drive, que trabaja por id: cada nivel se toma de la caché, si no se busca entre
/// las carpetas que creó la app (permiso drive.file) y si no se crea. Dropbox y OneDrive crean la ruta solos.
/// </summary>
internal sealed class GoogleDriveFolders(CloudStorageDbContext db, HttpClient client, string accessToken)
{
    private const string Provider = GoogleDriveConnectionService.ProviderId;
    private const string FolderMimeType = "application/vnd.google-apps.folder";
    private const string FilesEndpoint = "https://www.googleapis.com/drive/v3/files";

    /// <summary>Id de la última carpeta de la ruta, creando las que falten.</summary>
    public async Task<string> ResolveAsync(Guid tenantId, IReadOnlyList<string> folders, CancellationToken ct)
    {
        var parentId = "root";
        var path = string.Empty;
        foreach (var name in folders)
        {
            path = path.Length == 0 ? name : $"{path}/{name}";
            parentId =
                await KnownAsync(tenantId, path, ct) ?? await FindOrCreateAsync(tenantId, path, name, parentId, ct);
        }
        return parentId;
    }

    /// <summary>El usuario borró o movió una carpeta: se olvidan todas y la próxima subida las vuelve a resolver.</summary>
    public async Task ForgetAsync(Guid tenantId, CancellationToken ct)
    {
        var known = await db
            .CloudFolders.IgnoreQueryFilters()
            .Where(x => x.TenantId == tenantId && x.Provider == Provider)
            .ToListAsync(ct);
        db.CloudFolders.RemoveRange(known);
        await db.SaveChangesAsync(ct);
    }

    private Task<string?> KnownAsync(Guid tenantId, string path, CancellationToken ct) =>
        db
            .CloudFolders.IgnoreQueryFilters()
            .Where(x => x.TenantId == tenantId && x.Provider == Provider && x.Path == path)
            .Select(x => (string?)x.RemoteFolderId)
            .SingleOrDefaultAsync(ct);

    private async Task<string> FindOrCreateAsync(
        Guid tenantId,
        string path,
        string name,
        string parentId,
        CancellationToken ct
    )
    {
        var found = await FindAsync(name, parentId, ct);
        var folderId = found ?? await CreateAsync(name, parentId, ct);
        var row = TenantCloudFolder.Create(tenantId, Provider, path, folderId);
        db.CloudFolders.Add(row);
        try
        {
            await db.SaveChangesAsync(ct);
            return folderId;
        }
        catch (DbUpdateException)
        {
            // Otra subida la guardó primero (índice único): se usa la suya y se quita la que creamos de más.
            db.Entry(row).State = EntityState.Detached;
            var winner =
                await KnownAsync(tenantId, path, ct)
                ?? throw new InvalidOperationException("The Google Drive folder could not be saved.");
            if (found is null && winner != folderId)
                await TryDeleteAsync(folderId, ct);
            return winner;
        }
    }

    private async Task<string?> FindAsync(string name, string parentId, CancellationToken ct)
    {
        var query =
            $"name='{Escape(name)}' and '{Escape(parentId)}' in parents and mimeType='{FolderMimeType}' and trashed=false";
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{FilesEndpoint}?q={Uri.EscapeDataString(query)}&fields=files(id)&spaces=drive&pageSize=1"
        );
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<FileList>(cancellationToken: ct);
        return result?.Files?.FirstOrDefault()?.Id;
    }

    private async Task<string> CreateAsync(string name, string parentId, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{FilesEndpoint}?fields=id")
        {
            Content = JsonContent.Create(new FolderMetadata(name, FolderMimeType, [parentId])),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<FileRef>(cancellationToken: ct);
        return created?.Id ?? throw new InvalidOperationException("Google Drive did not return the folder ID.");
    }

    private async Task TryDeleteAsync(string folderId, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Delete,
                $"{FilesEndpoint}/{Uri.EscapeDataString(folderId)}"
            );
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var _ = await client.SendAsync(request, ct);
        }
        catch (HttpRequestException)
        {
            // Queda una carpeta vacía de más; no afecta a la copia.
        }
    }

    // Comillas y barras se escapan dentro de la consulta de Drive.
    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("'", "\\'");

    private sealed record FolderMetadata(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("mimeType")] string MimeType,
        [property: JsonPropertyName("parents")] string[] Parents
    );

    private sealed record FileRef([property: JsonPropertyName("id")] string? Id);

    private sealed record FileList([property: JsonPropertyName("files")] FileRef[]? Files);
}
