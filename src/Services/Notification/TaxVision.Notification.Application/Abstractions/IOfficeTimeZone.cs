using BuildingBlocks.TimeZones;

namespace TaxVision.Notification.Application.Abstractions;

/// <summary>Zona de la oficina para contar días en su calendario; sin proyección, UTC marcado.</summary>
public interface IOfficeTimeZone
{
    Task<ResolvedTimeZone> ResolveAsync(Guid tenantId, CancellationToken ct);
}
