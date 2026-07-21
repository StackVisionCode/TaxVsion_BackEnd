using TaxVision.Referrals.Domain.Codes;
using TaxVision.Referrals.Domain.Participants;

namespace TaxVision.Referrals.Application.Abstractions;

public interface IReferralCodeRepository
{
    /// <summary>
    /// Exact owner lookup used before issuing a new code. It is tenant-bound and must
    /// never elevate to another owner tenant.
    /// </summary>
    Task<ReferralCode?> GetActiveOwnedAsync(
        Guid ownerTenantId,
        Guid programId,
        ReferralParticipantType ownerType,
        Guid ownerId,
        CancellationToken ct = default
    );

    /// <summary>
    /// Lookup exacto y explícitamente elevado por ProgramId+hash. Es necesario porque
    /// un código T2T pertenece al tenant referrer y se canjea bajo el tenant referee.
    /// Nunca debe ofrecer búsqueda parcial, listado ni el token completo.
    /// </summary>
    Task<ReferralCode?> ResolveByHashAsync(Guid programId, string codeHash, CancellationToken ct = default);

    Task AddAsync(ReferralCode referralCode, CancellationToken ct = default);
}
