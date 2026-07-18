using BuildingBlocks.Common;
using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using BuildingBlocks.Results;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Domain.Audit;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.PaymentApp.Application.SaaSPayments.Common;

/// <summary>
/// Aplicar un <see cref="ChargeAuthorizationResult"/> a un <see cref="SaaSPayment"/> y
/// publicar el evento de resultado correspondiente es idéntico sin importar si el intento
/// vino de <c>ChargeSaaSPaymentHandler</c> (primer intento) o de
/// <c>RetrySaaSPaymentHandler</c> (dunning) — vive acá una sola vez.
/// </summary>
public static class SaaSPaymentChargeOutcome
{
    /// <summary>Backoff de dunning: 1h → 6h → 24h → se abandona. Indexado por
    /// <see cref="SaaSPayment.Attempts"/>.Count, así que el mismo cálculo sirve tanto para
    /// el primer intento (0 attempts todavía) como para cada retry posterior.
    /// <see cref="SaaSPaymentType.PlanChangeCharge"/> es la excepción: es un cargo interactivo
    /// iniciado por el usuario (un upgrade de plan), no una renovación en background — un solo
    /// intento, sin dunning, para que el fallo se reporte rápido en vez de reintentarse en
    /// silencio horas después.</summary>
    public static DateTime? ComputeNextRetryAtUtc(SaaSPayment payment, DateTime nowUtc)
    {
        if (payment.Type == SaaSPaymentType.PlanChangeCharge)
            return null;

        return payment.Attempts.Count switch
        {
            0 => nowUtc.AddHours(1),
            1 => nowUtc.AddHours(6),
            2 => nowUtc.AddHours(24),
            _ => null,
        };
    }

    /// <summary>Resuelve con qué customer y método de pago cargar. Si el tenant ya tiene un
    /// <c>TenantProviderCustomer</c> con un método default (§D), un cobro automático puede
    /// correr sin interacción — esto es lo que cierra el gap de "no hay tarjeta guardada"
    /// que Fase A/B/C dejaron documentado. Si no existe todavía, cae al mismo
    /// GetOrCreateCustomerAsync con email sintético que Fase A usaba (el cobro fallará si el
    /// provider exige un payment method explícito, igual que antes — comportamiento sin
    /// regresión).</summary>
    public static async Task<Result<(ProviderCustomerToken Customer, PaymentMethodToken? Method)>> ResolvePayerAsync(
        Guid tenantId,
        string fallbackEmail,
        string? fallbackName,
        ITenantProviderCustomerRepository customers,
        IPaymentProvider adapter,
        CancellationToken ct
    )
    {
        var savedCustomer = await customers.GetByTenantAndProviderAsync(tenantId, adapter.Code, ct);
        if (savedCustomer is not null)
        {
            var token = new ProviderCustomerToken(savedCustomer.CustomerReference.Value, savedCustomer.ProviderCode);
            var defaultMethod = savedCustomer.GetDefaultMethod();
            var methodToken = defaultMethod is null ? null : new PaymentMethodToken(defaultMethod.MethodReference);
            return Result.Success((token, methodToken));
        }

        var tokenResult = await adapter.GetOrCreateCustomerAsync(tenantId, fallbackEmail, fallbackName, ct);
        return tokenResult.IsFailure
            ? Result.Failure<(ProviderCustomerToken, PaymentMethodToken?)>(tokenResult.Error)
            : Result.Success((tokenResult.Value, (PaymentMethodToken?)null));
    }

    public static void ApplyChargeOutcome(
        SaaSPayment payment,
        ChargeAuthorizationResult outcome,
        Guid actorUserId,
        DateTime? nextRetryAtUtc,
        IPaymentAppMetrics metrics
    )
    {
        var nowUtc = DateTime.UtcNow;

        if (outcome.Status == PaymentStatus.Failed)
        {
            FailPayment(
                payment,
                outcome.FailureCode ?? "Provider.Unknown",
                outcome.FailureMessage ?? "The provider declined the charge.",
                actorUserId,
                nextRetryAtUtc,
                metrics
            );
            return;
        }

        var referenceResult = ExternalPaymentReference.Create(payment.ProviderCode, outcome.ProviderChargeReference);
        if (referenceResult.IsFailure)
        {
            FailPayment(
                payment,
                "Provider.InvalidReference",
                referenceResult.Error.Message,
                actorUserId,
                nextRetryAtUtc,
                metrics
            );
            return;
        }

        payment.MarkProcessing(
            referenceResult.Value,
            outcome.Status.ToString(),
            providerResponseBody: null,
            actorUserId,
            nowUtc
        );

        if (outcome.Status == PaymentStatus.Succeeded)
        {
            payment.MarkSucceeded(nowUtc, actorUserId);
            metrics.RecordSucceeded(payment.ProviderCode.ToString(), payment.Type.ToString());
        }
        else if (outcome.Status == PaymentStatus.RequiresAction)
        {
            payment.MarkRequiresAction(
                outcome.NextActionType ?? "unknown",
                outcome.NextActionUrl ?? string.Empty,
                actorUserId,
                nowUtc
            );
        }
    }

