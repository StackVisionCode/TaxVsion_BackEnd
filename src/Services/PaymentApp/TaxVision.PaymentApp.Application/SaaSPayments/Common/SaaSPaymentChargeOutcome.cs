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
    /// <summary>Mismo código que usa Stripe: Subscription ya lo trata como fallo permanente.</summary>
    public const string MissingPaymentMethodCode = "requires_payment_method";

    /// <summary>Fallos que no se arreglan reintentando: el tenant tiene que pagar a mano ("Pay now").</summary>
    private static readonly HashSet<string> PermanentFailureCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        MissingPaymentMethodCode,
        // PayPal no tiene cobro directo: un reintento automático nunca puede cobrar.
        "PayPal.Authorize.NotImplemented",
    };

    public static bool IsPermanentFailure(string? failureCode) =>
        failureCode is not null && PermanentFailureCodes.Contains(failureCode);

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

    /// <summary>
    /// Customer y tarjeta guardada con los que cobrar. Sin tarjeta guardada no hay cobro automático
    /// posible: se devuelve <see cref="MissingPaymentMethodCode"/> y no se llama al provider (antes se
    /// creaba un PaymentIntent sin tarjeta que quedaba "Incomplete" para siempre).
    /// </summary>
    public static async Task<Result<(ProviderCustomerToken Customer, PaymentMethodToken Method)>> ResolvePayerAsync(
        Guid tenantId,
        ITenantProviderCustomerRepository customers,
        IPaymentProvider adapter,
        CancellationToken ct
    )
    {
        var savedCustomer = await customers.GetByTenantAndProviderAsync(tenantId, adapter.Code, ct);
        var defaultMethod = savedCustomer?.GetDefaultMethod();
        if (savedCustomer is null || defaultMethod is null)
            return Result.Failure<(ProviderCustomerToken, PaymentMethodToken)>(
                new Error(
                    MissingPaymentMethodCode,
                    "No saved payment method; the office has to pay from the billing page."
                )
            );

        var token = new ProviderCustomerToken(savedCustomer.CustomerReference.Value, savedCustomer.ProviderCode);
        return Result.Success((token, new PaymentMethodToken(defaultMethod.MethodReference)));
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
        var retryAtUtc = IsPermanentFailure(code) ? null : nextRetryAtUtc;
        payment.MarkFailed(code, message, willRetry: retryAtUtc is not null, retryAtUtc, actorUserId, nowUtc);
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
