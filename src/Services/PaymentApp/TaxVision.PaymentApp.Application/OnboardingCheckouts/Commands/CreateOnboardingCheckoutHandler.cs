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

namespace TaxVision.PaymentApp.Application.OnboardingCheckouts.Commands;

/// <summary>
/// PayFlow (Fase 8) — crea (o, si ya existe por <see cref="CreateOnboardingCheckoutCommand.IdempotencyKey"/>,
/// devuelve) una Stripe Checkout Session hosteada para el primer pago de un onboarding
/// pago-primero. Solo Stripe soporta hoy este flujo (<see cref="IPaymentProvider.CreateHostedCheckoutSessionAsync"/>) —
/// el provider no viene en el request porque este endpoint no lo necesita elegible, a
/// diferencia de <c>ChargeSaaSPaymentHandler</c>.
/// PayFlow (Fase 16) — el precio/moneda ya NO vienen del caller: se resuelven acá mismo vía M2M a
/// Subscription (<see cref="ISubscriptionPlanPricingClient"/>), cerrando el gap documentado en
/// <c>Auth.Application.Onboarding.TenantOnboardings.Commands.StartOnboardingCheckoutCommand</c>.
/// <para>
/// PayFlow (auditoría F20) — <c>Handle</c> descompuesto en pasos con nombre (replay idempotente,
/// resolver precio+preparar el aggregate, crear la sesión en Stripe, registrarla en el aggregate,
/// persistir+auditar) para que cada uno se lea de una sola vez; el comportamiento no cambió.
/// </para>
/// <para>
/// PayFlow (auditoría F33) — <c>metrics.RecordAttempted</c> vivía repartido entre
/// <c>CreateStripeSessionAsync</c> (rama de fallo) y <c>PersistAndAuditAsync</c> (rama de éxito),
/// dos private methods no contiguos para el mismo contador. Ahora se registra una sola vez en
/// <c>Handle</c>, justo después de conocer el resultado de la sesión de Stripe.
/// </para>
/// </summary>
public static class CreateOnboardingCheckoutHandler
{
    private const string DefaultStatementDescriptor = "TAXVISION SAAS";
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(24);

    public static async Task<Result<OnboardingCheckoutResponse>> Handle(
        CreateOnboardingCheckoutCommand command,
        ISaaSPaymentRepository payments,
        IPaymentAdapterFactory providerFactory,
        ISubscriptionPlanPricingClient planPricing,
        IPaymentAuditLogWriter audit,
        IUnitOfWork unitOfWork,
        IPaymentAppMetrics metrics,
        ICorrelationContext correlation,
        ILogger<SaaSPayment> logger,
        CancellationToken ct
    )
    {
        var replay = await TryReplayExistingAsync(command, payments, logger, ct);
        if (replay is not null)
            return replay;

        var preparedResult = await ResolvePriceAndPreparePaymentAsync(command, planPricing, ct);
        if (preparedResult.IsFailure)
            return Result.Failure<OnboardingCheckoutResponse>(preparedResult.Error);

        var payment = preparedResult.Value;
        var nowUtc = DateTime.UtcNow;
        var expiresAtUtc = nowUtc.Add(SessionLifetime);

        var sessionResult = await CreateStripeSessionAsync(command, payment, providerFactory, expiresAtUtc, logger, ct);

        metrics.RecordAttempted(PaymentProviderCode.Stripe.ToString(), SaaSPaymentType.OnboardingInitial.ToString());
        if (sessionResult.IsFailure)
        {
            metrics.RecordFailed(
                PaymentProviderCode.Stripe.ToString(),
                SaaSPaymentType.OnboardingInitial.ToString(),
                sessionResult.Error.Code
            );
            return Result.Failure<OnboardingCheckoutResponse>(sessionResult.Error);
        }

        var session = sessionResult.Value;
        var recordResult = RecordSession(command, payment, session, nowUtc, logger);
        if (recordResult.IsFailure)
            return Result.Failure<OnboardingCheckoutResponse>(recordResult.Error);

        await PersistAndAuditAsync(command, payment, session, payments, audit, unitOfWork, correlation, nowUtc, ct);

        logger.LogInformation(
            "Onboarding checkout {SaaSPaymentId} created for onboarding {OnboardingId}.",
            payment.Id,
            command.OnboardingId
        );

        return Result.Success(
            new OnboardingCheckoutResponse(payment.Id, session.CheckoutUrl, session.ProviderSessionId, expiresAtUtc)
        );
    }

