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

    public static CustomerContactPolicyProjection Create(Guid tenantId, Guid customerId, bool smsEnabled)
    {
        var row = new CustomerContactPolicyProjection
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            SmsEnabled = smsEnabled,
            UpdatedAtUtc = DateTime.UtcNow,
        };
        row.SetTenant(tenantId);
        return row;
    }

    public void Reconcile(bool smsEnabled)
    {
        SmsEnabled = smsEnabled;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
