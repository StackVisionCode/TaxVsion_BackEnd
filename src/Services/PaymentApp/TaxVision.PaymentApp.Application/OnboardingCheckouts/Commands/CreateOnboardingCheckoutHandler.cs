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
        var existing = await payments.GetByIdempotencyKeyAsync(command.IdempotencyKey, ct);
        if (existing is not null)
        {
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

        var priceResult = await planPricing.GetMonthlyPriceAsync(command.PlanId, ct);
        if (priceResult.IsFailure)
            return Result.Failure<OnboardingCheckoutResponse>(priceResult.Error);

        var preparedResult = PrepareNewPayment(command, priceResult.Value);
        if (preparedResult.IsFailure)
            return Result.Failure<OnboardingCheckoutResponse>(preparedResult.Error);

        var payment = preparedResult.Value;
        var nowUtc = DateTime.UtcNow;
        var expiresAtUtc = nowUtc.Add(SessionLifetime);

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
            metrics.RecordAttempted(
                PaymentProviderCode.Stripe.ToString(),
                SaaSPaymentType.OnboardingInitial.ToString()
            );
            metrics.RecordFailed(
                PaymentProviderCode.Stripe.ToString(),
                SaaSPaymentType.OnboardingInitial.ToString(),
                sessionResult.Error.Code
            );
            logger.LogWarning(
                "Onboarding checkout session creation failed for onboarding {OnboardingId}. Error={ErrorCode}: {ErrorMessage}",
                command.OnboardingId,
                sessionResult.Error.Code,
                sessionResult.Error.Message
            );
            return Result.Failure<OnboardingCheckoutResponse>(sessionResult.Error);
        }

        var referenceResult = ExternalPaymentReference.Create(
            PaymentProviderCode.Stripe,
            sessionResult.Value.ProviderPaymentIntentReference
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
                sessionResult.Value.ProviderSessionId,
                referenceResult.Error.Code,
                referenceResult.Error.Message
            );
            return Result.Failure<OnboardingCheckoutResponse>(referenceResult.Error);
        }

        var recordResult = payment.RecordHostedCheckoutSession(
            sessionResult.Value.ProviderSessionId,
            referenceResult.Value,
            sessionResult.Value.CheckoutUrl,
            nowUtc
        );
        if (recordResult.IsFailure)
            logger.LogWarning(
                "Onboarding checkout for {OnboardingId} created a Stripe session ({SessionId}) but recording it locally failed: {ErrorCode}: {ErrorMessage}",
                command.OnboardingId,
                sessionResult.Value.ProviderSessionId,
                recordResult.Error.Code,
                recordResult.Error.Message
            );
        if (recordResult.IsFailure)
            return Result.Failure<OnboardingCheckoutResponse>(recordResult.Error);

        await payments.AddAsync(payment, ct);
        metrics.RecordAttempted(PaymentProviderCode.Stripe.ToString(), SaaSPaymentType.OnboardingInitial.ToString());

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
                sessionResult.Value.ProviderSessionId,
            },
            reason: null,
            nowUtc,
            ct
        );

        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation(
            "Onboarding checkout {SaaSPaymentId} created for onboarding {OnboardingId}.",
            payment.Id,
            command.OnboardingId
        );

        return Result.Success(
            new OnboardingCheckoutResponse(
                payment.Id,
                sessionResult.Value.CheckoutUrl,
                sessionResult.Value.ProviderSessionId,
                expiresAtUtc
            )
        );
    }

    private static Result<SaaSPayment> PrepareNewPayment(
        CreateOnboardingCheckoutCommand command,
        PlanMonthlyPrice price
    )
    {
        var keyResult = IdempotencyKey.Create(command.IdempotencyKey);
        if (keyResult.IsFailure)
            return Result.Failure<SaaSPayment>(keyResult.Error);

        var amountResult = Money.Create(price.AmountCents, price.Currency);
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
