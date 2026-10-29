namespace TaxVision.Notification.Application.Abstractions;

/// <summary>
/// Métricas por `kind` × `channel` × `outcome` para el pipeline del dispatcher
/// (Fase 11 del plan de notificaciones). Permite medir en Prometheus:
///   - Cuántas notificaciones salen por canal (sent).
///   - Cuántas se suprimen por preferencia del usuario (suppressed_preference)
///     — crítico para distinguir "no se envió por bug" de "no se envió porque
///     el user lo apagó".
///   - Cuántas fallan en el proveedor externo (failed).
///   - Cuántas no tenían destinatario válido (no_recipient) — típico push.
///
/// No se taggea tenantId (alta cardinalidad y riesgo PII); el log estructurado
/// del dispatcher ya lo lleva para forense per-tenant. Para kind/channel ambos
/// vienen del catálogo (set acotado), seguro para labels de Prometheus.
/// </summary>
public interface INotificationDispatchMetrics
{
    /// <summary>Entrega exitosa confirmada por el provider.</summary>
    void RecordSent(string kind, string channel);

    /// <summary>El user tenía la combinación `category × channel` apagada en
    /// `UserNotificationPreference`. Mandatory nunca llega aquí (bypass D2).</summary>
    void RecordSuppressedByPreference(string kind, string channel);

    /// <summary>El provider (SMTP/SMS/Push) rechazó o falló. El log de la fila
    /// `NotificationLog` queda con status Failed y el error del provider.</summary>
    void RecordFailed(string kind, string channel);

    /// <summary>Sin destinatario válido: push sin devices registrados,
    /// SMS sin phone verificado, etc. No es un error de provider — es ausencia
    /// de surface para entregar.</summary>
    void RecordNoRecipient(string kind, string channel);

    void RecordClientFormPushFailure(string code) { }
}
