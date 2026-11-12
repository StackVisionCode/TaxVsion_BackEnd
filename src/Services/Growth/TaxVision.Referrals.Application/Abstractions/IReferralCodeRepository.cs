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

    /// <summary>
    /// ¿Existe un código USABLE (activo y vigente) para ese programa y hash? Lookup exacto y elevado que devuelve solo un
    /// booleano —nunca el código, su dueño ni sus datos— para la validación pública previa al alta.
    /// </summary>
    Task<bool> ExistsUsableByHashAsync(Guid programId, string codeHash, DateTime nowUtc, CancellationToken ct = default);

    /// <summary>Código por Id, restringido al tenant dueño (el tenant activo debe coincidir). Para registrar envíos del propio código.</summary>
    Task<ReferralCode?> GetOwnedByIdAsync(Guid ownerTenantId, Guid codeId, CancellationToken ct = default);

    Task AddAsync(ReferralCode referralCode, CancellationToken ct = default);
}
