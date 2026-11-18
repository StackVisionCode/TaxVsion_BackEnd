using BuildingBlocks.Domain;

namespace TaxVision.Sms.Domain.Customers;

/// <summary>
/// Proyección local de <c>Customer.ContactPolicy</c> para el canal SMS. Un cliente, una fila. Sin fila,
/// la política efectiva es "SMS encendido", igual que el default del VO en Customer.
/// </summary>
public sealed class CustomerContactPolicyProjection : TenantEntity
{
    private CustomerContactPolicyProjection() { }

    public Guid CustomerId { get; private set; }
    public bool SmsEnabled { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>
    /// <c>OccurredOn</c> del último evento de Customer aplicado. Ordena los eventos entre sí; nulo = aún ninguno.
    /// <see cref="UpdatedAtUtc"/> es reloj de pared (lo usa la reconciliación) y no sirve para esto.
    /// </summary>
    public DateTime? SourceOccurredOnUtc { get; private set; }

    public static CustomerContactPolicyProjection Create(
        Guid tenantId,
        Guid customerId,
        bool smsEnabled,
        DateTime? sourceOccurredOnUtc = null
    )
    {
        var row = new CustomerContactPolicyProjection
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            SmsEnabled = smsEnabled,
            UpdatedAtUtc = DateTime.UtcNow,
            SourceOccurredOnUtc = sourceOccurredOnUtc,
        };
        row.SetTenant(tenantId);
        return row;
    }

    /// <summary>Aplica un evento de Customer salvo que sea más viejo que el último aplicado; el igual se aplica.</summary>
    public bool ApplyIfNewer(DateTime occurredOnUtc, bool smsEnabled)
    {
        if (occurredOnUtc < SourceOccurredOnUtc)
            return false;

        Reconcile(smsEnabled);
        SourceOccurredOnUtc = occurredOnUtc;
        return true;
    }

    public void Reconcile(bool smsEnabled)
    {
        SmsEnabled = smsEnabled;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
