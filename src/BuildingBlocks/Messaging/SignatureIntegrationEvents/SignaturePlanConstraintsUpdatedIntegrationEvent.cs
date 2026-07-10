namespace BuildingBlocks.Messaging.SignatureIntegrationEvents;

/// <summary>
/// Emitido cuando un platform admin modifica las restricciones de plan de un tenant via
/// PUT /admin/tenants/{tenantId}/signature-constraints.
///
/// Consumidores previstos:
///
///   1. [Notification Service — PENDIENTE DE IMPLEMENTAR]
///      Informa al tenant admin que sus capacidades de firma cambiaron (p.ej. "su plan
///      ahora permite hasta 100 MB por PDF" o "el canal WhatsApp ha sido habilitado para
///      su organización"). Template sugerido: "signature.plan.upgraded.v1" /
///      "signature.plan.downgraded.v1"
///
///   2. [Billing / Subscription Service — PENDIENTE DE IMPLEMENTAR]
///      Reconcilia las capacidades efectivas del tenant con el plan contratado. Útil para
///      detectar discrepancias entre lo que se factura y lo que se entregó, y para
///      activar o desactivar funcionalidades según cambios de suscripción.
/// </summary>
public sealed record SignaturePlanConstraintsUpdatedIntegrationEvent : IntegrationEvent
{
    /// <summary>Tenant al que se aplicaron las nuevas restricciones.</summary>
    public required Guid TenantId { get; init; }

    /// <summary>UserId del platform admin que realizó el ajuste (del JWT).</summary>
    public required Guid ChangedByUserId { get; init; }

    /// <summary>Momento en que las restricciones fueron persistidas.</summary>
    public required DateTime UpdatedAtUtc { get; init; }
}
