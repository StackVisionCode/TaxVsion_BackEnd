using BuildingBlocks.Common;

namespace TaxVision.Tasks.ClientForms.Application.Insights;

public static class FormInsightsTemplatesHandler
{
    public const int DefaultSize = 10;
    public const int MaxSize = 50;

    public static async Task<PagedResult<FormInsightsTemplateRowResponse>> Handle(
        FormInsightsTemplatesQuery query,
        IFormInsightsReadRepository reads,
        CancellationToken ct
    )
    {
        var window = FormInsightsWindow.Create(query.PeriodDays, query.UtcOffsetMinutes, DateTime.UtcNow);
        var scope = new FormInsightsScope(query.TenantId, query.ActorUserId, query.CanViewAll, null);
        var page = Math.Max(1, query.Page);
        var size = query.Size < 1 ? DefaultSize : Math.Min(query.Size, MaxSize);
        var raw = await reads.ListTemplatesAsync(scope, window, page, size, ct);
        return new PagedResult<FormInsightsTemplateRowResponse>(
            FormInsightsAssembler.Templates(raw),
            page,
            size,
            raw.Total
        );
    }
}
