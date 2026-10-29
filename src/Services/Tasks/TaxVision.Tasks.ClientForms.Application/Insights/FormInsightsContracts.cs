using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.ClientForms.Application.Insights;

// Métricas de la oficina sobre las requests enviadas (Origin = Directed), con el mismo ABAC que la bandeja.
public sealed record FormInsightsSummaryQuery(
    Guid TenantId,
    Guid ActorUserId,
    bool CanViewAll,
    int PeriodDays,
    Guid? TemplateId,
    int UtcOffsetMinutes
);

public sealed record FormInsightsTemplatesQuery(
    Guid TenantId,
    Guid ActorUserId,
    bool CanViewAll,
    int PeriodDays,
    int UtcOffsetMinutes,
    int Page,
    int Size
);

public enum FormInsightsGranularity
{
    Day = 1,
    Week = 2,
}

public sealed record FormInsightsSummaryResponse(
    FormInsightsPeriodResponse Period,
    FormInsightsKpisResponse Current,
    FormInsightsKpisResponse Previous,
    int OverdueOpen,
    IReadOnlyList<FormInsightsFunnelStepResponse> Funnel,
    IReadOnlyList<FormInsightsTrendPointResponse> Trend,
    IReadOnlyList<FormInsightsParticipantSplitResponse> ParticipantSplit,
    IReadOnlyList<FormInsightsAutomationResponse> Automation,
    IReadOnlyList<FormInsightsFailureReasonResponse> TopFailureReasons,
    FormInsightsSignaturesResponse Signatures,
    IReadOnlyList<FormInsightsTemplateOptionResponse> Templates,
    bool HasAnyRequests
);

public sealed record FormInsightsPeriodResponse(
    int Days,
    DateTime FromUtc,
    DateTime ToUtc,
    DateTime PreviousFromUtc,
    FormInsightsGranularity Granularity
);

public sealed record FormInsightsKpisResponse(
    int Sent,
    int Started,
    int Submitted,
    int Completed,
    int Cancelled,
    double? CompletionRate,
    int? MedianMinutesToFirstSubmission,
    double? AverageRemindersPerCompleted
);

public sealed record FormInsightsFunnelStepResponse(string Key, int Count, double? ConversionFromPrevious);

public sealed record FormInsightsTrendPointResponse(DateOnly Start, int Sent, int Completed);

public sealed record FormInsightsParticipantSplitResponse(FormParticipantKind Kind, int Sent, int Completed);

public sealed record FormInsightsAutomationResponse(
    string Kind,
    int Succeeded,
    int Failed,
    int Rejected,
    int Skipped,
    int Pending
);

public sealed record FormInsightsFailureReasonResponse(string Kind, string Code, int Count);

public sealed record FormInsightsSignaturesResponse(
    int Requested,
    int Signed,
    int Pending,
    int Declined,
    int Expired,
    int Canceled,
    int Failed
);

public sealed record FormInsightsTemplateOptionResponse(Guid TemplateId, string Name);

public sealed record FormInsightsTemplateRowResponse(
    Guid TemplateId,
    string Name,
    int Sent,
    int Completed,
    double? CompletionRate,
    int? MedianMinutesToFirstSubmission,
    int Returns
);

public static class FormInsightsAutomationKinds
{
    public const string CreateCustomer = "CreateCustomer";
    public const string UpdateCustomerProfile = "UpdateCustomerProfile";
    public const string SummaryPdf = "SummaryPdf";
    public const string Signature = "Signature";
}

public static class FormInsightsFunnelKeys
{
    public const string Sent = "sent";
    public const string Started = "started";
    public const string Submitted = "submitted";
    public const string Completed = "completed";
}
