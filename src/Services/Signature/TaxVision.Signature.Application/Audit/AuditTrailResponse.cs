using TaxVision.Signature.Domain.Audit;

namespace TaxVision.Signature.Application.Audit;

/// <summary>F7 — vista del audit chain para el preparador (GET /signature/requests/{id}/audit).</summary>
public sealed record AuditTrailResponse(IReadOnlyList<AuditTrailEntry> Entries)
{
    public static AuditTrailResponse From(IReadOnlyList<SignatureAuditEvent> events) =>
        new([
            .. events.Select(e => new AuditTrailEntry(
                e.Sequence,
                e.Kind.ToString(),
                e.OccurredAtUtc,
                e.PayloadJson,
                e.ChainHash
            )),
        ]);
}

public sealed record AuditTrailEntry(
    long Sequence,
    string Kind,
    DateTime OccurredAtUtc,
    // Payload JSON tal cual lo persistió el chain: el verificador cripto lo usa para rehashear;
    // la UI del preparador lo parsea y pinta campos clave (signerId, fileId, reason, etc.).
    string PayloadJson,
    string ChainHash
);
