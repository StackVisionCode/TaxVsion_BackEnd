using BuildingBlocks.Common;
using BuildingBlocks.Results;
using TaxVision.Referrals.Application.Abstractions;
using TaxVision.Referrals.Domain.Codes;
using TaxVision.Referrals.Domain.Rewards;

namespace TaxVision.Referrals.Application.Admin;

public sealed record ListReferralCodesAdminQuery(Guid? OwnerTenantId, string? Status, int Page = 1, int Size = 20);

public sealed record ListReferralRewardsAdminQuery(string? Status, int Page = 1, int Size = 20);

public static class ReferralAdminQueryHandler
{
    public static async Task<Result<PagedResult<ReferralRewardAdminRow>>> Handle(
        ListReferralRewardsAdminQuery query,
        IReferralAdminReadModel readModel,
        CancellationToken ct
    )
    {
        if (query.Page < 1 || query.Size is < 1 or > 100)
        {
            return Result.Failure<PagedResult<ReferralRewardAdminRow>>(
                new Error("Referrals.InvalidPaging", "page must be at least 1 and size between 1 and 100.")
            );
        }

        ReferralRewardCaseStatus? status = null;
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            if (!Enum.TryParse<ReferralRewardCaseStatus>(query.Status.Trim(), ignoreCase: true, out var parsed))
            {
                return Result.Failure<PagedResult<ReferralRewardAdminRow>>(
                    new Error("Referrals.InvalidStatus", $"Unknown status '{query.Status}'.")
                );
            }

            status = parsed;
        }

        return Result.Success(await readModel.ListRewardsAsync(status, query.Page, query.Size, ct));
    }

    public static async Task<Result<PagedResult<ReferralCodeAdminRow>>> Handle(
        ListReferralCodesAdminQuery query,
        IReferralAdminReadModel readModel,
        CancellationToken ct
    )
    {
        if (query.Page < 1 || query.Size is < 1 or > 100)
        {
            return Result.Failure<PagedResult<ReferralCodeAdminRow>>(
                new Error("Referrals.InvalidPaging", "page must be at least 1 and size between 1 and 100.")
            );
        }

        ReferralCodeStatus? status = null;
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            if (!Enum.TryParse<ReferralCodeStatus>(query.Status.Trim(), ignoreCase: true, out var parsed))
            {
                return Result.Failure<PagedResult<ReferralCodeAdminRow>>(
                    new Error("Referrals.InvalidStatus", $"Unknown status '{query.Status}'.")
                );
            }

            status = parsed;
        }

        return Result.Success(await readModel.ListCodesAsync(query.OwnerTenantId, status, query.Page, query.Size, ct));
    }
}
