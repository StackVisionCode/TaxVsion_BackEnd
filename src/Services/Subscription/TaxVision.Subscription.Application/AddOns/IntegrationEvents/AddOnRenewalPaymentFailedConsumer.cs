using BuildingBlocks.Common;
using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Subscriptions.IntegrationEvents;
using TaxVision.Subscription.Domain.AddOns;

namespace TaxVision.Subscription.Application.AddOns.IntegrationEvents;

/// <summary>
/// A6/A5.7 — registra el fallo de cobro de la renovación de un add-on y, al agotarse los reintentos,
/// **arranca su ventana de gracia** para que la escalera siga hasta Suspended/Expired.
///
/// Sin ese paso el add-on se quedaba en <c>PastDue</c> para siempre: <c>FailRenewal</c> lo deja ahí y
/// nadie lo movía a <c>GracePeriod</c>, así que <c>GracePeriodExpirationJob</c> —que busca add-ons
/// pasados de gracia— no lo encontraba nunca. Y <c>PastDue</c> CONSERVA los entitlements a propósito
/// (es un periodo de gracia), de modo que el módulo del add-on quedaba habilitado indefinidamente sin
/// que nadie volviera a pagarlo. Con el gate en log-only no se ve; con el gate aplicando, es un módulo
/// gratis permanente.
///
/// Usa la MISMA política que la suscripción base (<see cref="SubscriptionOptions.GracePeriodDays"/>):
/// dos ventanas distintas para el mismo impago serían imposibles de explicar a un tenant.
/// </summary>
public static class AddOnRenewalPaymentFailedConsumer
{
    public static async Task Handle(
        AddOnRenewalPaymentFailedIntegrationEvent evt,
        ITenantAddOnRepository tenantAddOns,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        IOptions<SubscriptionOptions> options,
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

            // Un fallo con reintento pendiente no transiciona: solo se abre la gracia cuando los
            // reintentos se agotaron y `FailRenewal` movió el add-on a PastDue.
            if (addOn.Status == AddOnStatus.PastDue && previousStatus != AddOnStatus.PastDue)
            {
                var graceEndsAt = nowUtc.AddDays(Math.Max(1, options.Value.GracePeriodDays));
                var graceResult = addOn.EnterGracePeriodAfterRetriesExhausted(graceEndsAt, Guid.Empty, nowUtc);
                if (graceResult.IsFailure)
                    logger.LogWarning(
                        "Could not enter grace period for add-on {TenantAddOnId}: {Code}.",
                        addOn.Id,
                        graceResult.Error.Code
                    );
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
