using BuildingBlocks.Results;

namespace TaxVision.Auth.Application.Abstractions;

/// <summary>
/// Sube la foto de perfil ya normalizada al bucket temporal de MinIO con la cuenta IAM propia de Auth.
/// Solo sube: el pedido de catálogo a CloudStorage (SaveFileRequested) lo publica el handler después
/// de guardar la foto pendiente, para que el resultado del antivirus nunca llegue antes que la fila.
/// </summary>
public interface IUserAvatarStorage
{
    Task<Result<StoredUserAvatar>> StoreAsync(
        Guid tenantId,
        Guid userId,
        Guid fileId,
        byte[] content,
        string contentType,
        CancellationToken ct = default
    );
}

/// <summary>Dónde quedó el objeto en el bucket temporal; CloudStorage lo copia desde ahí.</summary>
public sealed record StoredUserAvatar(Guid FileId, string SourceBucket, string SourceObjectKey);
