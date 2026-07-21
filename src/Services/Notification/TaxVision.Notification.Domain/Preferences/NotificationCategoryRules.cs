namespace TaxVision.Notification.Domain.Preferences;

/// <summary>
/// La categoría "Cuenta y seguridad" nunca se puede apagar (protege al propio usuario y a la
/// plataforma) — se muestra en la pantalla de preferencias con el interruptor deshabilitado,
/// no se oculta, mismo criterio que <c>TermsAcceptance</c> en Auth.
/// </summary>
public static class NotificationCategoryRules
{
    public static bool IsLocked(NotificationCategory category) => category == NotificationCategory.AccountSecurity;
}
