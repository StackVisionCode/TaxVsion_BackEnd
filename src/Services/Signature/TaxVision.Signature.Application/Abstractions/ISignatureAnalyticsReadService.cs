using TaxVision.Signature.Application.Analytics;

namespace TaxVision.Signature.Application.Abstractions;

public interface ISignatureAnalyticsReadService
{
    Task<SignatureAnalyticsSummary> GetSummaryAsync(
        SignatureAnalyticsSummaryQuery query,
        CancellationToken ct = default
    );

    Task<SignatureAnalyticsTimeline> GetTimelineAsync(
        SignatureAnalyticsTimelineQuery query,
        CancellationToken ct = default
    );

    Task<SignatureAnalyticsByCategory> GetByCategoryAsync(
        SignatureAnalyticsByCategoryQuery query,
        CancellationToken ct = default
    );
}
