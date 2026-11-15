using BuildingBlocks.TenantTimeZones;
using BuildingBlocks.TimeZones;
using TaxVision.Billing.Application.Abstractions;

namespace TaxVision.Billing.Infrastructure.TimeZones;

/// <summary>Lee la zona de la proyección compartida; sin proyección, el resolver ya devuelve UTC marcado.</summary>
internal sealed class OfficeTimeZone(ITenantTimeZoneResolver resolver) : IOfficeTimeZone
{
    public Task<ResolvedTimeZone> ResolveAsync(Guid tenantId, CancellationToken ct) =>
        resolver.ResolveAsync(tenantId, ct);
}
