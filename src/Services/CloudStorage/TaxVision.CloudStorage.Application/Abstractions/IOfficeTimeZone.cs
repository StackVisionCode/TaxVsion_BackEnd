using BuildingBlocks.TimeZones;

namespace TaxVision.CloudStorage.Application.Abstractions;

/// <summary>Zona de la oficina (kit TenantTimeZones); sin proyección, UTC marcado.</summary>
public interface IOfficeTimeZone
{
    Task<ResolvedTimeZone> ResolveAsync(Guid tenantId, CancellationToken ct);
}