    private static async Task<Result<OnboardingCheckoutResponse>?> TryReplayExistingAsync(
        CreateOnboardingCheckoutCommand command,
        ISaaSPaymentRepository payments,
        ILogger<SaaSPayment> logger,
        CancellationToken ct
    )
    {
        var existing = await payments.GetByIdempotencyKeyAsync(command.IdempotencyKey, ct);
        if (existing is null)
            return null;

        var replay = BuildResponse(existing);
        if (replay is null)
        {
            logger.LogWarning(
                "Onboarding checkout for IdempotencyKey {Key} already exists but has no recorded checkout session.",
                command.IdempotencyKey
            );
            return Result.Failure<OnboardingCheckoutResponse>(
                new Error("Onboarding.Checkout.NoSession", "This checkout already exists in an unexpected state.")
            );
        }

        logger.LogInformation(
            "Onboarding checkout already exists for IdempotencyKey {Key}; replaying (idempotent).",
            command.IdempotencyKey
        );
        return Result.Success(replay);
    }

    private static async Task<Result<SaaSPayment>> ResolvePriceAndPreparePaymentAsync(
        CreateOnboardingCheckoutCommand command,
        ISubscriptionPlanPricingClient planPricing,
        CancellationToken ct
    )
    {
        var priceResult = await planPricing.GetPriceAsync(command.PlanId, command.BillingCycle, ct);
        if (priceResult.IsFailure)
            return Result.Failure<SaaSPayment>(priceResult.Error);

        return PrepareNewPayment(command, priceResult.Value);
    }

    private static async Task<Result<HostedCheckoutSessionResult>> CreateStripeSessionAsync(
        CreateOnboardingCheckoutCommand command,
        SaaSPayment payment,
        IPaymentAdapterFactory providerFactory,
        DateTime expiresAtUtc,
        ILogger<SaaSPayment> logger,
        CancellationToken ct
    )
    {
        var adapter = providerFactory.Resolve(PaymentProviderCode.Stripe);
        var sessionRequest = new HostedCheckoutSessionRequest(
            Amount: payment.Amount,
            IdempotencyKey: payment.IdempotencyKey,
            Descriptor: payment.StatementDescriptor,
            PayerEmail: command.PayerEmail,
            SuccessUrl: command.SuccessUrl,
            CancelUrl: command.CancelUrl,
            ExpiresAtUtc: expiresAtUtc,
            Metadata: new Dictionary<string, string>
            {
                ["onboardingId"] = command.OnboardingId.ToString("N"),
                ["saaSPaymentId"] = payment.Id.ToString("N"),
            }
        );

        var sessionResult = await adapter.CreateHostedCheckoutSessionAsync(sessionRequest, ct);
        if (sessionResult.IsFailure)
        {
            logger.LogWarning(
                "Onboarding checkout session creation failed for onboarding {OnboardingId}. Error={ErrorCode}: {ErrorMessage}",
                command.OnboardingId,
                sessionResult.Error.Code,
                sessionResult.Error.Message
            );
        }

        return sessionResult;
    }

