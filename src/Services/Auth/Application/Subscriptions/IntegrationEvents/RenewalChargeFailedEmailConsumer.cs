using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Platform;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging;
using TaxVision.Auth.Application.Abstractions;
using Wolverine;

namespace TaxVision.Auth.Application.Subscriptions.IntegrationEvents;

/// <summary>
/// Un cobro automático de renovación falló: le pone destinatario (el admin principal, como el resto de
/// correos de la suscripción) y el enlace directo al pago en el Account del Landing, y lo manda por el
/// canal de correos de suscripción. Uno por intento.
/// </summary>
public static class RenewalChargeFailedEmailConsumer
{
    /// <summary>Sección del Account con "Renew now" / "Pay now". Sin sesión, el Account pide entrar y vuelve acá.</summary>
    public const string AccountPlanPath = "/account/plan";

    public static async Task Handle(
        RenewalChargeFailedIntegrationEvent evt,
        IUserRepository users,
        IMessageBus bus,
        IPlatformSettings platform,
        ICorrelationContext correlation,
        ILogger<RenewalChargeFailedIntegrationEvent> logger,
        CancellationToken ct
    )
    {
        var correlationId = string.IsNullOrWhiteSpace(evt.CorrelationId)
            ? evt.EventId.ToString("N")
            : evt.CorrelationId;

        using (correlation.Push(correlationId))
        {
            var admin = await users.GetPrimaryAdminAsync(evt.TenantId, ct);
            if (admin is null)
            {
                logger.LogWarning(
                    "No primary admin to notify for tenant {TenantId} about failed charge {SaaSPaymentId}.",
                    evt.TenantId,
                    evt.SaaSPaymentId
                );
                return;
            }

            var accountUrl = platform.Current.LandingUrl(AccountPlanPath);

            bus.TenantId = PlatformTenant.Id.ToString();
            await bus.PublishAsync(
                new TenantSubscriptionEmailRequestedIntegrationEvent
                {
                    TenantId = evt.TenantId,
                    Email = admin.Email,
                    FirstName = admin.Name,
                    Status =
                        evt.Consequence == RenewalChargeConsequences.SubscriptionSuspended ? "Suspended" : "Active",
                    Reason = RenewalChargeFailedIntegrationEvent.EmailReason,
                    FailureCode = evt.FailureCode,
                    RenewUrl = accountUrl,
                    ManageUrl = accountUrl,
                    Amount = ChargeAmountFormat.Format(evt.AmountCents, evt.Currency),
                    CardBrand = evt.CardBrand,
                    CardLast4 = evt.CardLast4,
                    SaaSPaymentId = evt.SaaSPaymentId,
                    AttemptNumber = evt.AttemptNumber,
                    MaxAttempts = evt.MaxAttempts,
                    IsFinalAttempt = evt.IsFinal,
                    NextAttemptAtUtc = evt.NextRetryAtUtc,
                    ItemDescription = evt.ItemDescription,
                    Consequence = evt.Consequence,
                    CorrelationId = correlationId,
                }
            );

            logger.LogInformation(
                "Queued failed-charge email for tenant {TenantId} (attempt {Attempt} of {Max}, final {IsFinal}).",
                evt.TenantId,
                evt.AttemptNumber,
                evt.MaxAttempts,
                evt.IsFinal
            );
        }
    }
}
