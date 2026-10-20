using BuildingBlocks.Domain;

namespace TaxVision.Notification.Domain.Directory;

/// <summary>
/// Proyección local de <c>Customer.ContactPolicy</c> (T7.3): decide si al cliente se le puede
/// enviar por cada canal. Hermana de <see cref="CustomerEmailDirectoryEntry"/>. Si no existe fila
/// para un cliente, el consumer la trata como "ambos ON" (opt-out) — alineado con el default del
/// VO en Customer-DB y con la ausencia de UI de preferencias tras T7.1.
/// </summary>
public sealed class CustomerContactPolicyProjection : TenantEntity
{
    private CustomerContactPolicyProjection() { }

    public Guid CustomerId { get; private set; }
    public bool EmailEnabled { get; private set; }
    public bool SmsEnabled { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static CustomerContactPolicyProjection Create(
        Guid tenantId,
        Guid customerId,
        bool emailEnabled,
        bool smsEnabled
    )
    {
        var row = new CustomerContactPolicyProjection
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            EmailEnabled = emailEnabled,
            SmsEnabled = smsEnabled,
            UpdatedAtUtc = DateTime.UtcNow,
        };
        row.SetTenant(tenantId);
        return row;
    }

    public void Reconcile(bool emailEnabled, bool smsEnabled)
    {
        EmailEnabled = emailEnabled;
        SmsEnabled = smsEnabled;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
