using BuildingBlocks.Results;
using TaxVision.Auth.Application.Onboarding.Abstractions;
using TaxVision.Auth.Domain.Onboarding.TenantOnboardings;

namespace TaxVision.Auth.Application.Onboarding.Admin.Queries;

public sealed record GetOnboardingAdminDetailQuery(Guid OnboardingId);

public sealed record OnboardingAdminDetailResponse(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    string Status,
    Guid PlanId,
    string? OfficeName,
    string? RequestedSubdomain,
    TenantProvisioningStep CurrentStep,
    TenantProvisioningStep? FailedStep,
    string? FailureCode,
    string? FailureReason,
    int RetryAttempt,
    DateTime? NextRetryAtUtc,
    Guid? TenantId,
    Guid? UserId,
    Guid? SubscriptionId,
    Guid? PaymentId,
    string? PaymentReference,
    DateTime CreatedAtUtc,
    DateTime? ProvisioningStartedAtUtc,
    DateTime? RegistrationCompletedAtUtc
);

/// <summary>PayFlow (Fase 17) — receptor de <c>GET /auth/onboarding/admin/{id}</c>.</summary>
public static class GetOnboardingAdminDetailHandler
{
    public static async Task<Result<OnboardingAdminDetailResponse>> Handle(
        GetOnboardingAdminDetailQuery query,
        ITenantOnboardingRepository onboardings,
        CancellationToken ct
    )
    {
        var onboarding = await onboardings.GetByIdAsync(query.OnboardingId, ct);
        if (onboarding is null)
            return Result.Failure<OnboardingAdminDetailResponse>(
                new Error("Onboarding.NotFound", "Onboarding not found.")
            );

        return Result.Success(
            new OnboardingAdminDetailResponse(
                onboarding.Id,
                onboarding.Email,
                onboarding.FirstName,
                onboarding.LastName,
                onboarding.Status.ToString(),
                onboarding.PlanId,
                onboarding.OfficeName,
                onboarding.RequestedSubdomain,
                onboarding.CurrentStep,
                onboarding.FailedStep,
                onboarding.FailureCode,
                onboarding.FailureReason,
                onboarding.RetryAttempt,
                onboarding.NextRetryAtUtc,
                onboarding.TenantId,
                onboarding.UserId,
                onboarding.SubscriptionId,
                onboarding.PaymentId,
                onboarding.PaymentReference,
                onboarding.CreatedAtUtc,
                onboarding.ProvisioningStartedAtUtc,
                onboarding.RegistrationCompletedAtUtc
            )
        );
    }
}
