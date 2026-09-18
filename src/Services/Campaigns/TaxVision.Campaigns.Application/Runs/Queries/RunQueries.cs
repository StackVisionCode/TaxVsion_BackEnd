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
