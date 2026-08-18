namespace BuildingBlocks.Messaging.PaymentAppIntegrationEvents;

/// <summary>
/// PayFlow (Fase 8) — el pago inicial de un onboarding pago-primero se confirmó. Auth (Fase 9)
/// lo consume para generar el <c>RegistrationToken</c>. <see cref="IntegrationEvent.TenantId"/>
/// queda en <c>Guid.Empty</c> (el tenant todavía no existe) — <see cref="OnboardingId"/> es la
/// clave de correlación real de este evento.
/// </summary>
public sealed record OnboardingPaymentSucceededIntegrationEvent : IntegrationEvent
{
    public required Guid OnboardingId { get; init; }
    public required Guid SaaSPaymentId { get; init; }
    public required Guid PlanId { get; init; }
    public required long AmountPaidCents { get; init; }
    public required string Currency { get; init; }
    public required DateTime PaidAtUtc { get; init; }
    public required string ProviderPaymentReference { get; init; }
    public string? PaymentMethodMasked { get; init; }
}
