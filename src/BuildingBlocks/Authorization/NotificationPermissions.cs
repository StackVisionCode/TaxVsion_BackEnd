namespace BuildingBlocks.Authorization;

/// <summary>
/// Permisos del servicio de notificaciones/email. Mismo patrón que
/// <see cref="CloudStoragePermissions"/>: claves punteadas en minúsculas usadas como
/// claim "perm" en el JWT y como policy en los endpoints.
/// </summary>
public static class NotificationPermissions
{
    // Envío y historial de correos
    public const string EmailSend = "notification.email.send";
    public const string EmailView = "notification.email.view";

    // Plantillas y layouts
    public const string TemplateView = "notification.template.view";
    public const string TemplateManage = "notification.template.manage";
    public const string LayoutManage = "notification.layout.manage";

    // Campañas
    public const string CampaignView = "notification.campaign.view";
    public const string CampaignManage = "notification.campaign.manage";

    // Auditoría/logs
    public const string LogView = "notification.log.view";

    // Suscripción a alertas de oficina (Fase 4, dos llaves): entre quienes tienen la capacidad,
    // quiénes reciben esa alerta. Staff delegables, no peligrosos, fuera del bundle automático del
    // TenantAdmin (AllowedActorTypes = TenantEmployee). El plural es deliberado (ver plan D4/T4.3).
    public const string BillingAlerts = "notifications.billing.alerts";
    public const string StorageAlerts = "notifications.storage.alerts";
    public const string SecurityTenantAlerts = "notifications.security.tenant_alerts";
    public const string MailboxAlerts = "notifications.mailbox.alerts";
}
