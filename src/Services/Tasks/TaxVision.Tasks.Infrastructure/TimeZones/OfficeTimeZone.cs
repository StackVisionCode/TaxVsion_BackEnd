using BuildingBlocks.TenantTimeZones;
using BuildingBlocks.TimeZones;
using TaxVision.Tasks.Application.Common.Abstractions;

namespace TaxVision.Tasks.Infrastructure.TimeZones;

/// <summary>Lee la zona de la proyección compartida; sin proyección, el resolver ya devuelve UTC marcado.</summary>
internal sealed class OfficeTimeZone(ITenantTimeZoneResolver resolver)
    : IOfficeTimeZone,
        global::TaxVision.Tasks.ClientForms.Application.Common.Abstractions.IOfficeTimeZone
{
    public Task<ResolvedTimeZone> ResolveAsync(Guid tenantId, CancellationToken ct) =>
        resolver.ResolveAsync(tenantId, ct);
}