    private static Result RecordSession(
        CreateOnboardingCheckoutCommand command,
        SaaSPayment payment,
        HostedCheckoutSessionResult session,
        DateTime nowUtc,
        ILogger<SaaSPayment> logger
    )
    {
        var referenceResult = ExternalPaymentReference.Create(
            PaymentProviderCode.Stripe,
            session.ProviderPaymentIntentReference
        );
        if (referenceResult.IsFailure)
        {
            // Bug real encontrado en la verificación E2E: si esto falla, Stripe YA creó la sesión
            // (consumió el IdempotencyKey) pero acá no queda ningún rastro local -- sin este log,
            // el próximo intento con la misma IdempotencyKey choca contra Stripe sin ninguna pista
            // de qué pasó la primera vez.
            logger.LogWarning(
                "Onboarding checkout for {OnboardingId} created a Stripe session ({SessionId}) but its payment reference was invalid: {ErrorCode}: {ErrorMessage}",
                command.OnboardingId,
                session.ProviderSessionId,
                referenceResult.Error.Code,
                referenceResult.Error.Message
            );
            return Result.Failure(referenceResult.Error);
        }

        var recordResult = payment.RecordHostedCheckoutSession(
            session.ProviderSessionId,
            referenceResult.Value,
            session.CheckoutUrl,
            nowUtc
        );
        if (recordResult.IsFailure)
            logger.LogWarning(
                "Onboarding checkout for {OnboardingId} created a Stripe session ({SessionId}) but recording it locally failed: {ErrorCode}: {ErrorMessage}",
                command.OnboardingId,
                session.ProviderSessionId,
                recordResult.Error.Code,
                recordResult.Error.Message
            );

        return recordResult;
    }

    private static async Task PersistAndAuditAsync(
        CreateOnboardingCheckoutCommand command,
        SaaSPayment payment,
        HostedCheckoutSessionResult session,
        ISaaSPaymentRepository payments,
        IPaymentAuditLogWriter audit,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
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
                OnboardingId = command.OnboardingId,
                session.ProviderSessionId,
            },
            reason: null,
            nowUtc,
            ct
        );

        await unitOfWork.SaveChangesAsync(ct);
    }

    private static Result<SaaSPayment> PrepareNewPayment(CreateOnboardingCheckoutCommand command, PlanPrice price)
    {
        var keyResult = IdempotencyKey.Create(command.IdempotencyKey);
        if (keyResult.IsFailure)
            return Result.Failure<SaaSPayment>(keyResult.Error);

        // Gift/Referral: se cobra el NETO si Auth lo pasó (descuento parcial), validado contra el bruto
        // autoritativo de Subscription; si no, el bruto. El carril $0 no llega acá (Auth no invoca checkout).
        var chargeCents = price.AmountCents;
        var chargeCurrency = price.Currency;
        if (command.NetAmountCents is { } net)
        {
            if (net <= 0 || net > price.AmountCents)
                return Result.Failure<SaaSPayment>(
                    new Error(
                        "Onboarding.Checkout.InvalidNet",
                        "The net amount must be greater than zero and not exceed the resolved plan price."
                    )
                );
            chargeCents = net;
        }

        var amountResult = Money.Create(chargeCents, chargeCurrency);
        if (amountResult.IsFailure)
            return Result.Failure<SaaSPayment>(amountResult.Error);

        var descriptorResult = StatementDescriptor.Create(DefaultStatementDescriptor);
        if (descriptorResult.IsFailure)
            return Result.Failure<SaaSPayment>(descriptorResult.Error);

        return SaaSPayment.CreateForOnboarding(
            command.OnboardingId,
            keyResult.Value,
            amountResult.Value,
            command.PlanId,
            PaymentProviderCode.Stripe,
            descriptorResult.Value,
            DateTime.UtcNow
        );
    }

    private static OnboardingCheckoutResponse? BuildResponse(SaaSPayment payment)
    {
        if (payment.ProviderCheckoutSessionId is null || payment.NextActionUrl is null)
            return null;

        return new OnboardingCheckoutResponse(
            payment.Id,
            payment.NextActionUrl,
            payment.ProviderCheckoutSessionId,
            payment.CreatedAtUtc.Add(SessionLifetime)
        );
    }
}
