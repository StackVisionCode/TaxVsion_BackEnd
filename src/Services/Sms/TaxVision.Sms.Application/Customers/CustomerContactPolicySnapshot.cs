using TaxVision.Sms.Application.Customers.Abstractions;
using TaxVision.Sms.Domain.Customers;

namespace TaxVision.Sms.Application.Customers;

/// <summary>
/// Reconciliación desde el snapshot de Customer, con las reglas de Notification: un Customer que no manda
/// la política no toca nada, y una fila que un evento actualizó después de pedir la página manda.
/// </summary>
public static class CustomerContactPolicySnapshot
{
    public static async Task<bool> ApplyAsync(
        ICustomerContactPolicyRepository repository,
        RemoteCustomerContactPolicy contact,
        DateTime snapshotAtUtc,
        CancellationToken ct
    )
    {
        if (contact.SmsEnabled is not { } smsEnabled)
            return false;

        var existing = await repository.GetByCustomerIdAsync(contact.TenantId, contact.CustomerId, ct);
        if (existing is null)
        {
            // Si el consumer la inserta a la vez, el índice único rechaza esta y gana la del evento.
            await repository.AddAsync(
                CustomerContactPolicyProjection.Create(contact.TenantId, contact.CustomerId, smsEnabled),
                ct
            );
            return true;
        }

        return await repository.ReconcileIfOlderAsync(
            contact.TenantId,
            contact.CustomerId,
            smsEnabled,
            snapshotAtUtc,
            ct
        );
    }
}
