namespace TaxVision.Tasks.ClientForms.Application.Reporting;

// Queries (entran desde el controller, ya resueltos tenant/actor/ABAC).

public sealed record FormReportAnswersQuery(
    Guid TenantId,
    Guid ActorUserId,
    bool CanViewAll,
    Guid TemplateId,
    string Key,
    string? Value,
    DateOnly? From,
    DateOnly? To,
    int Page
);

public sealed record FormReportSummaryQuery(
    Guid TenantId,
    Guid ActorUserId,
    bool CanViewAll,
    Guid TemplateId,
    string Key
);

public sealed record FormReportExportQuery(
    Guid TenantId,
    Guid ActorUserId,
    bool CanViewAll,
    Guid TemplateId,
    DateOnly? From,
    DateOnly? To
);

public sealed record FormReportKeysQuery(Guid TenantId, Guid TemplateId);

// Responses.

public sealed record FormReportAnswerRow(
    Guid FormRequestId,
    Guid? CustomerId,
    Guid? ParticipantId,
    string DisplayName,
    string Title,
    int RevisionNumber,
    DateTime CompletedAtUtc,
    string Key,
    int? RowIndex,
    string ValueText,
    decimal? ValueNumber,
    DateOnly? ValueDate
);

public sealed record FormReportSummaryBucket(string Value, int Count);

public sealed record FormReportSummaryResponse(
    Guid TemplateId,
    string Key,
    int Total,
    IReadOnlyList<FormReportSummaryBucket> Buckets
);

public sealed record FormReportKeysResponse(Guid TemplateId, IReadOnlyList<FormReportableKey> Keys);

// Fila cruda del export: una por valor indexado, con los metadatos de su request. El handler la pivotea.
public sealed record FormReportExportEntry(
    Guid FormRequestId,
    string DisplayName,
    string Title,
    int RevisionNumber,
    DateTime CompletedAtUtc,
    string Key,
    int? RowIndex,
    string ValueText
);
