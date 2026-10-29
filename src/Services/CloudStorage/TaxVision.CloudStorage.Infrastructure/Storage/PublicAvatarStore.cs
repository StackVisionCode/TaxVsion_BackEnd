using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using TaxVision.CloudStorage.Application.Files.Avatars;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Infrastructure.Persistence;

namespace TaxVision.CloudStorage.Infrastructure.Storage;

/// <summary>
/// Lookup del endpoint anónimo de avatares. Cross-tenant a propósito: la petición no trae JWT ni tenant
/// y el fileId es PK global (GUID aleatorio). El límite de seguridad son los filtros de abajo
/// (Avatars + Available + sin borrar), mismo criterio que GetByTokenHashAsync de los links públicos.
/// </summary>
public sealed class EfPublicAvatarLookup(CloudStorageDbContext db) : IPublicAvatarLookup
{
    public async Task<PublicAvatarDescriptor?> FindServableAsync(Guid fileId, CancellationToken ct)
    {
        var row = await db
            .Files.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(file =>
                file.Id == fileId
                && file.FolderType == FolderType.Avatars
                && file.Status == FileStatus.Available
                && file.SoftDeletedAtUtc == null
            )
            .Select(file => new
            {
                file.Id,
                file.ObjectKey,
                file.DetectedContentType,
                file.ChecksumSha256,
                file.SizeBytes,
            })
            .SingleOrDefaultAsync(ct);

        if (row is null || string.IsNullOrEmpty(row.DetectedContentType) || string.IsNullOrEmpty(row.ChecksumSha256))
            return null;

        return new PublicAvatarDescriptor(
            row.Id,
            row.ObjectKey,
            row.DetectedContentType,
            row.ChecksumSha256,
            row.SizeBytes
        );
    }
}

/// <summary>Caché de aciertos por réplica. TTL corto: es el tope de cuánto puede seguir sirviéndose un avatar borrado.</summary>
public sealed class MemoryPublicAvatarCache(IMemoryCache cache) : IPublicAvatarCache
{
    public static readonly TimeSpan TimeToLive = TimeSpan.FromMinutes(10);

    public bool TryGet(Guid fileId, out PublicAvatarDescriptor? descriptor) =>
        cache.TryGetValue(Key(fileId), out descriptor);

    public void Set(PublicAvatarDescriptor descriptor) =>
        cache.Set(
            Key(descriptor.FileId),
            descriptor,
            // Size=1: compatible con una IMemoryCache compartida que tenga SizeLimit.
            new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeToLive, Size = 1 }
        );

    public void Evict(Guid fileId) => cache.Remove(Key(fileId));

    private static string Key(Guid fileId) => $"cloudstorage:public-avatar:{fileId:N}";
}
