using BuildingBlocks.Common;
using BuildingBlocks.Results;
using TaxVision.Campaigns.Application.Templates.Abstractions;
using TaxVision.Campaigns.Domain.Templates;

namespace TaxVision.Campaigns.Application.Templates.Queries;

// ─────────────────────────── List ───────────────────────────

public sealed record ListCampaignTemplatesQuery(Guid TenantId, int Page, int Size);

public static class ListCampaignTemplatesHandler
{
    public static async Task<PagedResult<CampaignTemplateResponse>> Handle(
        ListCampaignTemplatesQuery query,
        ICampaignTemplateRepository templates,
        CancellationToken ct
    )
    {
        var page = await templates.ListAsync(query.TenantId, query.Page, query.Size, ct);
        return new PagedResult<CampaignTemplateResponse>(
            page.Items.Select(CampaignTemplateResponse.From).ToList(),
            page.Page,
            page.Size,
            page.TotalCount
        );
    }
}

// ─────────────────────────── Get ───────────────────────────

public sealed record GetCampaignTemplateQuery(Guid TenantId, Guid TemplateId);

public static class GetCampaignTemplateHandler
{
    public static async Task<Result<CampaignTemplateResponse>> Handle(
        GetCampaignTemplateQuery query,
        ICampaignTemplateRepository templates,
        CancellationToken ct
    )
    {
        var template = await templates.GetByIdAsync(query.TenantId, query.TemplateId, ct);
        return template is null
            ? Result.Failure<CampaignTemplateResponse>(CampaignTemplateErrors.NotFound)
            : Result.Success(CampaignTemplateResponse.From(template));
    }
}
