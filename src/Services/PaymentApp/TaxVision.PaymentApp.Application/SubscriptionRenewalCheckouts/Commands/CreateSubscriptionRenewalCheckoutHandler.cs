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

namespace TaxVision.PaymentApp.Application.SubscriptionRenewalCheckouts.Commands;

/// <summary>
/// Crea la sesión de checkout hosteada para una renovación/reactivación self-service de la suscripción base.
/// Molde: <c>CreateSeatsCheckoutHandler</c>, decompuesto en pasos con nombre. Reusa <c>SaaSPayment.Create</c>
/// (<see cref="SaaSPaymentType.SubscriptionRenewalCheckout"/>, con <c>TargetAggregateId = RenewalIntentId</c>)
/// + <c>RecordHostedCheckoutSession</c> + los adapters. El webhook (source of truth) confirma el pago y publica
/// el evento que Subscription consume para reactivar la suscripción.
/// </summary>
public static class CreateSubscriptionRenewalCheckoutHandler
{
    private const string DefaultStatementDescriptor = "TAXVISION RENEWAL";
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(24);

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
        var providerResult = ResolveCheckoutProvider(command, providerFactory);
        if (providerResult.IsFailure)
            return Result.Failure<SubscriptionRenewalCheckoutResponse>(providerResult.Error);

        var resolution = await ResolvePaymentAsync(command, payments, logger, ct);
        if (resolution.IsFailure)
            return Result.Failure<SubscriptionRenewalCheckoutResponse>(resolution.Error);
        if (resolution.Value.Replay is { } replay)
            return Result.Success(replay);

        var payment = resolution.Value.Payment!;
        var nowUtc = DateTime.UtcNow;
        var expiresAtUtc = nowUtc.Add(SessionLifetime);

        var sessionResult = await CreateSessionAsync(command, payment, providerResult.Value, expiresAtUtc, logger, ct);
        TrackSessionOutcome(sessionResult, command.Provider.ToString(), metrics);
        if (sessionResult.IsFailure)
            return Result.Failure<SubscriptionRenewalCheckoutResponse>(sessionResult.Error);

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
        CreateSubscriptionRenewalCheckoutCommand command,
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

    private sealed record CheckoutResolution(
        SubscriptionRenewalCheckoutResponse? Replay,
        SaaSPayment? Payment,
        bool IsNew
    );

    // Key único por intención (subscription-renewal-checkout-{intentId}): un re-submit de la MISMA intención
    // replaya su sesión; un intento nuevo crea un pago nuevo (una intención fallida se reintenta como una nueva).
    private static async Task<Result<CheckoutResolution>> ResolvePaymentAsync(
        CreateSubscriptionRenewalCheckoutCommand command,
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
                    "Subscription renewal checkout already exists for IdempotencyKey {Key}; replaying (idempotent).",
                    command.IdempotencyKey
                );
                return Result.Success(new CheckoutResolution(replay, null, IsNew: false));
            }

            return Result.Failure<CheckoutResolution>(
                new Error(
                    "Subscription.RenewalCheckout.NotReplayable",
                    "A checkout already exists for this request but has no usable session."
                )
            );
        }

        var prepared = PrepareNewPayment(command);
        return prepared.IsFailure
            ? Result.Failure<CheckoutResolution>(prepared.Error)
            : Result.Success(new CheckoutResolution(null, prepared.Value, IsNew: true));
    }

    private static Result<SaaSPayment> PrepareNewPayment(CreateSubscriptionRenewalCheckoutCommand command)
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
            SaaSPaymentType.SubscriptionRenewalCheckout,
            command.RenewalIntentId,
            command.Provider,
            descriptorResult.Value,
            actorUserId: Guid.Empty,
            DateTime.UtcNow
        );
    }

    private static async Task<Result<HostedCheckoutSessionResult>> CreateSessionAsync(
        CreateSubscriptionRenewalCheckoutCommand command,
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
                ["renewalIntentId"] = command.RenewalIntentId.ToString("N"),
                ["saaSPaymentId"] = payment.Id.ToString("N"),
            }
        );

        var sessionResult = await adapter.CreateHostedCheckoutSessionAsync(sessionRequest, ct);
        if (sessionResult.IsFailure)
            logger.LogWarning(
                "Subscription renewal checkout session creation failed for intent {IntentId}. Error={ErrorCode}: {ErrorMessage}",
                command.RenewalIntentId,
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
        metrics.RecordAttempted(provider, SaaSPaymentType.SubscriptionRenewalCheckout.ToString());
        if (sessionResult.IsFailure)
            metrics.RecordFailed(
                provider,
                SaaSPaymentType.SubscriptionRenewalCheckout.ToString(),
                sessionResult.Error.Code
            );
    }

    private static async Task<Result<SubscriptionRenewalCheckoutResponse>> FinalizeAsync(
        CreateSubscriptionRenewalCheckoutCommand command,
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
                "Subscription renewal checkout for intent {IntentId} created a session ({SessionId}) but its payment reference was invalid: {ErrorCode}: {ErrorMessage}",
                command.RenewalIntentId,
                session.ProviderSessionId,
                referenceResult.Error.Code,
                referenceResult.Error.Message
            );
            return Result.Failure<SubscriptionRenewalCheckoutResponse>(referenceResult.Error);
        }

        var recordResult = payment.RecordHostedCheckoutSession(
            session.ProviderSessionId,
            referenceResult.Value,
            session.CheckoutUrl,
            nowUtc
        );
        if (recordResult.IsFailure)
            return Result.Failure<SubscriptionRenewalCheckoutResponse>(recordResult.Error);

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
                RenewalIntentId = command.RenewalIntentId,
                session.ProviderSessionId,
            },
            reason: null,
            nowUtc,
            ct
        );
        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation(
            "Subscription renewal checkout {SaaSPaymentId} created for intent {IntentId}.",
            payment.Id,
            command.RenewalIntentId
        );

        return Result.Success(
            new SubscriptionRenewalCheckoutResponse(
                payment.Id,
                session.CheckoutUrl,
                session.ProviderSessionId,
                expiresAtUtc
            )
        );
    }

    private static SubscriptionRenewalCheckoutResponse? BuildResponse(SaaSPayment payment)
    {
        if (payment.ProviderCheckoutSessionId is null || payment.NextActionUrl is null)
            return null;

        return new SubscriptionRenewalCheckoutResponse(
            payment.Id,
            payment.NextActionUrl,
            payment.ProviderCheckoutSessionId,
            payment.CreatedAtUtc.Add(SessionLifetime)
        );
    }
}
