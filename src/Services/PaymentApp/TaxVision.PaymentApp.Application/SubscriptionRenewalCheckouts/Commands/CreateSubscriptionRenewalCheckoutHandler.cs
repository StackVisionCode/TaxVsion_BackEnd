using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Platform;
using BuildingBlocks.PurchaseIntentAmount;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Application.Common.Branding;
using TaxVision.PaymentApp.Application.Common.HostedCheckout;
using TaxVision.PaymentApp.Application.SaaSPayments.Common;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Application.SubscriptionRenewalCheckouts.Commands;

/// <summary>
/// Crea la sesión de checkout hosteada para una renovación/reactivación self-service de la suscripción base.
/// Los pasos comunes los pone <see cref="HostedCheckoutPipeline"/>; acá solo vive lo propio de la renovación.
/// El webhook (source of truth) confirma el pago y publica el evento que Subscription consume para reactivar.
/// </summary>
public static class CreateSubscriptionRenewalCheckoutHandler
{
    public static async Task<Result<SubscriptionRenewalCheckoutResponse>> Handle(
        CreateSubscriptionRenewalCheckoutCommand command,
        ISaaSPaymentRepository payments,
        IPaymentAdapterFactory providerFactory,
        IPaymentAuditLogWriter audit,
        IUnitOfWork unitOfWork,
        IPaymentAppMetrics metrics,
        ICorrelationContext correlation,
        IPurchaseIntentAmountVerifier intentVerifier,
        ILogger<SaaSPayment> logger,
        IPlatformSettings platform,
        CancellationToken ct
    )
    {
        // SEC-M1.C: ver CreateSeatsCheckoutHandler — firma HMAC local, fail-closed sólo en mismatch.
        var verify = intentVerifier.Verify(
            new PurchaseIntentAmountInput(
                command.TenantId,
                PurchaseIntentKind.SubscriptionRenewal,
                command.RenewalIntentId,
                command.AmountCents,
                command.Currency
            ),
            command.AmountSignature
        );
        if (verify.IsFailure)
            return Result.Failure<SubscriptionRenewalCheckoutResponse>(verify.Error);

        var request = new HostedCheckoutRequest(
            command.IdempotencyKey,
            command.PayerEmail,
            command.SuccessUrl,
            command.CancelUrl,
            command.Provider,
            command.Method
        );

        var result = await HostedCheckoutPipeline.RunAsync(
            request,
            PolicyFor(command, platform.Current),
            payments,
            providerFactory,
            audit,
            unitOfWork,
            metrics,
            correlation,
            logger,
            ct
        );

        return result.IsFailure
            ? Result.Failure<SubscriptionRenewalCheckoutResponse>(result.Error)
            : Result.Success(
                new SubscriptionRenewalCheckoutResponse(
                    result.Value.PaymentId,
                    result.Value.CheckoutUrl,
                    result.Value.ProviderSessionId,
                    result.Value.ExpiresAtUtc
                )
            );
    }

    private static HostedCheckoutPolicy PolicyFor(
        CreateSubscriptionRenewalCheckoutCommand command,
        PlatformSettingsSnapshot platform
    ) =>
        new()
        {
            PaymentType = SaaSPaymentType.SubscriptionRenewalCheckout,
            Subject = "Subscription renewal checkout",
            ReferenceId = command.RenewalIntentId,
            StatementDescriptor = PlatformStatementDescriptors.For(platform, "RENEWAL"),
            ResolveAmount = _ => Task.FromResult(Money.Create(command.AmountCents, command.Currency)),
            CreatePayment = (key, amount, descriptor, nowUtc) =>
                SaaSPayment
                    .Create(
                        command.TenantId,
                        key,
                        amount,
                        SaaSPaymentType.SubscriptionRenewalCheckout,
                        command.RenewalIntentId,
                        command.Provider,
                        descriptor,
                        actorUserId: Guid.Empty,
                        nowUtc
                    )
                    .WithConceptFor(SaaSPaymentType.SubscriptionRenewalCheckout),
            DecideOnExisting = DecideOnExisting,
            BuildMetadata = payment => new Dictionary<string, string>
            {
                ["tenantId"] = command.TenantId.ToString("N"),
                ["renewalIntentId"] = command.RenewalIntentId.ToString("N"),
                ["saaSPaymentId"] = payment.Id.ToString("N"),
            },
            BuildAuditPayload = (payment, session) =>
                new
                {
                    payment.Status,
                    RenewalIntentId = command.RenewalIntentId,
                    session.ProviderSessionId,
                },
        };

    // Clave única por intención (subscription-renewal-checkout-{intentId}): un re-submit de la MISMA intención
    // replaya su sesión; una intención fallida se reintenta como una nueva, con su propia clave.
    private static Result<ExistingPaymentDecision> DecideOnExisting(SaaSPayment existing, DateTime nowUtc) =>
        HostedCheckoutPipeline.TryBuildResponse(existing) is { } replay
            ? Result.Success(ExistingPaymentDecision.Replay(replay))
            : Result.Failure<ExistingPaymentDecision>(
                new Error(
                    "Subscription.RenewalCheckout.NotReplayable",
                    "A checkout already exists for this request but has no usable session."
                )
            );
}
