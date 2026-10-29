using BuildingBlocks.Domain;

namespace TaxVision.Notification.Domain.Directory;

public sealed class ClientFormNotificationSettingsProjection : TenantEntity
{
    private ClientFormNotificationSettingsProjection() { }

    public bool PushNotificationsEnabled { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static ClientFormNotificationSettingsProjection Create(Guid tenantId, bool pushEnabled, DateTime nowUtc)
    {
        var row = new ClientFormNotificationSettingsProjection();
        row.SetTenant(tenantId);
        row.Reconcile(pushEnabled, nowUtc);
        return row;
    }

    // El comando más viejo no pisa al más nuevo: la cola no garantiza orden.
    public void Reconcile(bool pushEnabled, DateTime occurredOnUtc)
    {
        if (occurredOnUtc < UpdatedAtUtc)
            return;
        PushNotificationsEnabled = pushEnabled;
        UpdatedAtUtc = occurredOnUtc;
    }
}
