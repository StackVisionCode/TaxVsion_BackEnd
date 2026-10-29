using BuildingBlocks.Results;

namespace TaxVision.Customer.Application.Abstractions;

/// <summary>Dónde quedó la foto en el bucket temporal, para que CloudStorage la copie y la escanee.</summary>
public sealed record CustomerAvatarSourceObject(string Bucket, string ObjectKey);

/// <summary>
/// Sube los bytes de la foto ya normalizada al bucket temporal de MinIO con la cuenta IAM propia de
/// Customer. No publica nada: el handler guarda la pendiente y DESPUÉS pide el catálogo.
/// </summary>
public interface ICustomerAvatarStorage
{
    Task<Result<CustomerAvatarSourceObject>> StoreAsync(
        Guid tenantId,
        Guid fileId,
        byte[] content,
        string contentType,
        CancellationToken ct
    );
}
