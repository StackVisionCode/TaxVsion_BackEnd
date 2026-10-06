using TaxVision.Signature.Domain.Requests;

namespace TaxVision.Signature.Application.Requests.Queries.List;

public sealed record SignatureRequestSummary(
    Guid Id,
    string Title,
    string Category,
    SignatureRequestStatus Status,
    Guid OriginalFileId,
    int SignerCount,
    DateTime? ExpiresAtUtc,
    DateTime CreatedAtUtc,
    DateTime? SentAtUtc,
    DateTime? CompletedAtUtc,
    // F2.5: distingue el borrador propio del actor para pintarlo como "In preparation".
    bool IsOwnedByActor,
    // F3: hora UTC programada (si Status == Scheduled); null en otro caso.
    DateTime? ScheduledSendAtUtc
);

public sealed record ListSignatureRequestsResult(
    IReadOnlyList<SignatureRequestSummary> Items,
    int TotalCount,
    int Page,
    int PageSize
);
