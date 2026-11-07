using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Application.OnboardingCheckouts.Commands;

public sealed record ReconcileOnboardingCheckoutCommand(Guid PaymentId);

public sealed record ReconcileOnboardingCheckoutResponse(
    Guid PaymentId,
    OnboardingPaymentStatus Status,
    long AmountPaidCents,
    string Currency,
    string? FailureCode,
    string? FailureMessage,
    string? ProviderPaymentReference,
    DateTime? PaidAtUtc
);

public static class ReconcileOnboardingCheckoutHandler
{
    public static async Task<Result<ReconcileOnboardingCheckoutResponse>> Handle(
        ReconcileOnboardingCheckoutCommand command,
        ISaaSPaymentRepository payments,
        IPaymentAdapterFactory providerFactory,
        IUnitOfWork unitOfWork,
        ILogger<SaaSPayment> logger,
        CancellationToken ct
    )
    {
        var payment = await payments.GetByIdAsync(command.PaymentId, ct);
        if (payment is null)
            return Result.Failure<ReconcileOnboardingCheckoutResponse>(
                new Error("SaaSPayment.NotFound", "Payment not found.")
            );

        if (payment.Type != SaaSPaymentType.OnboardingInitial)
            return Result.Failure<ReconcileOnboardingCheckoutResponse>(
                new Error("Onboarding.Payment.InvalidType", "Payment is not an onboarding checkout.")
            );

        if (IsTerminal(payment.Status))
            return Result.Success(BuildResponse(payment));

        if (payment.ExternalChargeReference is null)
            return Result.Failure<ReconcileOnboardingCheckoutResponse>(
                new Error("Onboarding.Checkout.NoSession", "This checkout has no provider reference.")
            );

        IPaymentProvider adapter;
        try
        {
            adapter = providerFactory.Resolve(payment.ProviderCode);
        }
        catch (InvalidOperationException)
        {
            return Result.Failure<ReconcileOnboardingCheckoutResponse>(
                new Error("PaymentProvider.NotConfigured", "The selected payment provider is not configured.")
            );
        }

        var statusResult = await adapter.FinalizeHostedCheckoutAsync(
            payment.ExternalChargeReference.Value,
            payment.Amount,
            ct
        );
        if (statusResult.IsFailure)
            return Result.Failure<ReconcileOnboardingCheckoutResponse>(statusResult.Error);

        var nowUtc = DateTime.UtcNow;
        var outcome = statusResult.Value;
        ReconcileReferenceIfNeeded(payment, outcome.ProviderChargeReference, logger, nowUtc);

        // El resultado lo avisa SaaSPaymentDomainEventHandler con el commit (evento de dominio).
        var changed = ApplyOutcome(payment, outcome, nowUtc);
        if (changed)
            await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(BuildResponse(payment));
    }

    private static bool ApplyOutcome(SaaSPayment payment, ChargeAuthorizationResult outcome, DateTime nowUtc)
    {
        switch (outcome.Status)
        {
            case PaymentStatus.Succeeded:
                return payment.MarkSucceeded(nowUtc, Guid.Empty).IsSuccess;

            case PaymentStatus.Failed
            or PaymentStatus.Cancelled:
                return payment
                    .MarkFailed(
                        outcome.FailureCode ?? "Provider.Unknown",
                        outcome.FailureMessage ?? "The provider reported the charge as failed.",
                        DunningDecision.NoRetry,
                        Guid.Empty,
                        nowUtc
                    )
                    .IsSuccess;

            default:
                return false;
        }
    }

    private static void ReconcileReferenceIfNeeded(
        SaaSPayment payment,
        string providerChargeReference,
        ILogger<SaaSPayment> logger,
        DateTime nowUtc
    )
    {
        if (
            string.IsNullOrWhiteSpace(providerChargeReference)
            || providerChargeReference == payment.ExternalChargeReference?.Value
        )
            return;

        var referenceResult = ExternalPaymentReference.Create(payment.ProviderCode, providerChargeReference);
        if (referenceResult.IsFailure)
        {
            logger.LogWarning(
                "Provider returned an invalid reconciled charge reference for SaaSPayment {SaaSPaymentId}: {ErrorCode}: {ErrorMessage}",
                payment.Id,
                referenceResult.Error.Code,
                referenceResult.Error.Message
            );
            return;
        }

        var reconcile = payment.ReconcileProviderChargeReference(referenceResult.Value, nowUtc);
        if (reconcile.IsFailure)
            logger.LogWarning(
                "Could not reconcile charge reference for SaaSPayment {SaaSPaymentId}: {ErrorCode}: {ErrorMessage}",
                payment.Id,
                reconcile.Error.Code,
                reconcile.Error.Message
            );
    }

    private static ReconcileOnboardingCheckoutResponse BuildResponse(SaaSPayment payment) =>
        new(
            payment.Id,
            ToContractStatus(payment.Status),
            payment.Amount.AmountCents,
            payment.Amount.Currency,
            payment.FailureCode,
            payment.FailureReason,
            payment.ExternalChargeReference?.Value,
            payment.PaidAtUtc
        );

    private static bool IsTerminal(PaymentStatus status) =>
        status
            is PaymentStatus.Succeeded
                or PaymentStatus.Failed
                or PaymentStatus.Cancelled
                or PaymentStatus.Refunded
                or PaymentStatus.PartiallyRefunded
                or PaymentStatus.ChargedBack;

    private static OnboardingPaymentStatus ToContractStatus(PaymentStatus status) =>
        status switch
        {
            PaymentStatus.Pending => OnboardingPaymentStatus.Pending,
            PaymentStatus.Processing => OnboardingPaymentStatus.Processing,
            PaymentStatus.RequiresAction => OnboardingPaymentStatus.RequiresAction,
            PaymentStatus.Succeeded => OnboardingPaymentStatus.Succeeded,
            PaymentStatus.Failed => OnboardingPaymentStatus.Failed,
            PaymentStatus.Cancelled => OnboardingPaymentStatus.Cancelled,
            PaymentStatus.PartiallyRefunded => OnboardingPaymentStatus.PartiallyRefunded,
            PaymentStatus.Refunded => OnboardingPaymentStatus.Refunded,
            PaymentStatus.ChargedBack => OnboardingPaymentStatus.ChargedBack,
            _ => OnboardingPaymentStatus.Processing,
        };
}
