using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Application.Common;
using TaxVision.PaymentApp.Application.Common.HostedCheckout;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Application.AddOnCheckouts.Commands;

/// <summary>
/// Crea la sesión de checkout hosteada para comprar un add-on. Los pasos comunes los pone
/// <see cref="HostedCheckoutPipeline"/>; acá solo vive lo propio del add-on. El webhook (source of truth)
/// confirma el pago y publica el evento que Subscription consume para activarlo.
/// </summary>
public static class CreateAddOnCheckoutHandler
{
    private const string DefaultStatementDescriptor = "TAXVISION ADDON";

    public static async Task<Result<AddOnCheckoutResponse>> Handle(
        CreateAddOnCheckoutCommand command,
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
            ? Result.Failure<AddOnCheckoutResponse>(result.Error)
            : Result.Success(
                new AddOnCheckoutResponse(
                    result.Value.PaymentId,
                    result.Value.CheckoutUrl,
                    result.Value.ProviderSessionId,
                    result.Value.ExpiresAtUtc
                )
            );
    }

    private static HostedCheckoutPolicy PolicyFor(CreateAddOnCheckoutCommand command) =>
        new()
        {
            PaymentType = SaaSPaymentType.AddOnPurchaseCharge,
            Subject = "Add-on checkout",
            ReferenceId = command.AddOnPurchaseIntentId,
            StatementDescriptor = DefaultStatementDescriptor,
            // Subscription (dueño del catálogo y del prorrateo) ya resolvió el total.
            ResolveAmount = _ => Task.FromResult(Money.Create(command.AmountCents, command.Currency)),
            CreatePayment = (key, amount, descriptor, nowUtc) =>
                SaaSPayment.Create(
                    command.TenantId,
                    key,
                    amount,
                    SaaSPaymentType.AddOnPurchaseCharge,
                    command.AddOnPurchaseIntentId,
                    command.Provider,
                    descriptor,
                    actorUserId: Guid.Empty,
                    nowUtc,
                    breakdown: ChargeBreakdowns.FromRequest(
                        command.Quantity,
                        command.UnitAmountCents,
                        amount.AmountCents
                    )
                ),
            DecideOnExisting = DecideOnExisting,
            BuildMetadata = payment => new Dictionary<string, string>
            {
                ["tenantId"] = command.TenantId.ToString("N"),
                ["addOnPurchaseIntentId"] = command.AddOnPurchaseIntentId.ToString("N"),
                ["saaSPaymentId"] = payment.Id.ToString("N"),
            },
            BuildAuditPayload = (payment, session) =>
                new
                {
                    payment.Status,
                    AddOnPurchaseIntentId = command.AddOnPurchaseIntentId,
                    session.ProviderSessionId,
                },
        };

    // Clave única por intención (addon-checkout-{intentId}): un re-submit de la MISMA intención replaya su
    // sesión; una intención fallida se reintenta como una compra nueva, con su propia clave.
    private static Result<ExistingPaymentDecision> DecideOnExisting(SaaSPayment existing, DateTime nowUtc) =>
        HostedCheckoutPipeline.TryBuildResponse(existing) is { } replay
            ? Result.Success(ExistingPaymentDecision.Replay(replay))
            : Result.Failure<ExistingPaymentDecision>(
                new Error(
                    "AddOn.Checkout.NotReplayable",
                    "A checkout already exists for this request but has no usable session."
                )
            );
}