    public static void FailPayment(
        SaaSPayment payment,
        Error error,
        Guid actorUserId,
        DateTime? nextRetryAtUtc,
        IPaymentAppMetrics metrics
    ) => FailPayment(payment, error.Code, error.Message, actorUserId, nextRetryAtUtc, metrics);

    private static void FailPayment(
        SaaSPayment payment,
        string code,
        string message,
        Guid actorUserId,
        DateTime? nextRetryAtUtc,
        IPaymentAppMetrics metrics
    )
    {
        var nowUtc = DateTime.UtcNow;
        payment.MarkFailed(code, message, willRetry: nextRetryAtUtc is not null, nextRetryAtUtc, actorUserId, nowUtc);
        metrics.RecordFailed(payment.ProviderCode.ToString(), payment.Type.ToString(), code);
    }

    /// <summary>Despacha el resultado al evento de integración correspondiente según
    /// <see cref="SaaSPayment.Type"/>. Cada renewal type (suscripción base, seat, add-on)
    /// tiene su propio par Succeeded/Failed — Subscription trata cada uno de forma
    /// independiente (renovar un seat no renueva la suscripción base). No-op si el pago no
    /// llegó a un estado terminal (p.ej. quedó RequiresAction).</summary>
    public static ValueTask PublishResultAsync(
        SaaSPayment payment,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        if (payment.Status != PaymentStatus.Succeeded && payment.Status != PaymentStatus.Failed)
            return ValueTask.CompletedTask;

        return payment.Type switch
        {
            SaaSPaymentType.SubscriptionRenewal => PublishSubscriptionRenewalResultAsync(payment, bus, correlation),
            SaaSPaymentType.SeatRenewal => PublishSeatRenewalResultAsync(payment, bus, correlation),
            SaaSPaymentType.AddOnRenewal => PublishAddOnRenewalResultAsync(payment, bus, correlation),
            SaaSPaymentType.PlanChangeCharge => PublishPlanChangeResultAsync(payment, bus, correlation),
            _ => ValueTask.CompletedTask,
        };
    }

    private static ValueTask PublishSubscriptionRenewalResultAsync(
        SaaSPayment payment,
        IMessageBus bus,
        ICorrelationContext correlation
    ) =>
        payment.Status == PaymentStatus.Succeeded
            ? bus.PublishAsync(
                new SubscriptionRenewalPaymentSucceededIntegrationEvent
                {
                    TenantId = payment.TenantId,
                    TenantSubscriptionId = payment.TargetAggregateId,
                    SaaSPaymentId = payment.Id,
                    IdempotencyKey = payment.IdempotencyKey.Value,
                    ExternalPaymentReference = payment.ExternalChargeReference?.Value ?? string.Empty,
                    PaidAtUtc = payment.PaidAtUtc ?? DateTime.UtcNow,
                    CorrelationId = correlation.CorrelationId,
                }
            )
            : bus.PublishAsync(
                new SubscriptionRenewalPaymentFailedIntegrationEvent
                {
                    TenantId = payment.TenantId,
                    TenantSubscriptionId = payment.TargetAggregateId,
                    SaaSPaymentId = payment.Id,
                    IdempotencyKey = payment.IdempotencyKey.Value,
                    FailureCode = payment.FailureCode ?? "Unknown",
                    FailureReason = payment.FailureReason ?? "The charge failed.",
                    WillRetry = payment.NextRetryAtUtc is not null,
                    NextRetryAtUtc = payment.NextRetryAtUtc,
                    CorrelationId = correlation.CorrelationId,
                }
            );

