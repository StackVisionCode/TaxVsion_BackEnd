using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Application.Onboarding.Consumers;

/// <summary>PayFlow (Fase 17) — compensa un onboarding cancelado (admin cancel-and-refund,
/// <c>CancelAndRefundOnboardingAdminHandler</c>) que ya había creado el usuario dueño del
/// tenant. No-op si el paso TenantAdmin nunca llegó a correr
/// (<see cref="OnboardingCancelRequestedIntegrationEvent.OnboardingUserId"/> null).</summary>
public static class OnboardingCancelRequestedConsumer
{
    public static async Task Handle(
        OnboardingCancelRequestedIntegrationEvent evt,
        IUserRepository users,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<User> logger,
        CancellationToken ct
    )
    {
        if (evt.OnboardingUserId is null)
            return;

        using (
            correlation.Push(
                string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId
            )
        )
        {
            var user = await users.GetByOnboardingIdAsync(evt.OnboardingId, ct);
            if (user is null)
            {
                logger.LogWarning(
                    "OnboardingCancelRequested: no user found for onboarding {OnboardingId} (expected {UserId}).",
                    evt.OnboardingId,
                    evt.OnboardingUserId
                );
                return;
            }

            if (!user.IsActive)
                return;

            user.Deactivate(DateTime.UtcNow);
            await unitOfWork.SaveChangesAsync(ct);

            logger.LogInformation(
                "OnboardingCancelRequested: user {UserId} for onboarding {OnboardingId} deactivated.",
                user.Id,
                evt.OnboardingId
            );
        }
    }
}
