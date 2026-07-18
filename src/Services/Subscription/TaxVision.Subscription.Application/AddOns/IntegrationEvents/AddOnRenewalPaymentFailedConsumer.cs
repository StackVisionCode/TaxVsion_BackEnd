using BuildingBlocks.Common;
using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.AddOns;

namespace TaxVision.Subscription.Application.AddOns.IntegrationEvents;

public static class AddOnRenewalPaymentFailedConsumer
{
    public static async Task Handle(
        AddOnRenewalPaymentFailedIntegrationEvent evt,
        ITenantAddOnRepository tenantAddOns,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<TenantAddOn> logger,
        CancellationToken ct
    )
    {
        var correlationId = string.IsNullOrWhiteSpace(evt.CorrelationId)
            ? evt.EventId.ToString("N")
            : evt.CorrelationId;

        using (correlation.Push(correlationId))
        {
            var addOn = await tenantAddOns.GetByIdAsync(evt.TenantAddOnId, evt.TenantId, ct);
            if (addOn is null)
            {
                logger.LogWarning("AddOnRenewalPaymentFailed for unknown add-on {TenantAddOnId}.", evt.TenantAddOnId);
                return;
            }

            var renewal = FindRenewalByKey(addOn, evt.IdempotencyKey);
            if (renewal is null)
            {
                logger.LogWarning(
                    "AddOnRenewalPaymentFailed for {TenantAddOnId} has no matching renewal for key {Key}.",
                    evt.TenantAddOnId,
                    evt.IdempotencyKey
                );
                return;
            }

            var result = addOn.FailRenewal(
                renewal.Id,
                evt.FailureCode,
                evt.FailureReason,
                evt.WillRetry,
                evt.NextRetryAtUtc,
                actorUserId: Guid.Empty,
                DateTime.UtcNow
            );
            if (result.IsFailure)
            {
                logger.LogWarning(
                    "Could not record failed renewal for add-on {TenantAddOnId}: {Code}.",
                    addOn.Id,
                    result.Error.Code
                );
                return;
            }

            await unitOfWork.SaveChangesAsync(ct);
        }
    }

    private static TenantAddOnRenewal? FindRenewalByKey(TenantAddOn addOn, string idempotencyKey)
    {
        foreach (var renewal in addOn.Renewals)
        {
            if (renewal.IdempotencyKey == idempotencyKey)
                return renewal;
        }

        return null;
    }
}
