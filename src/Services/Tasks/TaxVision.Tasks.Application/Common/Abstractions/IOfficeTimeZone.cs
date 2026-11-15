using BuildingBlocks.TimeZones;

namespace TaxVision.Tasks.Application.Common.Abstractions;

/// <summary>Zona de la oficina, para convertir fechas civiles (vencimientos) en instantes.</summary>
public interface IOfficeTimeZone
{
    Task<ResolvedTimeZone> ResolveAsync(Guid tenantId, CancellationToken ct);
}
