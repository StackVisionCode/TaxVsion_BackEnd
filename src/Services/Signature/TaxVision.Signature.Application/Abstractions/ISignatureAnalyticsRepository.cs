using TaxVision.Signature.Domain.Analytics;
using TaxVision.Signature.Domain.Requests;

namespace TaxVision.Signature.Application.Abstractions;

/// <summary>
/// Repositorio del read model <see cref="SignatureAnalyticsSnapshot"/>. Los consumers
/// invocan <see cref="GetOrCreateForDayAsync"/> como upsert idempotente para acumular
/// contadores; el read service usa las queries del <see cref="ISignatureAnalyticsReadService"/>.
/// </summary>
public interface ISignatureAnalyticsRepository
{
    Task<SignatureAnalyticsSnapshot> GetOrCreateForDayAsync(
        Guid tenantId,
        DateOnly day,
        SignatureCategory category,
        CancellationToken ct = default
    );
}
