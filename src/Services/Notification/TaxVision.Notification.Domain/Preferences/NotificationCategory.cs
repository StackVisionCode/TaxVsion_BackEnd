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

    /// <summary>
    /// Recordatorios personales (Reminder Fase 8). Categoría propia y no <c>Collaboration</c>: el
    /// usuario se los puso a sí mismo, así que apagarlos no le hace perder nada que otro le haya
    /// mandado — es exactamente la distinción que hace útil una pantalla de preferencias.
    /// </summary>
    Reminders,

    /// <summary>
    /// Lo que la firma le pide al cliente y el cliente todavía no mandó. Categoría propia y no
    /// <c>DocumentsAndSignatures</c>: el destinatario es el cliente, no el personal, y silenciarla no
    /// debe apagarle los avisos de firma que sí tiene que ver.
    /// </summary>
    ClientRequests,

    /// <summary>
    /// Citas: invitacion, cambio de hora, cancelacion y el aviso de que empieza. Categoria propia y no
    /// <c>Collaboration</c>: apagar el ruido de una tarea no puede dejar a alguien sin enterarse de que
    /// le movieron una reunion con un cliente.
    /// </summary>
    Calendar,
}
