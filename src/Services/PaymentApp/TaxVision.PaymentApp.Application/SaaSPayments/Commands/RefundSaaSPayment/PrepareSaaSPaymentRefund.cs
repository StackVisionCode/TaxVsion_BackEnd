using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Application.SaaSPayments.Common;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Application.SaaSPayments.Commands.RefundSaaSPayment;

/// <summary>
/// Paso 1 del reembolso (BB-8): en una transacción corta, valida y guarda la intención con su clave de proveedor antes
/// de llamarlo. Si la misma petición ya tiene intención, la reusa (misma clave) o la da por hecha.
/// <paramref name="AmountCents"/> null reembolsa el saldo. <paramref name="ReplayWindow"/> null: una petición
/// completada se repite para siempre (cabecera u onboarding); con valor, solo dentro de ese plazo (huella).
/// </summary>
public sealed record PrepareSaaSPaymentRefundCommand(
    Guid SaaSPaymentId,
    long? AmountCents,
    string Reason,
    Guid ActorUserId,
    string RequestKey,
    string? ProviderIdempotencyKey,
    TimeSpan? ReplayWindow,
    bool EnforceAdminThrottle
);

public enum SaaSPaymentRefundPlanKind
{
    /// <summary>Hay que llamar al proveedor con esta clave.</summary>
    Execute = 1,

    /// <summary>La misma petición ya se registró: no se llama al proveedor.</summary>
    AlreadyCompleted = 2,

    /// <summary>La petición pudo reembolsar sin registro: se concilia, nunca se reenvía.</summary>
    NeedsReconciliation = 3,
}

/// <summary>Lo que el proveedor tiene que recibir. Sale de la intención guardada: es igual en cada reintento.</summary>
public sealed record SaaSPaymentRefundPlan(
    SaaSPaymentRefundPlanKind Kind,
    Guid TenantId,
    Guid RequestId,
    PaymentProviderCode Provider,
    string ChargeReference,
    Money Amount,
    string Reason,
    string ProviderKey
);

public static class PrepareSaaSPaymentRefundHandler
{
    public static async Task<Result<SaaSPaymentRefundPlan>> Handle(
        PrepareSaaSPaymentRefundCommand command,
        ISaaSPaymentRepository payments,
        IPaymentAdapterFactory providerFactory,
        IPaymentAttemptThrottle throttle,
        IPaymentAuditLogWriter audit,
        IUnitOfWork unitOfWork,
        IPaymentAppMetrics metrics,
        IOptions<SaaSPaymentChargeOptions> chargeOptions,
        ICorrelationContext correlation,
        ILogger<SaaSPayment> logger,
        CancellationToken ct
    )
    {
        // Sin tenant: el reembolso es de plataforma y el pago de onboarding puede no tenerlo.
        var payment = await payments.GetByIdAsync(command.SaaSPaymentId, ct);
        if (payment is null)
            return Failure(new Error("SaaSPayment.NotFound", "SaaSPayment does not exist."));

        // Defensa en profundidad: más de 5 acciones de dinero por minuto sobre un tenant es automatización o abuso.
        if (command.EnforceAdminThrottle && await throttle.IsAdminActionThrottledAsync(payment.TenantId, ct))
            return Failure(
                new Error(
                    "PaymentApp.AdminActionThrottled",
                    "Too many admin actions for this tenant in the last minute."
                )
            );

        if (payment.ExternalChargeReference is null)
            return Failure(
                new Error(
                    "SaaSPayment.NoChargeReference",
                    "This payment was never charged with a provider — nothing to refund."
                )
            );

        var nowUtc = DateTime.UtcNow;
        var existing = payment.FindRefundRequest(command.RequestKey);
        if (existing is not null)
        {
            var replayable =
                command.ReplayWindow is not { } window
                || nowUtc - (existing.ResolvedAtUtc ?? existing.CreatedAtUtc) <= window;

            switch (existing.Status)
            {
                case RefundRequestStatus.Pending:
                    return await ReusePendingAsync(
                        payment,
                        existing,
                        providerFactory,
                        audit,
                        unitOfWork,
                        metrics,
                        chargeOptions.Value,
                        correlation,
                        logger,
                        nowUtc,
                        ct
                    );
                case RefundRequestStatus.Completed when replayable:
                    return Plan(SaaSPaymentRefundPlanKind.AlreadyCompleted, payment, existing);
                case RefundRequestStatus.NeedsReconciliation when replayable:
                    return Plan(SaaSPaymentRefundPlanKind.NeedsReconciliation, payment, existing);
            }
        }

        var amount = command.AmountCents is { } cents
            ? Money.Create(cents, payment.Amount.Currency)
            : payment.CanRefundFull(command.Reason);
        if (amount.IsFailure)
            return Failure(amount.Error);

        var opened = payment.OpenRefundRequest(
            amount.Value,
            command.Reason,
            command.ActorUserId,
            command.RequestKey,
            command.ProviderIdempotencyKey,
            nowUtc
        );
        if (opened.IsFailure)
            return Failure(opened.Error);

        await unitOfWork.SaveChangesAsync(ct);
        return Plan(SaaSPaymentRefundPlanKind.Execute, payment, opened.Value);
    }

    // Una intención sin resultado se repite con la misma clave, salvo que el proveedor ya no la recuerde o no use claves.
    private static async Task<Result<SaaSPaymentRefundPlan>> ReusePendingAsync(
        SaaSPayment payment,
        RefundRequest pending,
        IPaymentAdapterFactory providerFactory,
        IPaymentAuditLogWriter audit,
        IUnitOfWork unitOfWork,
        IPaymentAppMetrics metrics,
        SaaSPaymentChargeOptions chargeOptions,
        ICorrelationContext correlation,
        ILogger logger,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        var supportsKeys = providerFactory.Resolve(payment.ProviderCode).Capabilities.SupportsIdempotencyKeys;
        var keyExpired = nowUtc - pending.CreatedAtUtc > chargeOptions.ProviderKeyWindow;
        if (supportsKeys && !keyExpired)
            return Plan(SaaSPaymentRefundPlanKind.Execute, payment, pending);

        await RefundReconciliation.MarkAsync(
            payment,
            pending.Id,
            providerRefundReference: null,
            keyExpired ? "SaaSPayment.RefundKeyWindowElapsed" : "SaaSPayment.RefundOutcomeUnknown",
            audit,
            metrics,
            correlation,
            logger,
            nowUtc,
            ct
        );
        await unitOfWork.SaveChangesAsync(ct);
        return Plan(SaaSPaymentRefundPlanKind.NeedsReconciliation, payment, pending);
    }

    private static Result<SaaSPaymentRefundPlan> Plan(
        SaaSPaymentRefundPlanKind kind,
        SaaSPayment payment,
        RefundRequest request
    ) =>
        Result.Success(
            new SaaSPaymentRefundPlan(
                kind,
                payment.TenantId,
                request.Id,
                payment.ProviderCode,
                payment.ExternalChargeReference!.Value,
                request.Amount,
                request.Reason,
                request.ProviderIdempotencyKey
            )
        );

    private static Result<SaaSPaymentRefundPlan> Failure(Error error) => Result.Failure<SaaSPaymentRefundPlan>(error);
}
