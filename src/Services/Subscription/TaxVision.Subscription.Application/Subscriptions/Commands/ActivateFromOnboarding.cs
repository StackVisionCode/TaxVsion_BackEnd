using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlements;
using TaxVision.Subscription.Domain.Settings;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.Subscription.Application.Subscriptions.Commands;

public sealed record ActivateFromOnboardingCommand(Guid OnboardingId, Guid TenantId, Guid PlanId, BillingCycle BillingCycle);

/// <summary>
/// PayFlow (Fase 16) — receptor del <c>POST internal/subscriptions/activate-from-onboarding</c> que
/// la Saga de Auth (Fase 15, <c>Sagas/Commands/ActivateSubscriptionCommand.cs</c>) invoca. A
/// diferencia de <c>TenantCreatedConsumer</c> (trial automático), esta suscripción nace directo en
/// <c>Active</c> — el cliente ya pagó en PaymentApp antes de llegar acá. El billing cycle lo elige el
/// comprador en el onboarding y viaja por toda la cadena hasta acá (Monthly o Yearly); el fin de
/// período se deriva del ciclo (<c>CalculateNext</c>), no fijo a un mes.
/// </summary>
public static class ActivateFromOnboardingHandler
{
    public static async Task<Result> Handle(
        ActivateFromOnboardingCommand command,
        ISubscriptionRepository subscriptions,
        IPlanRepository plans,
        ISubscriptionTenantSettingsRepository settingsRepository,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var existing = await subscriptions.GetByOnboardingIdAsync(command.OnboardingId, ct);
        if (existing is not null)
            return Result.Success();

        var plan = await plans.GetByIdAsync(command.PlanId, ct);
        var planVersion = plan?.GetPublishedVersion();
        if (plan is null || planVersion is null)
            return Result.Failure(
                new Error("Subscription.Onboarding.PlanNotFound", "The requested plan is missing or unpublished.")
            );

        var nowUtc = DateTime.UtcNow;
        var createResult = TenantSubscription.ActivateImmediately(
            command.TenantId,
            plan,
            planVersion,
            command.BillingCycle,
            periodStartUtc: nowUtc,
            periodEndUtc: command.BillingCycle.CalculateNext(nowUtc),
            actorUserId: Guid.Empty,
            nowUtc,
            command.OnboardingId
        );
        if (createResult.IsFailure)
            return Result.Failure(createResult.Error);

        var subscription = createResult.Value;
        await subscriptions.AddAsync(subscription, ct);

        if (await settingsRepository.GetByTenantIdAsync(command.TenantId, ct) is null)
        {
            var settingsResult = SubscriptionTenantSettings.Default(command.TenantId, actorUserId: Guid.Empty, nowUtc);
            if (settingsResult.IsFailure)
                return Result.Failure(settingsResult.Error);

            await settingsRepository.AddAsync(settingsResult.Value, ct);
        }

        // Sellar el tenant real: este handler corre con un token M2M scoped a PlatformTenant.Id
        // (la Saga todavia no tiene sesion de usuario), asi que sin esto RecalculateEntitlements
        // vería el filtro fail-closed de RBAC Fase 5 — mismo motivo que TenantCreatedConsumer.
        bus.TenantId = command.TenantId.ToString();

        await bus.PublishAsync(
            new SubscriptionActivatedForOnboardingIntegrationEvent
            {
                TenantId = command.TenantId,
                OnboardingId = command.OnboardingId,
                CreatedSubscriptionId = subscription.Id,
                CorrelationId = correlation.CorrelationId,
            }
        );
        await bus.PublishAsync(new RecalculateEntitlementsCommand(command.TenantId));

        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}
