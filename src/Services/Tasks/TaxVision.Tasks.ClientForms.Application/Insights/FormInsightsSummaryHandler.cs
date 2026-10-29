namespace TaxVision.Tasks.ClientForms.Application.Insights;

public static class FormInsightsSummaryHandler
{
    public static async Task<FormInsightsSummaryResponse> Handle(
        FormInsightsSummaryQuery query,
        IFormInsightsReadRepository reads,
        CancellationToken ct
    )
    {
        var window = FormInsightsWindow.Create(query.PeriodDays, query.UtcOffsetMinutes, DateTime.UtcNow);
        var scope = new FormInsightsScope(query.TenantId, query.ActorUserId, query.CanViewAll, query.TemplateId);
        var raw = await reads.GetSummaryAsync(scope, window, ct);
        return FormInsightsAssembler.Summary(raw, window);
    }
}
