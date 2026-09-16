namespace BuildingBlocks.Messaging.AuthIntegrationEvents;

/// <summary>
/// El pago inicial de un onboarding pago-primero falló y Auth ya lo marcó como PaymentFailed. Auth
/// publica este evento (que SÍ lleva el email/nombre del comprador, tomados del aggregate) para que
/// Notification envíe el aviso "tu pago no pasó" con un link para reintentar. Es distinto del
/// <c>OnboardingPaymentFailedIntegrationEvent</c> de PaymentApp (que solo trae ids + motivo y no
/// alcanza para componer un email). <see cref="IntegrationEvent.TenantId"/> = PlatformTenant (el
/// tenant aún no existe); <see cref="OnboardingId"/> es la clave de correlación real.
/// </summary>
public sealed record OnboardingPaymentFailedNotificationRequestedIntegrationEvent : IntegrationEvent
{
    public required Guid OnboardingId { get; init; }
    public required string Email { get; init; }
    public required string FirstName { get; init; }

    /// <summary>Nullable — resuelto por <c>IPlanCatalogClient</c> antes de publicar; null si
    /// Subscription no responde. Notification cae a un fallback genérico ("tu plan").</summary>
    public string? PlanName { get; init; }

    /// <summary>Motivo legible del fallo (el <c>FailureReason</c> del evento de PaymentApp). Nullable:
    /// si falta, el email muestra solo el mensaje general sin la línea de motivo.</summary>
    public string? FailureReason { get; init; }

    /// <summary>Link para reintentar: la entrada del landing con el plan/ciclo preseleccionados.</summary>
    public required string RetryUrl { get; init; }
}
