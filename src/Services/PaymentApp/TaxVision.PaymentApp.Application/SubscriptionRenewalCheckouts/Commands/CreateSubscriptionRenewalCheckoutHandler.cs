using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Application.Common.HostedCheckout;
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
    private const string DefaultStatementDescriptor = "TAXVISION RENEWAL";

    public static async Task<Result<SubscriptionRenewalCheckoutResponse>> Handle(
        CreateSubscriptionRenewalCheckoutCommand command,
        ISaaSPaymentRepository payments,
        IPaymentAdapterFactory providerFactory,
        IPaymentAuditLogWriter audit,
        IUnitOfWork unitOfWork,
        IPaymentAppMetrics metrics,
        ICorrelationContext correlation,
        ILogger<SaaSPayment> logger,
        CancellationToken ct
    )
    {
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
            PolicyFor(command),
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

    private static HostedCheckoutPolicy PolicyFor(CreateSubscriptionRenewalCheckoutCommand command) =>
        new()
        {
            PaymentType = SaaSPaymentType.SubscriptionRenewalCheckout,
            Subject = "Subscription renewal checkout",
            ReferenceId = command.RenewalIntentId,
            StatementDescriptor = DefaultStatementDescriptor,
            ResolveAmount = _ => Task.FromResult(Money.Create(command.AmountCents, command.Currency)),
            CreatePayment = (key, amount, descriptor, nowUtc) =>
                SaaSPayment.Create(
                    command.TenantId,
                    key,
                    amount,
                    SaaSPaymentType.SubscriptionRenewalCheckout,
                    command.RenewalIntentId,
                    command.Provider,
                    descriptor,
                    actorUserId: Guid.Empty,
                    nowUtc
                ),
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
