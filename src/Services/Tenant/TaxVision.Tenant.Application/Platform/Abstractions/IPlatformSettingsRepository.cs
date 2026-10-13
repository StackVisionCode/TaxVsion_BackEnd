using TaxVision.Tenant.Domain.Platform;

namespace TaxVision.Tenant.Application.Platform.Abstractions;

/// <summary>La única fila de <see cref="PlatformSettings"/>. No es de ningún tenant: sin filtro.</summary>
public interface IPlatformSettingsRepository
{
    Task<PlatformSettings?> GetAsync(CancellationToken ct = default);

    Task AddAsync(PlatformSettings settings, CancellationToken ct = default);
}
