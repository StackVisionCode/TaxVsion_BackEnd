using BuildingBlocks.Common;
using TaxVision.Campaigns.Application.Scheduling.Abstractions;

namespace TaxVision.Campaigns.Application.Scheduling.Queries;

public sealed record ListCampaignSchedulesQuery(Guid TenantId, Guid CampaignId, int Page, int Size);

public static class ListCampaignSchedulesHandler
{
    public static async Task<PagedResult<CampaignScheduleResponse>> Handle(
        ListCampaignSchedulesQuery query,
        ICampaignScheduleRepository schedules,
        CancellationToken ct
    )
    {
        var page = await schedules.ListByCampaignAsync(query.TenantId, query.CampaignId, query.Page, query.Size, ct);
        return new PagedResult<CampaignScheduleResponse>(
            page.Items.Select(CampaignScheduleResponse.From).ToList(),
            page.Page,
            page.Size,
            page.TotalCount
        );
    }
}
