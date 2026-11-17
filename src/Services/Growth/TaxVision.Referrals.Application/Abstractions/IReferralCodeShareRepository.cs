using TaxVision.Referrals.Domain.Shares;

namespace TaxVision.Referrals.Application.Abstractions;

public interface IReferralCodeShareRepository
{
    Task AddAsync(ReferralCodeShare share, CancellationToken ct = default);

    /// <summary>Código al que pertenece el envío, o null si no existe. Sin acotar al tenant activo: el envío es del
    /// referidor y lo consulta el alta del referido.</summary>
    Task<Guid?> FindReferralCodeIdAsync(Guid shareId, CancellationToken ct = default);
}
