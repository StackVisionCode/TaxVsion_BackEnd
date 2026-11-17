using TaxVision.Referrals.Domain.Fraud;

namespace TaxVision.Referrals.Application.Abstractions;

/// <summary>Identidad del dueño de cada oficina (HMAC del email y si es desechable), tomada de la creación de la oficina.</summary>
public interface IOfficeOwnerSignalsReader
{
    /// <summary>Null si Growth aún no recibió la creación de esa oficina o su email no se pudo leer.</summary>
    Task<OfficeOwnerSignals?> ForOfficeAsync(Guid officeTenantId, CancellationToken ct = default);
}

public interface IReferralFraudReviewRepository
{
    /// <summary>Lookup exacto por Id, solo para el PlatformAdmin (cruza tenants).</summary>
    Task<ReferralFraudReview?> GetByIdAsync(Guid reviewId, CancellationToken ct = default);

    Task AddAsync(ReferralFraudReview review, CancellationToken ct = default);
}
