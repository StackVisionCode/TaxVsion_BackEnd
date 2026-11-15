using BuildingBlocks.Messaging.TenantIntegrationEvents;
using BuildingBlocks.Persistence;
using TaxVision.Calendar.Domain.Office;

namespace TaxVision.Calendar.Application.Office.TimeZoneChanges;

/// <summary>
/// P9: registra el cambio de zona de la oficina; no mueve nada. Un evento repetido no duplica el registro. Sin horarios ni
/// series en la zona de origen no hay nada que decidir y no se registra: un pendiente así quedaría invisible y fijaría
/// un origen equivocado para el cambio siguiente.
/// </summary>
public static class OfficeTimeZoneChangeConsumer
{
    public static async Task Handle(
        TenantTimeZoneChangedIntegrationEvent evt,
        IOfficeTimeZoneChangeRepository changes,
        IOfficeTimeZoneCandidates candidates,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        if (await changes.ExistsForVersionAsync(evt.ChangedTenantId, evt.TenantVersion, ct))
            return;

        var pending = await changes.GetPendingAsync(evt.ChangedTenantId, ct);
        var now = DateTime.UtcNow;
        var from = pending?.FromTimeZoneId ?? evt.PreviousTimeZoneId;
        if (
            !await GetOfficeTimeZoneChangePreviewHandler.AnythingToMoveAsync(
                candidates,
                evt.ChangedTenantId,
                from,
                now,
                ct
            )
        )
        {
            if (pending is not null)
            {
                pending.Supersede(now);
                await unitOfWork.SaveChangesAsync(ct);
            }
            return;
        }

        var recorded = OfficeTimeZoneChange.Record(
            evt.ChangedTenantId,
            evt.PreviousTimeZoneId,
            evt.NewTimeZoneId,
            evt.ChangedAtUtc,
            evt.TenantVersion,
            pending,
            now
        );
        if (recorded.Value is { } change)
            changes.Add(change);
        if (recorded.Value is not null || pending is not null)
            await unitOfWork.SaveChangesAsync(ct);
    }
}
