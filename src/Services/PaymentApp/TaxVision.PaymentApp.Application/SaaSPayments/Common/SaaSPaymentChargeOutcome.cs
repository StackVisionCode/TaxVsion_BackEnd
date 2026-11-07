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
/// <c>RetrySaaSPaymentHandler</c> (dunning). El resultado lo avisa <c>SaaSPaymentDomainEventHandler</c>.
/// </summary>
public static class SaaSPaymentChargeOutcome
{
    /// <summary>
    /// Customer y tarjeta guardada con los que cobrar. Sin tarjeta guardada no hay cobro automático
    /// posible: se devuelve <see cref="DunningPolicy.MissingPaymentMethodCode"/> y no se llama al provider (antes se
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
                    DunningPolicy.MissingPaymentMethodCode,
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
        DunningDecision retry,
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
                retry,
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
                retry,
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
        DunningDecision retry,
        IPaymentAppMetrics metrics
    ) => FailPayment(payment, error.Code, error.Message, actorUserId, retry, metrics);

    private static void FailPayment(
        SaaSPayment payment,
        string code,
        string message,
        Guid actorUserId,
        DunningDecision retry,
        IPaymentAppMetrics metrics
    )
    {
        var nowUtc = DateTime.UtcNow;
        payment.MarkFailed(code, message, DunningPolicy.ForFailure(code, retry), actorUserId, nowUtc);
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
