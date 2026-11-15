using BuildingBlocks.TimeZones;
using TaxVision.Notification.Application.Abstractions;

namespace TaxVision.Notification.Tests.TestDoubles;

/// <summary>Zona fija de la oficina para las pruebas.</summary>
internal sealed class OfficeZoneStub(string timeZoneId) : IOfficeTimeZone
{
    public Task<ResolvedTimeZone> ResolveAsync(Guid tenantId, CancellationToken ct) =>
        Task.FromResult(ResolvedTimeZone.Resolve(timeZoneId));
}
