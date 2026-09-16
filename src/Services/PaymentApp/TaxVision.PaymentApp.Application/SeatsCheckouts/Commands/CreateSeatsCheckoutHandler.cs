using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Application.Common;
using TaxVision.PaymentApp.Domain.Audit;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Application.SeatsCheckouts.Commands;

/// <summary>
/// Crea la sesión de checkout hosteada para una compra de asientos por redirect. Molde:
/// <c>CreateOnboardingCheckoutHandler</c>, decompuesto en pasos con nombre para mantener <c>Handle</c> chico.
/// Reusa <c>SaaSPayment.Create</c> (<see cref="SaaSPaymentType.SeatsPurchaseCharge"/>, con
/// <c>TargetAggregateId = SeatPurchaseIntentId</c>) + <c>RecordHostedCheckoutSession</c> + los adapters. El
/// webhook (source of truth) confirma el pago y publica el evento que Subscription consume para aprovisionar.
/// </summary>
public static class CreateSeatsCheckoutHandler
{
    private const string DefaultStatementDescriptor = "TAXVISION SEATS";
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(24);

    public static async Task<Result<SeatsCheckoutResponse>> Handle(
        CreateSeatsCheckoutCommand command,
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
        var providerResult = ResolveCheckoutProvider(command, providerFactory);
        if (providerResult.IsFailure)
            return Result.Failure<SeatsCheckoutResponse>(providerResult.Error);

        var resolution = await ResolvePaymentAsync(command, payments, logger, ct);
        if (resolution.IsFailure)
            return Result.Failure<SeatsCheckoutResponse>(resolution.Error);
        if (resolution.Value.Replay is { } replay)
            return Result.Success(replay);

        var payment = resolution.Value.Payment!;
        var nowUtc = DateTime.UtcNow;
        var expiresAtUtc = nowUtc.Add(SessionLifetime);

        var sessionResult = await CreateSessionAsync(command, payment, providerResult.Value, expiresAtUtc, logger, ct);
        TrackSessionOutcome(sessionResult, command.Provider.ToString(), metrics);
        if (sessionResult.IsFailure)
            return Result.Failure<SeatsCheckoutResponse>(sessionResult.Error);

        return await FinalizeAsync(
            command,
            payment,
            sessionResult.Value,
            nowUtc,
            expiresAtUtc,
            payments,
            audit,
            unitOfWork,
            correlation,
            logger,
            ct
        );
    }

    private static Result<IPaymentProvider> ResolveCheckoutProvider(
        CreateSeatsCheckoutCommand command,
        IPaymentAdapterFactory providerFactory
    )
    {
        IPaymentProvider provider;
        try
        {
            provider = providerFactory.Resolve(command.Provider);
        }
        catch (InvalidOperationException)
        {
            return Result.Failure<IPaymentProvider>(
                new Error("PaymentProvider.NotConfigured", "The selected payment provider is not configured.")
            );
        }

        if (!provider.Capabilities.SupportsHostedCheckoutRedirect)
            return Result.Failure<IPaymentProvider>(
                new Error(
                    "PaymentMethod.UnsupportedForCheckout",
                    "The selected provider does not support hosted checkout."
                )
            );

        if (!provider.Capabilities.SupportedMethods.Contains(command.Method))
            return Result.Failure<IPaymentProvider>(
                new Error(
                    "PaymentMethod.UnsupportedByProvider",
                    "The selected payment method is not supported by this provider."
                )
            );

        return Result.Success(provider);
    }

    private sealed record CheckoutResolution(SeatsCheckoutResponse? Replay, SaaSPayment? Payment, bool IsNew);

    // Key único por intención (seat-checkout-{intentId}): un re-submit de la MISMA intención replaya su sesión;
    // un intento nuevo (otra intención) crea un pago nuevo. No hay retry in-place (una intención fallida se
    // reintenta como una compra nueva, con su propia intención y key).
    private static async Task<Result<CheckoutResolution>> ResolvePaymentAsync(
        CreateSeatsCheckoutCommand command,
        ISaaSPaymentRepository payments,
        ILogger<SaaSPayment> logger,
        CancellationToken ct
    )
    {
        var existing = await payments.GetByIdempotencyKeyAsync(command.IdempotencyKey, ct);
        if (existing is not null)
        {
            if (BuildResponse(existing) is { } replay)
            {
                logger.LogInformation(
                    "Seats checkout already exists for IdempotencyKey {Key}; replaying (idempotent).",
                    command.IdempotencyKey
                );
                return Result.Success(new CheckoutResolution(replay, null, IsNew: false));
            }

            return Result.Failure<CheckoutResolution>(
                new Error(
                    "Seats.Checkout.NotReplayable",
                    "A checkout already exists for this request but has no usable session."
                )
            );
        }

        var prepared = PrepareNewPayment(command);
        return prepared.IsFailure
            ? Result.Failure<CheckoutResolution>(prepared.Error)
            : Result.Success(new CheckoutResolution(null, prepared.Value, IsNew: true));
    }

