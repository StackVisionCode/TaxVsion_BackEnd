using BuildingBlocks.Common;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Onboarding.Abstractions;
using TaxVision.Auth.Domain.Onboarding.TenantOnboardings;

namespace TaxVision.Auth.Application.Onboarding.Admin.Queries;

public sealed record GetOnboardingsAdminQuery(TenantOnboardingStatus? Status, int Page, int PageSize);

public sealed record OnboardingAdminSummaryResponse(
    Guid Id,
    string Email,
    string Status,
    Guid PlanId,
    TenantProvisioningStep CurrentStep,
    TenantProvisioningStep? FailedStep,
    string? FailureCode,
    int RetryAttempt,
    DateTime? NextRetryAtUtc,
    DateTime CreatedAtUtc,
    DateTime? ProvisioningStartedAtUtc
);

/// <summary>PayFlow (Fase 17) — receptor de <c>GET /auth/onboarding/admin</c>.</summary>
public static class GetOnboardingsAdminHandler
{
    public static async Task<Result<PagedResult<OnboardingAdminSummaryResponse>>> Handle(
        GetOnboardingsAdminQuery query,
        ITenantOnboardingRepository onboardings,
        CancellationToken ct
    )
    {
        var page = query.Page <= 0 ? 1 : query.Page;
        var pageSize = query.PageSize <= 0 ? 50 : Math.Min(query.PageSize, 200);

        var (items, totalCount) = await onboardings.GetPagedAdminAsync(query.Status, page, pageSize, ct);

        var mapped = items
            .Select(onboarding => new OnboardingAdminSummaryResponse(
                onboarding.Id,
                onboarding.Email,
                onboarding.Status.ToString(),
                onboarding.PlanId,
                onboarding.CurrentStep,
                onboarding.FailedStep,
                onboarding.FailureCode,
                onboarding.RetryAttempt,
                onboarding.NextRetryAtUtc,
                onboarding.CreatedAtUtc,
                onboarding.ProvisioningStartedAtUtc
            ))
            .ToList();

        return Result.Success(new PagedResult<OnboardingAdminSummaryResponse>(mapped, page, pageSize, totalCount));
    }
}
