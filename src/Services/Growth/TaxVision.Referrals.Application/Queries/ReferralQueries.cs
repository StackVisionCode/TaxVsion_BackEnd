using BuildingBlocks.Common;
using BuildingBlocks.Results;
using TaxVision.Referrals.Application.Abstractions;

namespace TaxVision.Referrals.Application.Queries;

public sealed record ListReferralInvitesQuery(Guid TenantId, int Page = 1, int Size = 20);

public sealed record ListReferralSharesQuery(Guid TenantId, int Page = 1, int Size = 20);

public sealed record GetReferralRewardSummaryQuery(Guid TenantId);

/// <summary>Lecturas de "Mis referidos" de una oficina. Validan la paginación (1 ≤ size ≤ 100) y el tenant.</summary>
public static class ReferralQueryHandler
{
    public static async Task<Result<PagedResult<ReferralInviteRow>>> Handle(
        ListReferralInvitesQuery query,
        IReferralReadModel readModel,
        CancellationToken ct
    )
    {
        var invalid = Validate(query.TenantId, query.Page, query.Size);
        return invalid is not null
            ? Result.Failure<PagedResult<ReferralInviteRow>>(invalid)
            : Result.Success(await readModel.ListInvitesAsync(query.TenantId, query.Page, query.Size, ct));
    }

    public static async Task<Result<PagedResult<ReferralShareRow>>> Handle(
        ListReferralSharesQuery query,
        IReferralReadModel readModel,
        CancellationToken ct
    )
    {
        var invalid = Validate(query.TenantId, query.Page, query.Size);
        return invalid is not null
            ? Result.Failure<PagedResult<ReferralShareRow>>(invalid)
            : Result.Success(await readModel.ListSharesAsync(query.TenantId, query.Page, query.Size, ct));
    }

    public static async Task<Result<ReferralRewardSummary>> Handle(
        GetReferralRewardSummaryQuery query,
        IReferralReadModel readModel,
        CancellationToken ct
    ) =>
        query.TenantId == Guid.Empty
            ? Result.Failure<ReferralRewardSummary>(new Error("Referrals.InvalidScope", "TenantId is required."))
            : Result.Success(await readModel.GetSummaryAsync(query.TenantId, ct));

    private static Error? Validate(Guid tenantId, int page, int size)
    {
        if (tenantId == Guid.Empty)
            return new Error("Referrals.InvalidScope", "TenantId is required.");

        return page < 1 || size is < 1 or > 100
            ? new Error("Referrals.InvalidPaging", "page must be at least 1 and size between 1 and 100.")
            : null;
    }
}
