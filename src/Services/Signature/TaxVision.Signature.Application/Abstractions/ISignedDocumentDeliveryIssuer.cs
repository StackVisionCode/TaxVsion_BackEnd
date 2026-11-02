using BuildingBlocks.Results;
using TaxVision.Signature.Domain.Delivery;
using TaxVision.Signature.Domain.Requests;

namespace TaxVision.Signature.Application.Abstractions;

/// <summary>Entrega lista para avisar: el token plano solo vive en memoria hasta cifrarlo en el evento.</summary>
public sealed record IssuedDelivery(SignedDocumentDelivery Delivery, DeliveryToken Token, bool IsReissue);

/// <summary>
/// Emite las entregas de documentos firmados (una por firmante, con todos sus archivos). Dos pasos para
/// respetar "guardar antes de publicar": <c>Prepare*</c> agrega al contexto (el caller guarda) y
/// <see cref="PublishAsync"/> avisa a Notification después del commit.
/// </summary>
public interface ISignedDocumentDeliveryIssuer
{
    /// <summary>Paquete final; omite a los firmantes que ya tienen su entrega (idempotente).</summary>
    Task<IReadOnlyList<IssuedDelivery>> PrepareCompletedAsync(SignatureRequest request, CancellationToken ct);

    Task<Result<IssuedDelivery>> PreparePartialCopyAsync(
        SignatureRequest request,
        Signer signer,
        IReadOnlyList<DeliveryItemSpec> items,
        CancellationToken ct
    );

    /// <summary>Rota el enlace (el anterior deja de valer) y lo vuelve a mandar al mismo email.</summary>
    Task<Result<IssuedDelivery>> PrepareReissueAsync(
        SignedDocumentDelivery delivery,
        Guid? byUserId,
        CancellationToken ct
    );

    Task PublishAsync(IssuedDelivery issued, SignatureRequest request, CancellationToken ct);
}
