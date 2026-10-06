using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;

namespace TaxVision.Signature.Application.Audit;

/// <summary>F7 — lee el audit chain completo de una request. Autorización en el controller.</summary>
public sealed record GetAuditTrailQuery(Guid TenantId, Guid SignatureRequestId);

public static class GetAuditTrailHandler
{
    public static async Task<Result<AuditTrailResponse>> Handle(
        GetAuditTrailQuery query,
        ISignatureAuditRepository repository,
        CancellationToken ct
    )
    {
        var events = await repository.ListAsync(query.TenantId, query.SignatureRequestId, ct);
        return Result.Success(AuditTrailResponse.From(events));
    }
}
