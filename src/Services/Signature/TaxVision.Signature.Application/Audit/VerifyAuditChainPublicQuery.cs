using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Audit;

namespace TaxVision.Signature.Application.Audit;

public sealed record VerifyAuditChainPublicQuery(string Token);

public sealed record AuditChainEventView(
    long Sequence,
    SignatureAuditEventKind Kind,
    DateTime OccurredAtUtc,
    string PayloadJson,
    string ChainHash
);

public sealed record AuditChainVerificationResponse(
    Guid SignatureRequestId,
    bool IsIntact,
    long EventCount,
    long LastSequence,
    AuditChainDefect? Defect,
    IReadOnlyList<AuditChainEventView> Events
);
