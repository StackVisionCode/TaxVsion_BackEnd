namespace BuildingBlocks.Messaging.SignatureIntegrationEvents;

/// <summary>
/// Emitido cuando un tenant admin actualiza su configuración de firma via
/// PUT /signature/settings.
///
/// Consumidores previstos:
///
///   1. [Audit Service — PENDIENTE DE IMPLEMENTAR]
///      Registra en el audit log central que el tenant admin modificó su configuración de
///      firma (canales, retención, límites, etc.). Útil para trazabilidad de cambios de
///      política y para revisiones de cumplimiento.
///
///   2. [Notification Service — PENDIENTE DE IMPLEMENTAR]
///      Envía al tenant admin un email de confirmación con las nuevas restricciones
///      activas, para que quede constancia y detecte cambios accidentales.
///      Template sugerido: "signature.settings.updated.v1"
/// </summary>
public sealed record SignatureSettingsUpdatedIntegrationEvent : IntegrationEvent
{
    /// <summary>Tenant cuya configuración fue actualizada.</summary>
    public required Guid TenantId { get; init; }

    /// <summary>UserId del tenant admin que realizó el cambio (del JWT).</summary>
    public required Guid ChangedByUserId { get; init; }

    /// <summary>Momento en que la configuración fue persistida.</summary>
    public required DateTime UpdatedAtUtc { get; init; }
}
