using TaxVision.Notification.Application.Directory.Abstractions;
using TaxVision.Notification.Domain.Directory;

namespace TaxVision.Notification.Application.Directory;

/// <summary>
/// Backfill de la política de contacto desde el snapshot de Customer (P-21.5). Dos reglas para no
/// reactivar un email apagado: un Customer viejo que no manda la política no toca nada, y una fila que
/// un evento actualizó después de pedir la página manda sobre el snapshot (UPDATE condicional atómico).
/// </summary>
public static class CustomerContactPolicySnapshot
{
    public static async Task<bool> ApplyAsync(
        ICustomerContactPolicyRepository repository,
        ICustomerContactPolicySnapshotWriter writer,
        RemoteCustomerContact contact,
        DateTime snapshotAtUtc,
        CancellationToken ct
    )
    {
        if (contact.EmailEnabled is not { } emailEnabled || contact.SmsEnabled is not { } smsEnabled)
            return false;
        var existing = await repository.GetByCustomerIdAsync(contact.TenantId, contact.CustomerId, ct);
        if (existing is null)
        {
            // Si el consumer la inserta a la vez, el índice único rechaza esta y gana la del evento.
            await repository.AddAsync(
                CustomerContactPolicyProjection.Create(
                    contact.TenantId,
                    contact.CustomerId,
                    emailEnabled,
                    smsEnabled,
                    contact.Language ?? "En"
                ),
                ct
            );
            return true;
        }
        return await writer.ReconcileIfOlderAsync(
            contact.TenantId,
            contact.CustomerId,
            emailEnabled,
            smsEnabled,
            contact.Language ?? existing.Language,
            snapshotAtUtc,
            ct
        );
    }
}
