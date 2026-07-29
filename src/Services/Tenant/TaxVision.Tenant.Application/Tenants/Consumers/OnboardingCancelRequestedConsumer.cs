using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Tenant.Application.Tenants.Abstractions;
using TaxVision.Tenant.Domain.Enums;

namespace TaxVision.Tenant.Application.Tenants.Consumers;

/// <summary>PayFlow (Fase 17) — compensa un onboarding cancelado (admin cancel-and-refund, Auth)
/// que ya había creado el tenant. No-op si el paso Tenant nunca llegó a correr
/// (<see cref="OnboardingCancelRequestedIntegrationEvent.OnboardingTenantId"/> null).
/// <see cref="TaxVision.Tenant.Domain.Tenant.ChangeStatus"/> es idempotente hacia Closed — un
/// replay del evento no falla.</summary>
public static class OnboardingCancelRequestedConsumer
{
    public static async Task Handle(
        OnboardingCancelRequestedIntegrationEvent evt,
        ITenantRepository tenants,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<TaxVision.Tenant.Domain.Tenant> logger,
        CancellationToken ct
    )
    {
        if (evt.OnboardingTenantId is null)
            return;

        using (
            correlation.Push(
                string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId
            )
        )
        {
            var tenant = await tenants.GetByOnboardingIdAsync(evt.OnboardingId, ct);
            if (tenant is null)
            {
                logger.LogWarning(
                    "OnboardingCancelRequested: no tenant found for onboarding {OnboardingId} (expected {TenantId}).",
                    evt.OnboardingId,
                    evt.OnboardingTenantId
                );
                return;
            }

            var result = tenant.ChangeStatus(EnumTenantStatus.TenantStatus.Closed);
            if (result.IsFailure)
            {
                logger.LogWarning(
                    "OnboardingCancelRequested: could not close tenant {TenantId} for onboarding {OnboardingId}: {Code}.",
                    tenant.Id,
                    evt.OnboardingId,
                    result.Error.Code
                );
                return;
            }

            await unitOfWork.SaveChangesAsync(ct);

            logger.LogInformation(
                "OnboardingCancelRequested: tenant {TenantId} for onboarding {OnboardingId} closed.",
                tenant.Id,
                evt.OnboardingId
            );
        }
    }
}
