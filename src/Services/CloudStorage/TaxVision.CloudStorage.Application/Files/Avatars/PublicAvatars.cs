using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;

namespace TaxVision.CloudStorage.Application.Files.Avatars;

/// <summary>Lo mínimo para servir una foto de perfil: dónde está, qué tipo real tiene y su SHA-256 (ETag).</summary>
public sealed record PublicAvatarDescriptor(
    Guid FileId,
    string ObjectKey,
    string ContentType,
    string Sha256,
    long SizeBytes
);

/// <summary>
/// Búsqueda cross-tenant por fileId (GUID aleatorio y PK global) que SOLO devuelve archivos de la
/// carpeta Avatars, en estado Available y no borrados. Cualquier otro caso es null: el endpoint
/// anónimo responde el mismo 404 para "no existe" y "no es un avatar".
/// </summary>
public interface IPublicAvatarLookup
{
    Task<PublicAvatarDescriptor?> FindServableAsync(Guid fileId, CancellationToken ct);
}

/// <summary>Caché en memoria de aciertos (fileId → descriptor). Nunca guarda un "no encontrado".</summary>
public interface IPublicAvatarCache
{
    bool TryGet(Guid fileId, out PublicAvatarDescriptor? descriptor);
    void Set(PublicAvatarDescriptor descriptor);
    void Evict(Guid fileId);
}

/// <summary>
/// Resuelve la foto de perfil pública. Un avatar borrado deja de servirse cuando su entrada sale de la
/// caché: al instante en la réplica que procesa el FileDeleted (ver PublicAvatarCacheEvictionConsumer)
/// y, en el peor caso, al vencer el TTL de la caché (10 min) en las demás réplicas.
/// </summary>
public sealed class PublicAvatarReader(IPublicAvatarLookup lookup, IPublicAvatarCache cache)
{
    /// <summary>Tipos que el endpoint anónimo acepta servir: nunca SVG/HTML, aunque la carpeta lo admitiera.</summary>
    public static readonly IReadOnlySet<string> ServableContentTypes = new HashSet<string>(
        ["image/jpeg", "image/png", "image/webp"],
        StringComparer.OrdinalIgnoreCase
    );

    public async Task<PublicAvatarDescriptor?> ResolveAsync(Guid fileId, CancellationToken ct)
    {
        if (cache.TryGet(fileId, out var cached) && cached is not null)
            return cached;

        var found = await lookup.FindServableAsync(fileId, ct);
        if (found is null || !ServableContentTypes.Contains(found.ContentType))
            return null;

        cache.Set(found);
        return found;
    }

    /// <summary>Descarta la caché y vuelve a la BD (p.ej. el objeto ya no estaba en MinIO).</summary>
    public async Task<PublicAvatarDescriptor?> RevalidateAsync(Guid fileId, CancellationToken ct)
    {
        cache.Evict(fileId);
        return await ResolveAsync(fileId, ct);
    }
}

/// <summary>
/// CloudStorage recibe su propio FileDeleted/DMCA por el fanout (cola cloudstorage-events): se usa para
/// sacar el avatar de la caché de esta réplica sin acoplar los handlers de borrado a la caché.
/// </summary>
public static class PublicAvatarCacheEvictionConsumer
{
    public static void Handle(FileDeletedIntegrationEvent message, IPublicAvatarCache cache) =>
        cache.Evict(message.FileId);

    public static void Handle(FileBlockedByDmcaTakedownIntegrationEvent message, IPublicAvatarCache cache) =>
        cache.Evict(message.FileId);
}
