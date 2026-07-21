namespace TaxVision.Notification.Domain.Preferences;

/// <summary>
/// Fase 5 del plan de notificaciones dinámicas: agrupación por categoría (no por tipo de
/// evento suelto — la industria recomienda 5-10 categorías, no decenas) para que el usuario
/// tenga una pantalla de preferencias manejable.
/// </summary>
public enum NotificationCategory
{
    /// <summary>Reset de password, alertas de login sospechoso, MFA — nunca apagable, ver <see cref="NotificationCategoryRules.IsLocked"/>.</summary>
    AccountSecurity,
    DocumentsAndSignatures,
    StorageAndQuota,
    Billing,
    Collaboration,
}
