using BuildingBlocks.Results;
using TaxVision.PaymentClient.Application.Abstractions.Payments;
using TaxVision.PaymentClient.Domain.Audit;
using TaxVision.PaymentClient.Domain.TenantPayments;
using TaxVision.PaymentClient.Domain.ValueObjects;

namespace TaxVision.PaymentClient.Application.TenantPayments.Common;

/// <summary>
/// Aplicar un <see cref="ChargeAuthorizationResult"/> a un <see cref="TenantPayment"/> — mismo
/// código sin importar si el intento vino del endpoint de cobro (E.6) o de un futuro retry, así
/// que vive acá una sola vez.
/// </summary>
public static class TenantPaymentChargeOutcome
{
    public static void ApplyChargeOutcome(TenantPayment payment, ChargeAuthorizationResult outcome, Guid actorUserId)
    {
        var nowUtc = DateTime.UtcNow;

        if (outcome.Status == PaymentStatus.Failed)
        {
            FailPayment(payment, outcome.FailureCode ?? "Provider.Unknown", outcome.FailureMessage ?? "The provider declined the charge.", actorUserId);
            return;
        }

        var referenceResult = ExternalPaymentReference.Create(payment.ProviderCode, outcome.ProviderChargeReference);
        if (referenceResult.IsFailure)
        {
            FailPayment(payment, "Provider.InvalidReference", referenceResult.Error.Message, actorUserId);
            return;
        }

        payment.MarkProcessing(referenceResult.Value, outcome.Status.ToString(), providerResponseBody: null, actorUserId, nowUtc);

        if (outcome.Status == PaymentStatus.Succeeded)
        {
            payment.MarkSucceeded(nowUtc, actorUserId);
        }
        else if (outcome.Status == PaymentStatus.RequiresAction)
        {
            payment.MarkRequiresAction(outcome.NextActionType ?? "unknown", outcome.NextActionUrl ?? string.Empty, actorUserId, nowUtc);
        }
    }

    /// <summary>Equivalente de <see cref="ApplyChargeOutcome"/> para un cobro en modo
    /// <see cref="TaxVision.PaymentClient.Domain.TenantPaymentConfigs.TenantPaymentMode.Connect"/>
    /// — usa <c>TenantPayment.MarkProcessingViaConnect</c> en vez de <c>MarkProcessing</c> para
    /// poblar <c>ProviderChargeReferenceOnConnect</c>/<c>SplitPayment</c>.</summary>
    public static void ApplyChargeOutcomeViaConnect(TenantPayment payment, ChargeAuthorizationResult outcome, SplitPaymentBreakdown split, Guid actorUserId)
    {
        var nowUtc = DateTime.UtcNow;

        if (outcome.Status == PaymentStatus.Failed)
        {
            FailPayment(payment, outcome.FailureCode ?? "Provider.Unknown", outcome.FailureMessage ?? "The provider declined the charge.", actorUserId);
            return;
        }

        if (string.IsNullOrWhiteSpace(outcome.ProviderChargeReference))
        {
            FailPayment(payment, "Provider.InvalidReference", "Provider did not return a charge reference.", actorUserId);
            return;
        }

        var processingResult = payment.MarkProcessingViaConnect(
            outcome.ProviderChargeReference, split, outcome.Status.ToString(), providerResponseBody: null, actorUserId, nowUtc);
        if (processingResult.IsFailure)
        {
            FailPayment(payment, processingResult.Error, actorUserId);
            return;
        }

        if (outcome.Status == PaymentStatus.Succeeded)
        {
            payment.MarkSucceeded(nowUtc, actorUserId);
        }
        else if (outcome.Status == PaymentStatus.RequiresAction)
        {
            payment.MarkRequiresAction(outcome.NextActionType ?? "unknown", outcome.NextActionUrl ?? string.Empty, actorUserId, nowUtc);
        }
    }

    public static void FailPayment(TenantPayment payment, Error error, Guid actorUserId) =>
        FailPayment(payment, error.Code, error.Message, actorUserId);

    private static void FailPayment(TenantPayment payment, string code, string message, Guid actorUserId) =>
        payment.MarkFailed(code, message, willRetry: false, nextRetryAtUtc: null, actorUserId, DateTime.UtcNow);

    public static PaymentAuditAction MapAuditAction(PaymentStatus status) => status switch
    {
        PaymentStatus.Succeeded => PaymentAuditAction.TenantPaymentSucceeded,
        PaymentStatus.Failed => PaymentAuditAction.TenantPaymentFailed,
        PaymentStatus.RequiresAction => PaymentAuditAction.TenantPaymentRequiresAction,
        PaymentStatus.Processing => PaymentAuditAction.TenantPaymentMarkedProcessing,
        _ => PaymentAuditAction.TenantPaymentCreated,
    };
}
