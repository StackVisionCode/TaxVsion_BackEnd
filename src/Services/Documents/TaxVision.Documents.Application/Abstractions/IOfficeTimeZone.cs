using BuildingBlocks.TimeZones;

namespace TaxVision.Documents.Application.Abstractions;

/// <summary>Zona de la oficina que paga, para su recibo (P7); sin proyección, UTC marcado.</summary>
public interface IOfficeTimeZone
{
    Task<ResolvedTimeZone> ResolveAsync(Guid tenantId, CancellationToken ct);
}
