using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Templates;

namespace TaxVision.Signature.Application.Templates.Queries.List;

public sealed record ListTemplatesQuery(
    Guid TenantId,
    SignatureTemplateStatus? Status,
    SignatureCategory? Category,
    int Page,
    int PageSize
);

public sealed record TemplateSummary(
    Guid Id,
    string Title,
    SignatureCategory Category,
    SignatureTemplateStatus Status,
    int SlotCount,
    int FieldCount,
    DateTime CreatedAtUtc,
    DateTime? PublishedAtUtc
);

public sealed record ListTemplatesResult(IReadOnlyList<TemplateSummary> Items, int TotalCount, int Page, int PageSize);
