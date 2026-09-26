using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Domain.Audit;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Application.Common.HostedCheckout;

/// <summary>
/// Pasos comunes a todo checkout hosteado por redirect: capabilities del proveedor, idempotencia,
/// sesión de 24 h, registro en el agregado, métricas, auditoría y respuesta. Lo que cambia por tipo
/// (descriptor, monto, creación del agregado, política de idempotencia y metadata) vive en la
/// <see cref="HostedCheckoutPolicy"/> que trae cada handler. El webhook sigue siendo el source of
/// truth: acá no se publica ningún evento.
/// </summary>
public static class HostedCheckoutPipeline
{
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(24);

    public static async Task<Result<HostedCheckoutResult>> RunAsync(
        HostedCheckoutRequest request,
        HostedCheckoutPolicy policy,
        ISaaSPaymentRepository payments,
        IPaymentAdapterFactory providerFactory,
        IPaymentAuditLogWriter audit,
        IUnitOfWork unitOfWork,
        IPaymentAppMetrics metrics,
        ICorrelationContext correlation,
        ILogger logger,
        CancellationToken ct
    )
    {
        var providerResult = await ResolveProviderAsync(request, policy, providerFactory, ct);
        if (providerResult.IsFailure)
            return Result.Failure<HostedCheckoutResult>(providerResult.Error);

        var nowUtc = DateTime.UtcNow;
        var resolution = await ResolvePaymentAsync(request, policy, payments, nowUtc, logger, ct);
        if (resolution.IsFailure)
            return Result.Failure<HostedCheckoutResult>(resolution.Error);
        if (resolution.Value.Replay is { } replay)
            return Result.Success(replay);

        var payment = resolution.Value.Payment!;
        var expiresAtUtc = nowUtc.Add(SessionLifetime);

        var sessionResult = await CreateSessionAsync(
            request,
            policy,
            payment,
            providerResult.Value,
            expiresAtUtc,
            logger,
            ct
        );
        TrackSessionOutcome(sessionResult, request.Provider.ToString(), policy.PaymentType, metrics);
        if (sessionResult.IsFailure)
            return Result.Failure<HostedCheckoutResult>(sessionResult.Error);

        return await FinalizeAsync(
            request,
            policy,
            payment,
            resolution.Value.IsNew,
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

    /// <summary>La respuesta que se puede devolver de un pago ya existente, o null si no tiene sesión usable.</summary>
    public static HostedCheckoutResult? TryBuildResponse(SaaSPayment payment)
    {
        if (payment.ProviderCheckoutSessionId is null || payment.NextActionUrl is null)
            return null;

        return new HostedCheckoutResult(
            payment.Id,
            payment.NextActionUrl,
            payment.ProviderCheckoutSessionId,
            payment.CreatedAtUtc.Add(SessionLifetime)
        );
    }

    private static async Task<Result<IPaymentProvider>> ResolveProviderAsync(
        HostedCheckoutRequest request,
        HostedCheckoutPolicy policy,
        IPaymentAdapterFactory providerFactory,
        CancellationToken ct
    )
    {
        if (policy.PreCheck is { } preCheck)
        {
            var precondition = await preCheck(ct);
            if (precondition.IsFailure)
                return Result.Failure<IPaymentProvider>(precondition.Error);
        }

        IPaymentProvider provider;
        try
        {
            provider = providerFactory.Resolve(request.Provider);
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
                    "The selected payment provider does not support hosted checkout."
                )
            );

        if (!provider.Capabilities.SupportedMethods.Contains(request.Method))
            return Result.Failure<IPaymentProvider>(
                new Error(
                    "PaymentMethod.UnsupportedByProvider",
                    "The selected payment method is not supported by this provider."
                )
            );

        return Result.Success(provider);
    }

    private sealed record PaymentResolution(HostedCheckoutResult? Replay, SaaSPayment? Payment, bool IsNew);

    private static async Task<Result<PaymentResolution>> ResolvePaymentAsync(
        HostedCheckoutRequest request,
        HostedCheckoutPolicy policy,
        ISaaSPaymentRepository payments,
        DateTime nowUtc,
        ILogger logger,
        CancellationToken ct
    )
    {
        var existing = await payments.GetByIdempotencyKeyAsync(request.RequestKey, ct);
        if (existing is not null)
            return ResolveExisting(request, policy, existing, nowUtc, logger);

        var amountResult = await policy.ResolveAmount(ct);
        if (amountResult.IsFailure)
            return Result.Failure<PaymentResolution>(amountResult.Error);

        var prepared = PrepareNewPayment(request, policy, amountResult.Value, nowUtc);
        return prepared.IsFailure
            ? Result.Failure<PaymentResolution>(prepared.Error)
            : Result.Success(new PaymentResolution(null, prepared.Value, IsNew: true));
    }

    private static Result<PaymentResolution> ResolveExisting(
        HostedCheckoutRequest request,
        HostedCheckoutPolicy policy,
        SaaSPayment existing,
        DateTime nowUtc,
        ILogger logger
    )
    {
        var decision = policy.DecideOnExisting(existing, nowUtc);
        if (decision.IsFailure)
        {
            logger.LogWarning(
                "{Subject} for IdempotencyKey {Key} cannot be reused from status {Status}: {ErrorCode}.",
                policy.Subject,
                request.RequestKey,
                existing.Status,
                decision.Error.Code
            );
            return Result.Failure<PaymentResolution>(decision.Error);
        }

        if (decision.Value.Reuse is { } reused)
            return Result.Success(new PaymentResolution(null, reused, IsNew: false));

        logger.LogInformation(
            "{Subject} already exists for IdempotencyKey {Key}; replaying (idempotent).",
            policy.Subject,
            request.RequestKey
        );
        return Result.Success(new PaymentResolution(decision.Value.Response, null, IsNew: false));
    }

    private static Result<SaaSPayment> PrepareNewPayment(
        HostedCheckoutRequest request,
        HostedCheckoutPolicy policy,
        Money amount,
        DateTime nowUtc
    )
    {
        var keyResult = IdempotencyKey.Create(request.RequestKey);
        if (keyResult.IsFailure)
            return Result.Failure<SaaSPayment>(keyResult.Error);

        var descriptorResult = StatementDescriptor.Create(policy.StatementDescriptor);
        if (descriptorResult.IsFailure)
            return Result.Failure<SaaSPayment>(descriptorResult.Error);

        return policy.CreatePayment(keyResult.Value, amount, descriptorResult.Value, nowUtc);
    }

    private static async Task<Result<HostedCheckoutSessionResult>> CreateSessionAsync(
        HostedCheckoutRequest request,
        HostedCheckoutPolicy policy,
        SaaSPayment payment,
        IPaymentProvider adapter,
        DateTime expiresAtUtc,
        ILogger logger,
        CancellationToken ct
    )
    {
        var providerKeyResult = policy.ProviderKey(payment);
        if (providerKeyResult.IsFailure)
            return Result.Failure<HostedCheckoutSessionResult>(providerKeyResult.Error);

        var sessionRequest = new HostedCheckoutSessionRequest(
            Amount: payment.Amount,
            Method: request.Method,
            IdempotencyKey: providerKeyResult.Value,
            Descriptor: payment.StatementDescriptor,
            PayerEmail: request.PayerEmail,
            SuccessUrl: request.SuccessUrl,
            CancelUrl: request.CancelUrl,
            ExpiresAtUtc: expiresAtUtc,
            Metadata: policy.BuildMetadata(payment)
        );

        var sessionResult = await adapter.CreateHostedCheckoutSessionAsync(sessionRequest, ct);
        if (sessionResult.IsFailure)
            logger.LogWarning(
                "{Subject} session creation failed for {ReferenceId}. Error={ErrorCode}: {ErrorMessage}",
                policy.Subject,
                policy.ReferenceId,
                sessionResult.Error.Code,
                sessionResult.Error.Message
            );

        return sessionResult;
    }

    private static void TrackSessionOutcome(
        Result<HostedCheckoutSessionResult> sessionResult,
        string provider,
        SaaSPaymentType paymentType,
        IPaymentAppMetrics metrics
    )
    {
        metrics.RecordAttempted(provider, paymentType.ToString());
        if (sessionResult.IsFailure)
            metrics.RecordFailed(provider, paymentType.ToString(), sessionResult.Error.Code);
    }

    private static async Task<Result<HostedCheckoutResult>> FinalizeAsync(
        HostedCheckoutRequest request,
        HostedCheckoutPolicy policy,
        SaaSPayment payment,
        bool isNew,
        HostedCheckoutSessionResult session,
        DateTime nowUtc,
        DateTime expiresAtUtc,
        ISaaSPaymentRepository payments,
        IPaymentAuditLogWriter audit,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger logger,
        CancellationToken ct
    )
    {
        var recordResult = RecordSession(policy, payment, session, request.Provider, nowUtc, logger);
        if (recordResult.IsFailure)
            return Result.Failure<HostedCheckoutResult>(recordResult.Error);

        // Reintento en sitio: el agregado ya está rastreado (se cargó por clave), solo se persiste.
        if (isNew)
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
            after: policy.BuildAuditPayload(payment, session),
            reason: null,
            nowUtc,
            ct
        );
        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation(
            "{Subject} {SaaSPaymentId} created for {ReferenceId}.",
            policy.Subject,
            payment.Id,
            policy.ReferenceId
        );

        return Result.Success(
            new HostedCheckoutResult(payment.Id, session.CheckoutUrl, session.ProviderSessionId, expiresAtUtc)
        );
    }

    private static Result RecordSession(
        HostedCheckoutPolicy policy,
        SaaSPayment payment,
        HostedCheckoutSessionResult session,
        PaymentProviderCode provider,
        DateTime nowUtc,
        ILogger logger
    )
    {
        var referenceResult = ExternalPaymentReference.Create(provider, session.ProviderPaymentIntentReference);
        if (referenceResult.IsFailure)
        {
            // El proveedor YA creó la sesión (consumió la clave) y acá no quedaría rastro local: sin este
            // log, el próximo intento con la misma clave choca contra el proveedor sin ninguna pista.
            logger.LogWarning(
                "{Subject} for {ReferenceId} created a session ({SessionId}) but its payment reference was invalid: {ErrorCode}: {ErrorMessage}",
                policy.Subject,
                policy.ReferenceId,
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
                "{Subject} for {ReferenceId} created a session ({SessionId}) but recording it locally failed: {ErrorCode}: {ErrorMessage}",
                policy.Subject,
                policy.ReferenceId,
                session.ProviderSessionId,
                recordResult.Error.Code,
                recordResult.Error.Message
            );

        return recordResult;
    }
}
