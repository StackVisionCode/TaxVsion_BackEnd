using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.ClientForms.Application.Insights;

// Agregados crudos que calcula la base (consultas agrupadas); el armado y las medianas van en memoria.
public interface IFormInsightsReadRepository
{
    Task<FormInsightsRawSummary> GetSummaryAsync(
        FormInsightsScope scope,
        FormInsightsWindow window,
        CancellationToken ct = default
    );

    Task<FormInsightsRawTemplatePage> ListTemplatesAsync(
        FormInsightsScope scope,
        FormInsightsWindow window,
        int page,
        int size,
        CancellationToken ct = default
    );
}

public sealed record FormInsightsScope(Guid TenantId, Guid ActorUserId, bool CanViewAll, Guid? TemplateId);

public sealed record FormInsightsCohortCounts(
    int Sent,
    int Started,
    int Submitted,
    int Completed,
    int Cancelled,
    int CompletedReminders
)
{
    public static readonly FormInsightsCohortCounts Zero = new(0, 0, 0, 0, 0, 0);

    public FormInsightsCohortCounts Plus(FormInsightsCohortCounts other) =>
        new(
            Sent + other.Sent,
            Started + other.Started,
            Submitted + other.Submitted,
            Completed + other.Completed,
            Cancelled + other.Cancelled,
            CompletedReminders + other.CompletedReminders
        );
}

public sealed record FormInsightsCohortTally(
    FormParticipantKind Kind,
    FormInsightsCohortCounts Current,
    FormInsightsCohortCounts Previous
);

// Histograma de minutos hasta el primer envío: el valor del bucket y cuántas requests caen en él.
public sealed record FormInsightsDurationBucket(int Minutes, int Current, int Previous);

public sealed record FormInsightsTemplateDurationBucket(Guid TemplateId, int Minutes, int Count);

public sealed record FormInsightsDayCount(DateTime Day, int Count);

public sealed record FormInsightsRunTally(string ActionType, FormActionRunStatus Status, int Count);

public sealed record FormInsightsFailureTally(string ActionType, string? Code, int Count);

public sealed record FormInsightsPdfTally(FormRevisionPdfStatus Status, int Count);

public sealed record FormInsightsSignatureTally(FormRevisionSignatureStatus Status, int Count);

public sealed record FormInsightsRawSummary(
    IReadOnlyList<FormInsightsCohortTally> Cohort,
    IReadOnlyList<FormInsightsDurationBucket> Durations,
    int OverdueOpen,
    IReadOnlyList<FormInsightsDayCount> SentByDay,
    IReadOnlyList<FormInsightsDayCount> CompletedByDay,
    IReadOnlyList<FormInsightsRunTally> Runs,
    IReadOnlyList<FormInsightsFailureTally> Failures,
    IReadOnlyList<FormInsightsPdfTally> Pdfs,
    IReadOnlyList<FormInsightsSignatureTally> Signatures,
    IReadOnlyList<FormInsightsTemplateOptionResponse> Templates,
    bool HasAnyRequests
);

public sealed record FormInsightsTemplateTally(Guid TemplateId, int Sent, int Completed);

public sealed record FormInsightsRawTemplatePage(
    IReadOnlyList<FormInsightsTemplateTally> Rows,
    int Total,
    IReadOnlyDictionary<Guid, string> Names,
    IReadOnlyList<FormInsightsTemplateDurationBucket> Durations,
    IReadOnlyDictionary<Guid, int> Returns
);
