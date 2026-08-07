using BuildingBlocks.Results;

namespace TaxVision.Auth.Application.Onboarding.Abstractions;

public sealed record ActivateSubscriptionForOnboardingRequest(Guid OnboardingId, Guid TenantId, Guid PlanId);

/// <summary>PayFlow (Fase 15) — dispara la activación asíncrona de la suscripción
/// (<c>POST internal/subscriptions/activate-from-onboarding</c>, Fase 16), directamente en
/// <c>Active</c> (no <c>Trialing</c>). <c>SubscriptionActivatedForOnboardingIntegrationEvent</c>
/// (publicado por Subscription) es la señal real que la Saga espera.</summary>
public interface ISubscriptionActivationClient
{
    Task<Result> ActivateAsync(ActivateSubscriptionForOnboardingRequest request, CancellationToken ct = default);
}
