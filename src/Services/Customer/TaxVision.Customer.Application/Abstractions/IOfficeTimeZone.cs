using BuildingBlocks.TimeZones;

namespace TaxVision.Customer.Application.Abstractions;

/// <summary>Zona IANA de la oficina (proyección local); sin dato, UTC marcado como respaldo.</summary>
public interface IOfficeTimeZone
{
    Task<ResolvedTimeZone> ResolveAsync(Guid tenantId, CancellationToken ct);
}
