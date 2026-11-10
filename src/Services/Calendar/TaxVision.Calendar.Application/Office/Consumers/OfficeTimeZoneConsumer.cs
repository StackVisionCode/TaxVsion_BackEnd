using BuildingBlocks.Messaging.TenantIntegrationEvents;
using BuildingBlocks.Persistence;
using TaxVision.Calendar.Application.Office.Abstractions;
using TaxVision.Calendar.Domain.Office;

namespace TaxVision.Calendar.Application.Office.Consumers;

/// <summary>La oficina cambió de zona: su horario se lee en la nueva. Ordenado por fecha del cambio.</summary>
public static class OfficeTimeZoneConsumer
{
    public static async Task Handle(
        TenantUpdatedIntegrationEvent evt,
        IOfficeScheduleRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var now = DateTime.UtcNow;
        var projection = await repository.GetAsync(evt.UpdatedTenantId, ct);
        if (projection is null)
        {
            projection = OfficeScheduleProjection.Create(evt.UpdatedTenantId, now);
            repository.Add(projection);
        }

        if (projection.ApplyTimeZone(evt.DefaultTimeZoneId, evt.UpdatedAtUtc, now))
            await unitOfWork.SaveChangesAsync(ct);
    }
}
