using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Platform;
using BuildingBlocks.PurchaseIntentAmount;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Application.Common;
using TaxVision.PaymentApp.Application.Common.Branding;
using TaxVision.PaymentApp.Application.Common.HostedCheckout;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Application.SeatsCheckouts.Commands;

/// <summary>
/// Crea la sesión de checkout hosteada para una compra de asientos por redirect. Los pasos comunes los
/// pone <see cref="HostedCheckoutPipeline"/>; acá solo vive lo propio de asientos. El webhook (source of
/// truth) confirma el pago y publica el evento que Subscription consume para aprovisionar.
/// </summary>
public static class CreateSeatsCheckoutHandler
{
    public static async Task<Result<SeatsCheckoutResponse>> Handle(
        CreateSeatsCheckoutCommand command,
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
        // SEC-M1.C: la firma HMAC viene en el body M2M (emitida por Subscription con su secret). PaymentApp
        // la verifica localmente — sin round-trip al servicio dueño, sin TX visibility issue — para rechazar
        // amount tampering si el token M2M del caller se compromete. Fail-closed sólo en mismatch; firma
        // ausente pasa con warning hasta que RejectUnsigned=true.
        var verify = intentVerifier.Verify(
            new PurchaseIntentAmountInput(
                command.TenantId,
                PurchaseIntentKind.Seats,
                command.SeatPurchaseIntentId,
                command.AmountCents,
                command.Currency
            ),
            command.AmountSignature
        );
        if (verify.IsFailure)
            return Result.Failure<SeatsCheckoutResponse>(verify.Error);

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
            ? Result.Failure<SeatsCheckoutResponse>(result.Error)
            : Result.Success(
                new SeatsCheckoutResponse(
                    result.Value.PaymentId,
                    result.Value.CheckoutUrl,
                    result.Value.ProviderSessionId,
                    result.Value.ExpiresAtUtc
                )
            );
    }

    private static HostedCheckoutPolicy PolicyFor(
        CreateSeatsCheckoutCommand command,
        PlatformSettingsSnapshot platform
    ) =>
        new()
        {
            PaymentType = SaaSPaymentType.SeatsPurchaseCharge,
            Subject = "Seats checkout",
            ReferenceId = command.SeatPurchaseIntentId,
            StatementDescriptor = PlatformStatementDescriptors.For(platform, "SEATS"),
            // Subscription (dueño del precio de asiento) ya resolvió el total prorrateado.
            ResolveAmount = _ => Task.FromResult(Money.Create(command.AmountCents, command.Currency)),
            CreatePayment = (key, amount, descriptor, nowUtc) =>
                SaaSPayment.Create(
                    command.TenantId,
                    key,
                    amount,
                    SaaSPaymentType.SeatsPurchaseCharge,
                    command.SeatPurchaseIntentId,
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
                ["seatPurchaseIntentId"] = command.SeatPurchaseIntentId.ToString("N"),
                ["saaSPaymentId"] = payment.Id.ToString("N"),
            },
            BuildAuditPayload = (payment, session) =>
                new
                {
                    payment.Status,
                    SeatPurchaseIntentId = command.SeatPurchaseIntentId,
                    session.ProviderSessionId,
                },
        };

    // Clave única por intención (seat-checkout-{intentId}): un re-submit de la MISMA intención replaya su
    // sesión; un intento nuevo (otra intención) crea un pago nuevo. No hay reintento en sitio: una intención
    // fallida se reintenta como una compra nueva, con su propia intención y clave.
    private static Result<ExistingPaymentDecision> DecideOnExisting(SaaSPayment existing, DateTime nowUtc) =>
        HostedCheckoutPipeline.TryBuildResponse(existing) is { } replay
            ? Result.Success(ExistingPaymentDecision.Replay(replay))
            : Result.Failure<ExistingPaymentDecision>(
                new Error(
                    "Seats.Checkout.NotReplayable",
                    "A checkout already exists for this request but has no usable session."
                )
            );
}
