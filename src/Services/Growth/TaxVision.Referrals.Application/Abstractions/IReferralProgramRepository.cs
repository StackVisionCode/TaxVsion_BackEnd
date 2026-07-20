using TaxVision.Referrals.Domain.Programs;

namespace TaxVision.Referrals.Application.Abstractions;

public interface IReferralProgramRepository
{
    /// <summary>
    /// Exact owner lookup for administrative mutations. Implementations must require
    /// the active tenant to equal <paramref name="ownerTenantId"/> and must not elevate
    /// across tenant filters.
    /// </summary>
    Task<ReferralProgram?> GetOwnedByIdAsync(
        Guid ownerTenantId,
        Guid programId,
        CancellationToken ct = default
    );

    /// <summary>
    /// Resolución explícita de un catálogo platform-owned o tenant-owned. Para programas
    /// platform la implementación eleva solo esta consulta por Id; no desactiva filtros
    /// para operaciones genéricas ni devuelve colecciones cross-tenant.
    /// </summary>
    Task<ReferralProgram?> GetForEvaluationAsync(Guid programId, CancellationToken ct = default);

    Task AddAsync(ReferralProgram program, CancellationToken ct = default);
}
