using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Results;
using TaxVision.Signature.Domain.Requests;

namespace TaxVision.Signature.Application.Abstractions;

/// <summary>
/// Publica una página de declaraciones de origen en su propia transacción con outbox: o salen todas o ninguna.
/// Lo usa el backfill, que recorre muchas solicitudes.
/// </summary>
public interface IFileSourceDeclarationPublisher
{
    Task<Result> PublishAsync(IReadOnlyList<FileSourceDeclaredIntegrationEvent> page, CancellationToken ct = default);
}

/// <summary>Originales de una solicitud (cualquier estado) con los clientes de sus firmantes, para el backfill.</summary>
public sealed record RequestOriginalFiles(
    Guid RequestId,
    IReadOnlyList<Guid> FileIds,
    IReadOnlyList<Guid?> MappedCustomerIds
);

/// <summary>Lectura liviana de los originales de todas las solicitudes del tenant.</summary>
public interface ISignatureOriginalFileQuery
{
    Task<IReadOnlyList<RequestOriginalFiles>> ListAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Solicitudes del tenant (solo lectura, con firmantes, documentos y copias) con algún original, sellado,
    /// certificado o copia en <paramref name="fileIds"/>.
    /// </summary>
    Task<IReadOnlyList<SignatureRequest>> ListOwningFilesAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> fileIds,
        CancellationToken ct = default
    );
}
