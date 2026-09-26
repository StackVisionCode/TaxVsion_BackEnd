namespace BuildingBlocks.Messaging.AuthIntegrationEvents;

/// <summary>
/// Solicitud de email de ciclo de vida de la suscripción (plan de Expiración/Dunning, Fase 3). Auth
/// consume <c>TenantSubscriptionStatusChangedIntegrationEvent</c> (que trae solo TenantId), resuelve el
/// email del admin/owner del tenant desde SUS datos y publica esto para que Notification lo renderice y
/// envíe — mismo patrón que <c>OnboardingPaymentFailedNotificationRequestedIntegrationEvent</c> (Auth es
/// el dueño del contacto; Notification no conoce usuarios). El <see cref="Status"/> elige el template.
/// </summary>
public sealed record TenantSubscriptionEmailRequestedIntegrationEvent : IntegrationEvent
{
    public required string Email { get; init; }
    public required string FirstName { get; init; }

    /// <summary>Estado nuevo de la suscripción (GracePeriod/Suspended/Expired/Active…) — decide el template.</summary>
    public required string Status { get; init; }

    /// <summary>Motivo (SubscriptionChangeReason stringificado).</summary>
    public required string Reason { get; init; }

    /// <summary>Nombre del plan, para el copy.</summary>
    public string? PlanName { get; init; }

    /// <summary>Fin de la ventana de gracia (cuando Status = GracePeriod) — "tu acceso se corta el {fecha}".</summary>
    public DateTime? GracePeriodEndsAtUtc { get; init; }

    /// <summary>Hasta cuándo llega el acceso ya pagado, cuando el tenant canceló al fin del período.</summary>
    public DateTime? AccessEndsAtUtc { get; init; }

    /// <summary>Código de fallo del proveedor, cuando la transición la disparó un pago fallido.</summary>
    public string? FailureCode { get; init; }

    /// <summary>URL a la que el email manda al admin para renovar/actualizar el pago (login de su oficina).</summary>
    public required string RenewUrl { get; init; }
}
