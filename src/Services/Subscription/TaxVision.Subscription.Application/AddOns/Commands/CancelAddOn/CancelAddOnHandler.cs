using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlements;
using TaxVision.Subscription.Domain.AddOns;
using Wolverine;

namespace TaxVision.Subscription.Application.AddOns.Commands.CancelAddOn;

public static class CancelAddOnHandler
{
    public static async Task<Result> Handle(
        CancelAddOnCommand command,
        ITenantAddOnRepository tenantAddOns,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<TenantAddOn> logger,
        CancellationToken ct
    )
    {
        var addOn = await tenantAddOns.GetByIdAsync(command.TenantAddOnId, command.TenantId, ct);
        if (addOn is null)
            return Result.Failure(new Error("AddOn.NotFound", "Add-on does not exist."));

        var result = addOn.CancelActive(command.Reason, command.RequestedByUserId, DateTime.UtcNow);
        if (result.IsFailure)
            return result;

        await bus.PublishAsync(new AddOnCancelledIntegrationEvent
        {
            TenantId = command.TenantId,
            TenantAddOnId = addOn.Id,
            AddOnCode = addOn.AddOnCode,
            Reason = command.Reason,
            CorrelationId = correlation.CorrelationId,
        });
        await unitOfWork.SaveChangesAsync(ct);

        await bus.InvokeAsync<Result>(new RecalculateEntitlementsCommand(command.TenantId), ct);

        logger.LogInformation("Add-on {AddOnCode} cancelled for tenant {TenantId}: {Reason}.", addOn.AddOnCode, command.TenantId, command.Reason);
        return Result.Success();
    }
}
