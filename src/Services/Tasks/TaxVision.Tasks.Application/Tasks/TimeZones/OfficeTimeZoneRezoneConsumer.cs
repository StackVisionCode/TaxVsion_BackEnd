using BuildingBlocks.Messaging.TenantIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.TimeZones;
using TaxVision.Tasks.Domain.ClientRequests;
using TaxVision.Tasks.Domain.Series;
using TaxVision.Tasks.Domain.Tasks;

namespace TaxVision.Tasks.Application.Tasks.TimeZones;

/// <summary>Lo abierto que puede tener un vencimiento de fecha civil en la zona anterior de la oficina.</summary>
public interface ICivilDueRezoneStore
{
    Task<IReadOnlyList<TaskItem>> OpenTasksWithCivilDuesAsync(
        Guid tenantId,
        IReadOnlyCollection<string> timeZoneIds,
        CancellationToken ct
    );
    Task<IReadOnlyList<ClientRequest>> OpenClientRequestsWithDueAsync(Guid tenantId, CancellationToken ct);

    /// <summary>Las series vivas (activas o pausadas) cuya regla está en alguna de las zonas.</summary>
    Task<IReadOnlyList<TaskSeries>> SeriesToRezoneAsync(
        Guid tenantId,
        IReadOnlyCollection<string> timeZoneIds,
        CancellationToken ct
    );
}

/// <summary>
/// P9: la oficina cambió de zona. Las tareas, las solicitudes al cliente y las series abiertas que vencían al fin del
/// día en la zona anterior pasan al fin del mismo día en la nueva. Repetir el evento no cambia nada más: lo ya movido
/// ya no está en la zona anterior.
/// </summary>
public static class OfficeTimeZoneRezoneConsumer
{
    public static async Task Handle(
        TenantTimeZoneChangedIntegrationEvent evt,
        ICivilDueRezoneStore store,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var newZone = ResolvedTimeZone.Resolve(evt.NewTimeZoneId);
        var zones = ZonesToMove(evt);
        var changed = 0;
        foreach (var task in await store.OpenTasksWithCivilDuesAsync(evt.ChangedTenantId, zones, ct))
            changed += zones.Count(zone => task.RezoneCivilDues(zone, newZone));
        foreach (var request in await store.OpenClientRequestsWithDueAsync(evt.ChangedTenantId, ct))
            changed += request.RezoneCivilDue(evt.PreviousTimeZoneId, newZone) ? 1 : 0;
        foreach (var series in await store.SeriesToRezoneAsync(evt.ChangedTenantId, zones, ct))
            changed += zones.Count(zone => series.RezoneCivilDue(zone, newZone));

        if (changed > 0)
            await unitOfWork.SaveChangesAsync(ct);
    }

    /// <summary>
    /// La zona anterior y, si la oficina no está en UTC, el respaldo UTC: lo guardado ahí se creó cuando la zona de la
    /// oficina aún no había llegado.
    /// </summary>
    private static IReadOnlyCollection<string> ZonesToMove(TenantTimeZoneChangedIntegrationEvent evt) =>
        evt.NewTimeZoneId == IanaTimeZone.UtcId || evt.PreviousTimeZoneId == IanaTimeZone.UtcId
            ? [evt.PreviousTimeZoneId]
            : [evt.PreviousTimeZoneId, IanaTimeZone.UtcId];
}
