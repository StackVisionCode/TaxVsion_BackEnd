using BuildingBlocks.TimeZones;

namespace TaxVision.Billing.Application.Abstractions;

/// <summary>Zona de la oficina para las fechas civiles de la factura (P6); sin proyección, UTC marcado.</summary>
public interface IOfficeTimeZone
{
    Task<ResolvedTimeZone> ResolveAsync(Guid tenantId, CancellationToken ct);
}
