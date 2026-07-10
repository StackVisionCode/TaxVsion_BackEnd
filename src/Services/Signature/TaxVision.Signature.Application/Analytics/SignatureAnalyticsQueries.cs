using TaxVision.Signature.Domain.Requests;

namespace TaxVision.Signature.Application.Analytics;

/// <summary>KPIs top-level para el dashboard staff en un rango de fechas UTC.</summary>
public sealed record SignatureAnalyticsSummary(
    Guid TenantId,
    DateOnly FromDay,
    DateOnly ToDay,
    int RequestsCreated,
    int RequestsSent,
    int RequestsCanceled,
    int RequestsExpired,
    int RequestsCompleted,
    int RequestsSealed,
    int SignersSigned,
    int SignersRejected,
    double CompletionRate,
    double RejectionRate
);

/// <summary>Serie de tiempo diaria (una fila por día del rango, ceros incluidos).</summary>
public sealed record SignatureAnalyticsTimelinePoint(
    DateOnly Day,
    int RequestsCreated,
    int RequestsSent,
    int RequestsCompleted,
    int SignersSigned,
    int SignersRejected
);

public sealed record SignatureAnalyticsTimeline(
    DateOnly FromDay,
    DateOnly ToDay,
    IReadOnlyList<SignatureAnalyticsTimelinePoint> Points
);

/// <summary>Distribución por <see cref="SignatureCategory"/> dentro del rango.</summary>
public sealed record SignatureAnalyticsByCategoryEntry(
    SignatureCategory Category,
    int RequestsCreated,
    int RequestsCompleted,
    int RequestsCanceled
);

public sealed record SignatureAnalyticsByCategory(
    DateOnly FromDay,
    DateOnly ToDay,
    IReadOnlyList<SignatureAnalyticsByCategoryEntry> Entries
);

public sealed record SignatureAnalyticsSummaryQuery(Guid TenantId, DateOnly FromDay, DateOnly ToDay);

public sealed record SignatureAnalyticsTimelineQuery(Guid TenantId, DateOnly FromDay, DateOnly ToDay);

public sealed record SignatureAnalyticsByCategoryQuery(Guid TenantId, DateOnly FromDay, DateOnly ToDay);
