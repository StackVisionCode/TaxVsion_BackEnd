using BuildingBlocks.Results;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Domain.Audit;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Application.SaaSPayments.Common;

/// <summary>
/// Aplicar un <see cref="ChargeAuthorizationResult"/> a un <see cref="SaaSPayment"/> es idéntico
/// sin importar si el intento vino de <c>ChargeSaaSPaymentHandler</c> (primer intento) o de
/// <c>RetrySaaSPaymentHandler</c> (dunning). El resultado lo publica <see cref="SaaSPaymentResultPublisher"/>.
/// </summary>
public static class SaaSPaymentChargeOutcome
{
    /// <summary>Solo las renovaciones en background tienen dunning. El cambio de plan es un cargo
    /// interactivo (el fallo se reporta en el acto) y los checkouts hosteados los reintenta el
    /// propio usuario.</summary>
    public static bool SupportsDunning(SaaSPaymentType type) =>
        type is SaaSPaymentType.SubscriptionRenewal or SaaSPaymentType.SeatRenewal or SaaSPaymentType.AddOnRenewal;

    /// <summary>Backoff de dunning: 1h → 6h → 24h → se abandona, indexado por intentos previos al que
    /// falló. El cobro síncrono lo calcula antes de registrar su intento; el webhook y la
    /// reconciliación, con el intento fallido ya registrado (<paramref name="failedAttemptRecorded"/>).</summary>
    public static DateTime? ComputeNextRetryAtUtc(
        SaaSPayment payment,
        DateTime nowUtc,
        bool failedAttemptRecorded = false
    )
    {
        if (!SupportsDunning(payment.Type))
            return null;

        var previousAttempts = payment.Attempts.Count - (failedAttemptRecorded ? 1 : 0);
        return previousAttempts switch
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
