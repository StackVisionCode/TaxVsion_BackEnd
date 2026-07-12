using BuildingBlocks.Common;
using BuildingBlocks.Messaging;
using BuildingBlocks.Persistence;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Common;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Subscriptions;
using Wolverine;

namespace TaxVision.Subscription.Application.Subscriptions.IntegrationEvents;

public sealed class SubscriptionOptions
{
    public const string SectionName = "Subscriptions";

    public string DefaultPlanCode { get; set; } = PlanCatalog.Starter;
    public int TrialDays { get; set; } = 14;
}

/// <summary>
/// Alta de tenant ⇒ se crea la suscripción en período de prueba con el plan por
/// defecto y se publican los límites para que Auth los proyecte. Idempotente.
/// </summary>
public static class TenantCreatedConsumer
{
    public static async Task Handle(
        TenantCreatedIntegrationEvent evt,
        ISubscriptionRepository subscriptions,
        IPlanRepository plans,
        IOptions<SubscriptionOptions> options,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<TenantSubscription> logger,
        CancellationToken ct
    )
    {
        var correlationId = string.IsNullOrWhiteSpace(evt.CorrelationId)
            ? evt.EventId.ToString("N")
            : evt.CorrelationId;

        using (correlation.Push(correlationId))
        {
            // El tenant interno de plataforma no se suscribe.
            if (Enum.TryParse<TenantKind>(evt.Kind, true, out var kind) && kind == TenantKind.Platform)
            {
                return;
            }

            var existing = await subscriptions.GetByTenantIdAsync(evt.NewTenantId, ct);
            if (existing is not null)
            {
                logger.LogInformation(
                    "Subscription already exists for tenant {TenantId}; ignoring event.",
                    evt.NewTenantId
                );
                return;
            }

            var plan = await plans.GetByCodeAsync(options.Value.DefaultPlanCode, ct);
            if (plan is null)
            {
                logger.LogError(
                    "Default plan '{PlanCode}' not found; cannot create subscription for tenant {TenantId}.",
                    options.Value.DefaultPlanCode,
                    evt.NewTenantId
                );
                throw new InvalidOperationException($"Default plan '{options.Value.DefaultPlanCode}' is missing.");
            }

            var result = TenantSubscription.StartTrial(evt.NewTenantId, plan, options.Value.TrialDays);
            if (result.IsFailure)
                throw new InvalidOperationException(result.Error.Message);

            await subscriptions.AddAsync(result.Value, ct);
            await bus.PublishAsync(SubscriptionEventFactory.Activated(result.Value, plan, correlationId));
            await unitOfWork.SaveChangesAsync(ct);

            logger.LogInformation(
                "Trial subscription created for tenant {TenantId} on plan {PlanCode} until {TrialEnd}.",
                evt.NewTenantId,
                plan.Code,
                result.Value.TrialEndsAtUtc
            );
        }
    }
}
