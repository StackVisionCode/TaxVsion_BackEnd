using TaxVision.Postmaster.Domain.Suppression;

namespace TaxVision.Postmaster.Application.Suppression.Queries.ListSuppressionEntries;

public sealed record ListSuppressionEntriesQuery(
    Guid TenantId,
    string? Address,
    SuppressionReason? Reason,
    int Page,
    int PageSize
);

public sealed record SuppressionListEntryDto(
    string EmailAddress,
    string Reason,
    DateTime AddedAtUtc,
    Guid? AddedByUserId,
    string? Notes
);
