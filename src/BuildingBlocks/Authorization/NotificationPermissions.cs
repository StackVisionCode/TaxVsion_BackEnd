namespace BuildingBlocks.Authorization;

/// <summary>
/// Permisos del servicio de notificaciones/email. Mismo patrón que
/// <see cref="CloudStoragePermissions"/>: claves punteadas en minúsculas usadas como
/// claim "perm" en el JWT y como policy en los endpoints.
/// </summary>
public static class NotificationPermissions
{
    // Configuración SMTP/API
    public const string SettingsManage = "notification.settings.manage";

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

    // Cuentas de correo externas y sincronización
    public const string AccountView = "notification.account.view";
    public const string AccountManage = "notification.account.manage";

    // Auditoría/logs
    public const string LogView = "notification.log.view";
}
