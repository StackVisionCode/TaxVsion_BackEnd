using BuildingBlocks.Common;
using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.AddOns;

namespace TaxVision.Subscription.Application.AddOns.IntegrationEvents;

public static class AddOnRenewalPaymentSucceededConsumer
{
    public static async Task Handle(
        AddOnRenewalPaymentSucceededIntegrationEvent evt,
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
                logger.LogWarning(
                    "AddOnRenewalPaymentSucceeded for unknown add-on {TenantAddOnId}.",
                    evt.TenantAddOnId
                );
                return;
            }

            var renewal = FindRenewalByKey(addOn, evt.IdempotencyKey);
            if (renewal is null)
            {
                logger.LogWarning(
                    "AddOnRenewalPaymentSucceeded for {TenantAddOnId} has no matching renewal for key {Key}.",
                    evt.TenantAddOnId,
                    evt.IdempotencyKey
                );
                return;
            }

            var result = addOn.CompleteRenewal(
                renewal.Id,
                evt.ExternalPaymentReference,
                actorUserId: Guid.Empty,
                evt.PaidAtUtc
            );
            if (result.IsFailure)
            {
                logger.LogWarning(
                    "Could not complete renewal for add-on {TenantAddOnId}: {Code}.",
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
