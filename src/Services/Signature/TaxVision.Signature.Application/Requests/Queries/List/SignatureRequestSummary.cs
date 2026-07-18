using TaxVision.Signature.Domain.Requests;

namespace TaxVision.Signature.Application.Requests.Queries.List;

public sealed record SignatureRequestSummary(
    Guid Id,
    string Title,
    SignatureCategory Category,
    SignatureRequestStatus Status,
    Guid OriginalFileId,
    int SignerCount,
    DateTime ExpiresAtUtc,
    DateTime CreatedAtUtc,
    DateTime? SentAtUtc,
    DateTime? CompletedAtUtc
);

public sealed record ListSignatureRequestsResult(
    IReadOnlyList<SignatureRequestSummary> Items,
    int TotalCount,
    int Page,
    int PageSize
);
