namespace BuildingBlocks.Messaging.PaymentAppIntegrationEvents;

/// <summary>
/// PayFlow (Fase 8) — el pago inicial de un onboarding pago-primero falló (declinado,
/// expirado, cancelado por el pagador). Auth (Fase 9) lo consume para marcar el
/// <c>TenantOnboarding</c> como <c>PaymentFailed</c>.
/// </summary>
public sealed record OnboardingPaymentFailedIntegrationEvent : IntegrationEvent
{
    public required Guid OnboardingId { get; init; }
    public required Guid SaaSPaymentId { get; init; }
    public required string FailureCode { get; init; }
    public required string FailureReason { get; init; }
}
