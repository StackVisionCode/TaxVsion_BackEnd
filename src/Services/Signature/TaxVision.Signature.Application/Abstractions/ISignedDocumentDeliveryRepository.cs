using TaxVision.Signature.Domain.Delivery;

namespace TaxVision.Signature.Application.Abstractions;

public interface ISignedDocumentDeliveryRepository
{
    Task AddAsync(SignedDocumentDelivery delivery, CancellationToken ct = default);

    /// <summary>Público: lo llama la página de entrega sin sesión; el tenant sale de la propia entrega.</summary>
    Task<SignedDocumentDelivery?> GetByTokenHashAsync(string tokenHash, CancellationToken ct = default);

    Task<SignedDocumentDelivery?> GetByIdAsync(Guid tenantId, Guid deliveryId, CancellationToken ct = default);

    Task<IReadOnlyList<SignedDocumentDelivery>> ListByRequestAsync(
        Guid tenantId,
        Guid signatureRequestId,
        CancellationToken ct = default
    );

    Task<bool> HasCompletedDeliveryAsync(
        Guid tenantId,
        Guid signatureRequestId,
        Guid signerId,
        CancellationToken ct = default
    );
}
