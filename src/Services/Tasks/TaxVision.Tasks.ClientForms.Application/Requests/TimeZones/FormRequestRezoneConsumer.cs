using BuildingBlocks.Messaging.TenantIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.TimeZones;
using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.ClientForms.Application.Requests.TimeZones;

/// <summary>Formularios abiertos con fecha civil de vencimiento.</summary>
public interface IFormRequestRezoneStore
{
    Task<IReadOnlyList<FormRequest>> OpenWithDueAsync(Guid tenantId, CancellationToken ct);
}

/// <summary>P9: un formulario abierto que vencía al fin del día en la zona anterior pasa al fin del mismo día en la nueva.</summary>
public static class FormRequestRezoneConsumer
{
    public static async Task Handle(
        TenantTimeZoneChangedIntegrationEvent evt,
        IFormRequestRezoneStore store,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var newZone = ResolvedTimeZone.Resolve(evt.NewTimeZoneId);
        var changed = 0;
        foreach (var request in await store.OpenWithDueAsync(evt.ChangedTenantId, ct))
            changed += request.RezoneCivilDue(evt.PreviousTimeZoneId, newZone) ? 1 : 0;

        if (changed > 0)
            await unitOfWork.SaveChangesAsync(ct);
    }
}
