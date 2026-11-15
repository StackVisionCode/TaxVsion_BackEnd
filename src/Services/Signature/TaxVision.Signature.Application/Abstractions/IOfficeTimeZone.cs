using BuildingBlocks.TimeZones;

namespace TaxVision.Signature.Application.Abstractions;

/// <summary>Zona de la oficina que se congela al enviar una solicitud (P8); sin proyección, UTC marcado.</summary>
public interface IOfficeTimeZone
{
    Task<ResolvedTimeZone> ResolveAsync(Guid tenantId, CancellationToken ct);
}
