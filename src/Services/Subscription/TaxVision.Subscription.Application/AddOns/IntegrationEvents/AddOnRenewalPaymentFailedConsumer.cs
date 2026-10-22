using BuildingBlocks.Common;
using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Subscriptions.IntegrationEvents;
using TaxVision.Subscription.Domain.AddOns;
using Wolverine;

namespace TaxVision.Subscription.Application.AddOns.IntegrationEvents;

/// <summary>
/// Registra el fallo de cobro de la renovación de un add-on y, al agotarse los reintentos, lo suspende
/// (sin ventana de gracia intermedia): la escalera queda Active → PastDue → Suspended → Expired.
/// </summary>
public static class AddOnRenewalPaymentFailedConsumer
{
    public static async Task Handle(
        AddOnRenewalPaymentFailedIntegrationEvent evt,
        ITenantAddOnRepository tenantAddOns,
        IAddOnDefinitionRepository definitions,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
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

            // El nombre se busca antes de tocar nada: si el catálogo falla, el correo sale genérico.
            var itemDescription = evt.IsAutomaticCharge
                ? RenewalItemDescriptions.AddOn(await TryGetAddOnNameAsync(definitions, addOn, logger, ct))
                : string.Empty;

            var previousStatus = addOn.Status;
            var nowUtc = DateTime.UtcNow;
            var result = addOn.FailRenewal(
                renewal.Id,
                evt.FailureCode,
                evt.FailureReason,
                evt.WillRetry,
                evt.NextRetryAtUtc,
                actorUserId: Guid.Empty,
                nowUtc
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

            // Al agotarse los reintentos (PastDue), el add-on se suspende de una: ya no se da ventana con
            // acceso vivo. Un fallo con reintento pendiente no transiciona.
            if (addOn.Status == AddOnStatus.PastDue && previousStatus != AddOnStatus.PastDue)
            {
                var suspendResult = addOn.SuspendAfterRetriesExhausted(Guid.Empty, nowUtc);
                if (suspendResult.IsFailure)
                    logger.LogWarning(
                        "Could not suspend add-on {TenantAddOnId} after retries exhausted: {Code}.",
                        addOn.Id,
                        suspendResult.Error.Code
                    );
            }

            await unitOfWork.SaveChangesAsync(ct);

            var suspended = addOn.Status == AddOnStatus.Suspended && previousStatus != AddOnStatus.Suspended;
            await bus.PublishRenewalChargeFailedAsync(
                evt,
                itemDescription,
                suspended ? RenewalChargeConsequences.AddOnSuspended : null,
                correlationId
            );
        }
    }

    private static async Task<string?> TryGetAddOnNameAsync(
        IAddOnDefinitionRepository definitions,
        TenantAddOn addOn,
        ILogger logger,
        CancellationToken ct
    )
    {
        try
        {
            return (await definitions.GetByCodeAsync(addOn.AddOnCode, ct))?.Name;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not read add-on {AddOnCode} for the failed-charge email.", addOn.AddOnCode);
            return null;
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