    private static ValueTask PublishSeatRenewalResultAsync(
        SaaSPayment payment,
        IMessageBus bus,
        ICorrelationContext correlation
    ) =>
        payment.Status == PaymentStatus.Succeeded
            ? bus.PublishAsync(
                new SeatRenewalPaymentSucceededIntegrationEvent
                {
                    TenantId = payment.TenantId,
                    SeatId = payment.TargetAggregateId,
                    SaaSPaymentId = payment.Id,
                    IdempotencyKey = payment.IdempotencyKey.Value,
                    ExternalPaymentReference = payment.ExternalChargeReference?.Value ?? string.Empty,
                    PaidAtUtc = payment.PaidAtUtc ?? DateTime.UtcNow,
                    CorrelationId = correlation.CorrelationId,
                }
            )
            : bus.PublishAsync(
                new SeatRenewalPaymentFailedIntegrationEvent
                {
                    TenantId = payment.TenantId,
                    SeatId = payment.TargetAggregateId,
                    SaaSPaymentId = payment.Id,
                    IdempotencyKey = payment.IdempotencyKey.Value,
                    FailureCode = payment.FailureCode ?? "Unknown",
                    FailureReason = payment.FailureReason ?? "The charge failed.",
                    WillRetry = payment.NextRetryAtUtc is not null,
                    NextRetryAtUtc = payment.NextRetryAtUtc,
                    CorrelationId = correlation.CorrelationId,
                }
            );

    private static ValueTask PublishAddOnRenewalResultAsync(
        SaaSPayment payment,
        IMessageBus bus,
        ICorrelationContext correlation
    ) =>
        payment.Status == PaymentStatus.Succeeded
            ? bus.PublishAsync(
                new AddOnRenewalPaymentSucceededIntegrationEvent
                {
                    TenantId = payment.TenantId,
                    TenantAddOnId = payment.TargetAggregateId,
                    SaaSPaymentId = payment.Id,
                    IdempotencyKey = payment.IdempotencyKey.Value,
                    ExternalPaymentReference = payment.ExternalChargeReference?.Value ?? string.Empty,
                    PaidAtUtc = payment.PaidAtUtc ?? DateTime.UtcNow,
                    CorrelationId = correlation.CorrelationId,
                }
            )
            : bus.PublishAsync(
                new AddOnRenewalPaymentFailedIntegrationEvent
                {
                    TenantId = payment.TenantId,
                    TenantAddOnId = payment.TargetAggregateId,
                    SaaSPaymentId = payment.Id,
                    IdempotencyKey = payment.IdempotencyKey.Value,
                    FailureCode = payment.FailureCode ?? "Unknown",
                    FailureReason = payment.FailureReason ?? "The charge failed.",
                    WillRetry = payment.NextRetryAtUtc is not null,
                    NextRetryAtUtc = payment.NextRetryAtUtc,
                    CorrelationId = correlation.CorrelationId,
                }
            );

    /// <summary>A diferencia de los Renewal*, no lleva WillRetry/NextRetryAtUtc — un upgrade
    /// de plan es un cargo interactivo iniciado por el usuario, no dunning en background (ver
    /// override de <see cref="ComputeNextRetryAtUtc"/> para este tipo en ChargeSaaSPaymentHandler/
    /// RetrySaaSPaymentHandler, que siempre pasa null). payment.TargetAggregateId es el
    /// PlanChangeRequestId — así Subscription no necesita ningún campo extra para ubicar el
    /// request de vuelta.</summary>
    private static ValueTask PublishPlanChangeResultAsync(
        SaaSPayment payment,
        IMessageBus bus,
        ICorrelationContext correlation
    ) =>
        payment.Status == PaymentStatus.Succeeded
            ? bus.PublishAsync(
                new SubscriptionPlanChangePaymentSucceededIntegrationEvent
                {
                    TenantId = payment.TenantId,
                    PlanChangeRequestId = payment.TargetAggregateId,
                    SaaSPaymentId = payment.Id,
                    IdempotencyKey = payment.IdempotencyKey.Value,
                    ExternalPaymentReference = payment.ExternalChargeReference?.Value ?? string.Empty,
                    PaidAtUtc = payment.PaidAtUtc ?? DateTime.UtcNow,
                    CorrelationId = correlation.CorrelationId,
                }
            )
            : bus.PublishAsync(
                new SubscriptionPlanChangePaymentFailedIntegrationEvent
                {
                    TenantId = payment.TenantId,
                    PlanChangeRequestId = payment.TargetAggregateId,
                    SaaSPaymentId = payment.Id,
                    IdempotencyKey = payment.IdempotencyKey.Value,
                    FailureCode = payment.FailureCode ?? "Unknown",
                    FailureReason = payment.FailureReason ?? "The charge failed.",
                    CorrelationId = correlation.CorrelationId,
                }
            );

    public static PaymentAuditAction MapAuditAction(PaymentStatus status) =>
        status switch
        {
            PaymentStatus.Succeeded => PaymentAuditAction.SaaSPaymentSucceeded,
            PaymentStatus.Failed => PaymentAuditAction.SaaSPaymentFailed,
            PaymentStatus.RequiresAction => PaymentAuditAction.SaaSPaymentRequiresAction,
            PaymentStatus.Processing => PaymentAuditAction.SaaSPaymentMarkedProcessing,
            _ => PaymentAuditAction.SaaSPaymentCreated,
        };
}
