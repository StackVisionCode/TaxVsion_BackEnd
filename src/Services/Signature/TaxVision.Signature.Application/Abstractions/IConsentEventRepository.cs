using TaxVision.Signature.Domain.Consents;

namespace TaxVision.Signature.Application.Abstractions;

public interface IConsentEventRepository
{
    Task AddAsync(ConsentEvent evt, CancellationToken ct = default);

    /// <summary>Devuelve el último consent aceptado por el firmante en la solicitud (para audit).</summary>
    Task<ConsentEvent?> GetLatestForSignerAsync(
        Guid tenantId,
        Guid signatureRequestId,
        Guid signerId,
        CancellationToken ct = default
    );
}
