using BuildingBlocks.Common;
using BuildingBlocks.Results;
using TaxVision.Campaigns.Application.Campaigns.Abstractions;
using TaxVision.Campaigns.Domain.Campaigns;

namespace TaxVision.Campaigns.Application.Campaigns.Queries;

public sealed record GetCampaignQuery(Guid TenantId, Guid Id);

public static class GetCampaignHandler
{
    public static async Task<Result<CampaignResponse>> Handle(
        GetCampaignQuery query,
        ICampaignRepository campaigns,
        CancellationToken ct
    )
    {
        var campaign = await campaigns.GetByIdAsync(query.TenantId, query.Id, ct);
        return campaign is null
            ? Result.Failure<CampaignResponse>(CampaignErrors.NotFound)
            : Result.Success(CampaignResponse.From(campaign));
    }
}

public sealed record ListCampaignsQuery(Guid TenantId, CampaignStatus? Status, int Page, int Size);

public static class ListCampaignsHandler
{
    public static async Task<PagedResult<CampaignResponse>> Handle(
        ListCampaignsQuery query,
        ICampaignRepository campaigns,
        CancellationToken ct
    )
    {
        var page = await campaigns.ListAsync(query.TenantId, query.Status, query.Page, query.Size, ct);
        return new PagedResult<CampaignResponse>(
            page.Items.Select(CampaignResponse.From).ToList(),
            page.Page,
            page.Size,
            page.TotalCount
        );
    }
}
