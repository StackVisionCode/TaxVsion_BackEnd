using BuildingBlocks.Common;
using BuildingBlocks.Results;
using TaxVision.Campaigns.Application.Runs.Abstractions;
using TaxVision.Campaigns.Domain.Runs;

namespace TaxVision.Campaigns.Application.Runs.Queries;

public sealed record GetCampaignRunQuery(Guid TenantId, Guid RunId);

public static class GetCampaignRunHandler
{
    public static async Task<Result<CampaignRunResponse>> Handle(
        GetCampaignRunQuery query,
        ICampaignRunRepository runs,
        CancellationToken ct
    )
    {
        var run = await runs.GetByIdAsync(query.TenantId, query.RunId, ct);
        return run is null
            ? Result.Failure<CampaignRunResponse>(CampaignRunErrors.NotFound)
            : Result.Success(CampaignRunResponse.From(run));
    }
}

public sealed record ListCampaignRunsQuery(Guid TenantId, Guid CampaignId, int Page, int Size);

public static class ListCampaignRunsHandler
{
    public static async Task<PagedResult<CampaignRunResponse>> Handle(
        ListCampaignRunsQuery query,
        ICampaignRunRepository runs,
        CancellationToken ct
    )
    {
        var page = await runs.ListByCampaignAsync(query.TenantId, query.CampaignId, query.Page, query.Size, ct);
        return new PagedResult<CampaignRunResponse>(
            page.Items.Select(CampaignRunResponse.From).ToList(),
            page.Page,
            page.Size,
            page.TotalCount
        );
    }
}

public sealed record ListRecentCampaignRunsQuery(Guid TenantId, int Take, DateTime NowUtc);

public static class ListRecentCampaignRunsHandler
{
    private const int DefaultTake = 5;
    private const int MaxTake = 20;

    public static Task<RecentCampaignRunsResponse> Handle(
        ListRecentCampaignRunsQuery query,
        ICampaignRunRepository runs,
        CancellationToken ct
    ) => runs.ListRecentAsync(query.TenantId, ClampTake(query.Take), query.NowUtc, ct);

    // Sin valor (o inválido) = 5; más de 20 = 20.
    private static int ClampTake(int take) => take < 1 ? DefaultTake : Math.Min(take, MaxTake);
}
