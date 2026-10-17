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

namespace TaxVision.PaymentApp.Application.SeatsCheckouts.Commands;

/// <summary>Pipeline del checkout de reactivación: reusa <see cref="HostedCheckoutPipeline"/> con política
/// específica — <see cref="SaaSPaymentType.SeatRenewal"/> + <see cref="SaaSPayment.TargetAggregateId"/> = seatId,
/// para que el publisher global <see cref="SaaSPaymentResultPublisher"/> dispare el evento ya existente
/// <c>SeatRenewalPaymentSucceededIntegrationEvent</c> y Subscription aplique la reactivación en el aggregate.</summary>
public static class CreateSeatReactivationCheckoutHandler
{
    public static async Task<Result<SeatsCheckoutResponse>> Handle(
        CreateSeatReactivationCheckoutCommand command,
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
        var verify = intentVerifier.Verify(
            new PurchaseIntentAmountInput(
                command.TenantId,
                PurchaseIntentKind.SeatReactivation,
                command.SeatId,
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
        CreateSeatReactivationCheckoutCommand command,
        PlatformSettingsSnapshot platform
    ) =>
        new()
        {
            PaymentType = SaaSPaymentType.SeatRenewal,
            Subject = "Seat reactivation",
            ReferenceId = command.SeatId,
            StatementDescriptor = PlatformStatementDescriptors.For(platform, "SEATS"),
            ResolveAmount = _ => Task.FromResult(Money.Create(command.AmountCents, command.Currency)),
            CreatePayment = (key, amount, descriptor, nowUtc) =>
                SaaSPayment
                    .Create(
                        command.TenantId,
                        key,
                        amount,
                        SaaSPaymentType.SeatRenewal,
                        // Target = seat concreto: así el publisher global pone SeatId en el evento y el consumer
                        // de Subscription identifica el aggregate que debe reactivarse.
                        command.SeatId,
                        command.Provider,
                        descriptor,
                        actorUserId: Guid.Empty,
                        nowUtc
                    )
                    .WithConceptFor(SaaSPaymentType.SeatRenewal),
            DecideOnExisting = DecideOnExisting,
            BuildMetadata = payment => new Dictionary<string, string>
            {
                ["tenantId"] = command.TenantId.ToString("N"),
                ["seatId"] = command.SeatId.ToString("N"),
                ["saaSPaymentId"] = payment.Id.ToString("N"),
                ["flow"] = "seat-reactivation",
            },
            BuildAuditPayload = (payment, session) =>
                new
                {
                    payment.Status,
                    SeatId = command.SeatId,
                    session.ProviderSessionId,
                    Flow = "seat-reactivation",
                },
        };

    /// <summary>Replay idempotente del mismo click: si el pago existente aún tiene sesión válida, se devuelve
    /// esa misma URL en vez de abrir otra. Un click nuevo tras una suspensión posterior genera idempotency key
    /// nueva (bucket cambia con el <c>SuspendedAtUtc</c> del seat) y crea un pago aparte.</summary>
    private static Result<ExistingPaymentDecision> DecideOnExisting(SaaSPayment existing, DateTime nowUtc) =>
        HostedCheckoutPipeline.TryBuildResponse(existing) is { } replay
            ? Result.Success(ExistingPaymentDecision.Replay(replay))
            : Result.Failure<ExistingPaymentDecision>(
                new Error(
                    "SeatReactivation.Checkout.NotReplayable",
                    "A reactivation checkout already exists for this seat but has no usable session."
                )
            );
}
