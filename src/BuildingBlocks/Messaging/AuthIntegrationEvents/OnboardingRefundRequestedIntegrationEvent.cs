namespace BuildingBlocks.Messaging.AuthIntegrationEvents;

/// <summary>
/// PayFlow (Fase 17) — publicado por <c>CancelAndRefundOnboardingHandler</c> (Auth,
/// <c>OnboardingAdminController</c>) tras una acción humana explícita con confirmación textual. Lleva
/// <see cref="PaymentId"/> directo (ya vive en <c>TenantOnboarding.PaymentId</c> desde Fase 8/9) en vez
/// de <see cref="OnboardingId"/> solo, para que PaymentApp no necesite ninguna capacidad nueva de
/// "buscar el pago de un onboarding" — <c>OnboardingRefundConsumer</c> carga el <c>SaaSPayment</c>
/// directo por Id y llama <c>IPaymentProvider.RefundAsync</c> + <c>SaaSPayment.RefundFull</c>, mismo
/// patrón que <c>RefundSaaSPaymentHandler</c> (admin refund manual ya existente).
/// </summary>
public sealed record OnboardingRefundRequestedIntegrationEvent : IntegrationEvent
{
    public required Guid OnboardingId { get; init; }
    public required Guid PaymentId { get; init; }
    public required string Reason { get; init; }
}
