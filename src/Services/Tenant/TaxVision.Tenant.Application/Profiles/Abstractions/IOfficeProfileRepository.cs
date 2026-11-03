using TaxVision.Tenant.Domain.Profiles;

namespace TaxVision.Tenant.Application.Profiles.Abstractions;

public interface IOfficeProfileRepository
{
    /// <summary>null = la oficina todavía no guardó perfil (no es un error: se responde vacío).</summary>
    Task<OfficeProfile?> GetAsync(Guid tenantId, CancellationToken ct = default);

    Task AddAsync(OfficeProfile profile, CancellationToken ct = default);

    /// <summary>Perfiles ya guardados (con versión), de todas las oficinas, en orden estable para paginar.</summary>
    Task<IReadOnlyList<OfficeProfile>> ListSavedAsync(int skip, int take, CancellationToken ct = default);

    /// <summary>De estos ids: cuáles son oficinas (no la de plataforma) y cuáles de ellas ya tienen perfil.</summary>
    Task<ImportCandidates> ClassifyImportCandidatesAsync(
        IReadOnlyCollection<Guid> tenantIds,
        CancellationToken ct = default
    );
}

public sealed record ImportCandidates(IReadOnlySet<Guid> Offices, IReadOnlySet<Guid> WithProfile)
{
    public static readonly ImportCandidates None = new(new HashSet<Guid>(), new HashSet<Guid>());
}
