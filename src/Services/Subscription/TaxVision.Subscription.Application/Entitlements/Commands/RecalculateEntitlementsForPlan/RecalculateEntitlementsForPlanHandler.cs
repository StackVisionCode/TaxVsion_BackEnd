using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlements;
using Wolverine;

namespace TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlementsForPlan;

public static class RecalculateEntitlementsForPlanHandler
{
    // Enumera los tenants del plan por keyset y encola un RecalculateEntitlementsCommand por tenant.
    // Ese comando no va a RabbitMQ (cola local durable) → cada tenant se procesa en su propia
    // transacción con retry+DLQ, sin saturar el bus. Re-ejecutarlo es seguro: el recálculo per-tenant
    // es un upsert idempotente por RevisionNumber.
    public static async Task<Result<int>> Handle(
        RecalculateEntitlementsForPlanCommand command,
        ISubscriptionRepository subscriptions,
        IMessageBus bus,
        ILogger<RecalculateEntitlementsForPlanCommand> logger,
        CancellationToken ct
    )
    {
        var batchSize = command.BatchSize <= 0 ? 200 : command.BatchSize;
        var cursor = Guid.Empty;
        var dispatched = 0;

        while (true)
        {
            var tenantIds = await subscriptions.GetTenantIdsByPlanAsync(command.PlanId, cursor, batchSize, ct);
            if (tenantIds.Count == 0)
                break;

            foreach (var tenantId in tenantIds)
            {
                await bus.PublishAsync(new RecalculateEntitlementsCommand(tenantId));
                dispatched++;
            }

            cursor = tenantIds[^1];
            if (tenantIds.Count < batchSize)
                break;
        }

        logger.LogInformation(
            "Mass entitlement recalculation queued for plan {PlanId}: {Count} tenant(s).",
            command.PlanId,
            dispatched
        );
        return Result.Success(dispatched);
    }
}
