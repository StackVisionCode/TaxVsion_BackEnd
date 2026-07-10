using TaxVision.Signature.Application.Abstractions;

namespace TaxVision.Signature.Application.Analytics;

public static class GetSignatureAnalyticsSummaryHandler
{
    public static Task<SignatureAnalyticsSummary> Handle(
        SignatureAnalyticsSummaryQuery query,
        ISignatureAnalyticsReadService readService,
        CancellationToken ct
    ) => readService.GetSummaryAsync(query, ct);
}

public static class GetSignatureAnalyticsTimelineHandler
{
    public static Task<SignatureAnalyticsTimeline> Handle(
        SignatureAnalyticsTimelineQuery query,
        ISignatureAnalyticsReadService readService,
        CancellationToken ct
    ) => readService.GetTimelineAsync(query, ct);
}

public static class GetSignatureAnalyticsByCategoryHandler
{
    public static Task<SignatureAnalyticsByCategory> Handle(
        SignatureAnalyticsByCategoryQuery query,
        ISignatureAnalyticsReadService readService,
        CancellationToken ct
    ) => readService.GetByCategoryAsync(query, ct);
}
