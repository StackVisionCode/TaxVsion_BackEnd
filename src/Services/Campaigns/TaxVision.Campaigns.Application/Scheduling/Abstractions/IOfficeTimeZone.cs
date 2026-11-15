using BuildingBlocks.TimeZones;

namespace TaxVision.Campaigns.Application.Scheduling.Abstractions;

/// <summary>Zona de la oficina para el ancla civil de los agendados (P10); sin proyección, UTC marcado.</summary>
public interface IOfficeTimeZone
{
    Task<ResolvedTimeZone> ResolveAsync(Guid tenantId, CancellationToken ct);
}