    private static Result<SaaSPayment> PrepareNewPayment(CreateSeatsCheckoutCommand command)
    {
        var keyResult = IdempotencyKey.Create(command.IdempotencyKey);
        if (keyResult.IsFailure)
            return Result.Failure<SaaSPayment>(keyResult.Error);

        var amountResult = Money.Create(command.AmountCents, command.Currency);
        if (amountResult.IsFailure)
            return Result.Failure<SaaSPayment>(amountResult.Error);

        var descriptorResult = StatementDescriptor.Create(DefaultStatementDescriptor);
        if (descriptorResult.IsFailure)
            return Result.Failure<SaaSPayment>(descriptorResult.Error);

        return SaaSPayment.Create(
            command.TenantId,
            keyResult.Value,
            amountResult.Value,
            SaaSPaymentType.SeatsPurchaseCharge,
            command.SeatPurchaseIntentId,
            command.Provider,
            descriptorResult.Value,
            actorUserId: Guid.Empty,
            DateTime.UtcNow
        );
    }

    private static async Task<Result<HostedCheckoutSessionResult>> CreateSessionAsync(
        CreateSeatsCheckoutCommand command,
        SaaSPayment payment,
        IPaymentProvider adapter,
        DateTime expiresAtUtc,
        ILogger<SaaSPayment> logger,
        CancellationToken ct
    )
    {
        var sessionRequest = new HostedCheckoutSessionRequest(
            Amount: payment.Amount,
            Method: command.Method,
            IdempotencyKey: payment.IdempotencyKey,
            Descriptor: payment.StatementDescriptor,
            PayerEmail: command.PayerEmail,
            SuccessUrl: command.SuccessUrl,
            CancelUrl: command.CancelUrl,
            ExpiresAtUtc: expiresAtUtc,
            Metadata: new Dictionary<string, string>
            {
                ["tenantId"] = command.TenantId.ToString("N"),
                ["seatPurchaseIntentId"] = command.SeatPurchaseIntentId.ToString("N"),
                ["saaSPaymentId"] = payment.Id.ToString("N"),
            }
        );

        var sessionResult = await adapter.CreateHostedCheckoutSessionAsync(sessionRequest, ct);
        if (sessionResult.IsFailure)
            logger.LogWarning(
                "Seats checkout session creation failed for intent {IntentId}. Error={ErrorCode}: {ErrorMessage}",
                command.SeatPurchaseIntentId,
                sessionResult.Error.Code,
                sessionResult.Error.Message
            );

        return sessionResult;
    }

    private static void TrackSessionOutcome(
        Result<HostedCheckoutSessionResult> sessionResult,
        string provider,
        IPaymentAppMetrics metrics
    )
    {
        metrics.RecordAttempted(provider, SaaSPaymentType.SeatsPurchaseCharge.ToString());
        if (sessionResult.IsFailure)
            metrics.RecordFailed(provider, SaaSPaymentType.SeatsPurchaseCharge.ToString(), sessionResult.Error.Code);
    }

    private static async Task<Result<SeatsCheckoutResponse>> FinalizeAsync(
        CreateSeatsCheckoutCommand command,
        SaaSPayment payment,
        HostedCheckoutSessionResult session,
        DateTime nowUtc,
        DateTime expiresAtUtc,
        ISaaSPaymentRepository payments,
        IPaymentAuditLogWriter audit,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<SaaSPayment> logger,
        CancellationToken ct
    )
    {
        var referenceResult = ExternalPaymentReference.Create(command.Provider, session.ProviderPaymentIntentReference);
        if (referenceResult.IsFailure)
        {
            logger.LogWarning(
                "Seats checkout for intent {IntentId} created a session ({SessionId}) but its payment reference was invalid: {ErrorCode}: {ErrorMessage}",
                command.SeatPurchaseIntentId,
                session.ProviderSessionId,
                referenceResult.Error.Code,
                referenceResult.Error.Message
            );
            return Result.Failure<SeatsCheckoutResponse>(referenceResult.Error);
        }

        var recordResult = payment.RecordHostedCheckoutSession(
            session.ProviderSessionId,
            referenceResult.Value,
            session.CheckoutUrl,
            nowUtc
        );
        if (recordResult.IsFailure)
            return Result.Failure<SeatsCheckoutResponse>(recordResult.Error);

        await payments.AddAsync(payment, ct);
        await AuditEntryFactory.AppendAsync(
            audit,
            payment.TenantId,
            nameof(SaaSPayment),
            payment.Id,
            PaymentAuditAction.SaaSPaymentCreated,
            actorUserId: Guid.Empty,
            correlation.CorrelationId,
            before: (object?)null,
            after: new
            {
                payment.Status,
                SeatPurchaseIntentId = command.SeatPurchaseIntentId,
                session.ProviderSessionId,
            },
            reason: null,
            nowUtc,
            ct
        );
        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation(
            "Seats checkout {SaaSPaymentId} created for intent {IntentId}.",
            payment.Id,
            command.SeatPurchaseIntentId
        );

        return Result.Success(
            new SeatsCheckoutResponse(payment.Id, session.CheckoutUrl, session.ProviderSessionId, expiresAtUtc)
        );
    }

    private static SeatsCheckoutResponse? BuildResponse(SaaSPayment payment)
    {
        if (payment.ProviderCheckoutSessionId is null || payment.NextActionUrl is null)
            return null;

        return new SeatsCheckoutResponse(
            payment.Id,
            payment.NextActionUrl,
            payment.ProviderCheckoutSessionId,
            payment.CreatedAtUtc.Add(SessionLifetime)
        );
    }
}
