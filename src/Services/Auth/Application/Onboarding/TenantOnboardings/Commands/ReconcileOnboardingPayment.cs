using BuildingBlocks.Common;
using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Onboarding.Abstractions;
using TaxVision.Auth.Application.Onboarding.TenantOnboardings.Services;
using TaxVision.Auth.Domain.Onboarding.TenantOnboardings;

namespace TaxVision.Auth.Application.Onboarding.TenantOnboardings.Commands;

public sealed record ReconcileOnboardingPaymentCommand(Guid OnboardingId);

public sealed record ReconcileOnboardingPaymentResponse(
    Guid OnboardingId,
    Guid? PaymentId,
    string Status,
    string? RegistrationUrl,
    string? FailureCode,
    string? FailureMessage
);

public static class ReconcileOnboardingPaymentHandler
{
    public static async Task<Result<ReconcileOnboardingPaymentResponse>> Handle(
        ReconcileOnboardingPaymentCommand command,
        ITenantOnboardingRepository onboardings,
        IPaymentAppOnboardingClient paymentApp,
        OnboardingSuccessCompleter successCompleter,
        IPlanCatalogClient planCatalog,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var onboarding = await onboardings.GetByIdAsync(command.OnboardingId, ct);
        if (onboarding is null)
            return Result.Failure<ReconcileOnboardingPaymentResponse>(
                new Error("Onboarding.NotFound", "Onboarding not found.")
            );

        if (onboarding.Status == TenantOnboardingStatus.RegistrationPending)
        {
            var registrationUrl = await successCompleter.TryBuildRegistrationUrlAsync(onboarding, ct);
            return Result.Success(BuildResponse(onboarding, registrationUrl));
        }

        if (onboarding.Status == TenantOnboardingStatus.PaymentFailed)
            return Result.Success(BuildResponse(onboarding, registrationUrl: null));

        if (onboarding.Status == TenantOnboardingStatus.PaymentCompleted)
            return await CompleteRegistrationReadyAsync(
                onboarding,
                amountPaidCents: onboarding.NetAmountCents ?? onboarding.GrossAmountCents ?? 0,
                currency: onboarding.Currency ?? "USD",
                providerPaymentReference: null,
                paymentMethodMasked: null,
                paidAtUtc: onboarding.PaymentCompletedAtUtc ?? DateTime.UtcNow,
                successCompleter,
                planCatalog,
                unitOfWork,
                correlation,
                ct
            );

        if (onboarding.Status != TenantOnboardingStatus.PaymentProcessing)
            return Result.Failure<ReconcileOnboardingPaymentResponse>(
                new Error("Onboarding.PaymentReconcileInvalidState", "This onboarding cannot reconcile a payment now.")
            );

        if (onboarding.PaymentId is null)
            return Result.Failure<ReconcileOnboardingPaymentResponse>(
                new Error("Onboarding.PaymentMissing", "This onboarding has no pending payment.")
            );

        var reconcile = await paymentApp.ReconcileCheckoutAsync(
            new PaymentAppReconcileRequest(onboarding.PaymentId.Value),
            ct
        );
        if (reconcile.IsFailure)
            return Result.Failure<ReconcileOnboardingPaymentResponse>(reconcile.Error);

        return reconcile.Value.Status switch
        {
            OnboardingPaymentStatus.Succeeded => await CompleteSucceededPaymentAsync(
                onboarding,
                reconcile.Value,
                successCompleter,
                planCatalog,
                unitOfWork,
                correlation,
                ct
            ),
            OnboardingPaymentStatus.Failed
            or OnboardingPaymentStatus.Cancelled
            or OnboardingPaymentStatus.Refunded
            or OnboardingPaymentStatus.PartiallyRefunded
            or OnboardingPaymentStatus.ChargedBack => await MarkPaymentFailedAsync(
                onboarding,
                reconcile.Value,
                unitOfWork,
                ct
            ),
            _ => Result.Success(BuildResponse(onboarding, registrationUrl: null)),
        };
    }

    private static async Task<Result<ReconcileOnboardingPaymentResponse>> CompleteSucceededPaymentAsync(
        TenantOnboarding onboarding,
        PaymentAppReconcileResult payment,
        OnboardingSuccessCompleter successCompleter,
        IPlanCatalogClient planCatalog,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var completed = onboarding.MarkPaymentCompleted(
            payment.PaymentId.ToString("N"),
            payment.PaidAtUtc ?? DateTime.UtcNow
        );
        if (completed.IsFailure)
            return Result.Failure<ReconcileOnboardingPaymentResponse>(completed.Error);

        return await CompleteRegistrationReadyAsync(
            onboarding,
            payment.AmountPaidCents,
            payment.Currency,
            payment.ProviderPaymentReference,
            paymentMethodMasked: null,
            payment.PaidAtUtc ?? DateTime.UtcNow,
            successCompleter,
            planCatalog,
            unitOfWork,
            correlation,
            ct
        );
    }

    private static async Task<Result<ReconcileOnboardingPaymentResponse>> CompleteRegistrationReadyAsync(
        TenantOnboarding onboarding,
        long amountPaidCents,
        string currency,
        string? providerPaymentReference,
        string? paymentMethodMasked,
        DateTime paidAtUtc,
        OnboardingSuccessCompleter successCompleter,
        IPlanCatalogClient planCatalog,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var planName = await planCatalog.GetPlanNameAsync(onboarding.PlanId, ct);
        var correlationId = string.IsNullOrWhiteSpace(correlation.CorrelationId)
            ? onboarding.Id.ToString("N")
            : correlation.CorrelationId;

        var success = await successCompleter.CompleteAsync(
            onboarding,
            amountPaidCents,
            currency,
            onboarding.PaymentId,
            planName,
            paidAtUtc,
            providerPaymentReference,
            paymentMethodMasked,
            correlationId,
            ct
        );
        if (success.IsFailure)
            return Result.Failure<ReconcileOnboardingPaymentResponse>(success.Error);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(BuildResponse(onboarding, success.Value.RegistrationUrl));
    }

    private static async Task<Result<ReconcileOnboardingPaymentResponse>> MarkPaymentFailedAsync(
        TenantOnboarding onboarding,
        PaymentAppReconcileResult payment,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var reason =
            payment.FailureMessage ?? payment.FailureCode ?? "The payment provider did not complete the payment.";
        var failed = onboarding.MarkPaymentFailed(reason);
        if (failed.IsFailure)
            return Result.Failure<ReconcileOnboardingPaymentResponse>(failed.Error);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(BuildResponse(onboarding, registrationUrl: null, payment.FailureCode, reason));
    }

    private static ReconcileOnboardingPaymentResponse BuildResponse(
        TenantOnboarding onboarding,
        string? registrationUrl,
        string? failureCode = null,
        string? failureMessage = null
    ) =>
        new(
            onboarding.Id,
            onboarding.PaymentId,
            onboarding.Status.ToString(),
            registrationUrl,
            failureCode,
            failureMessage
        );
}
