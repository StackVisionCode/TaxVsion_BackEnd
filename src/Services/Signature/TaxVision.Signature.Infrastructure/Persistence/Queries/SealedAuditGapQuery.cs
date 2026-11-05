using Microsoft.EntityFrameworkCore;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Audit;

namespace TaxVision.Signature.Infrastructure.Persistence.Queries;

public sealed class SealedAuditGapQuery(SignatureDbContext db) : ISealedAuditGapQuery
{
    public async Task<IReadOnlyList<SealedAuditGap>> ListAsync(Guid? tenantId, CancellationToken ct = default)
    {
        // Cross-tenant a propósito (comando de plataforma): el filtro va explícito.
        var rows =
            from document in db.RequestDocuments.IgnoreQueryFilters()
            join request in db.SignatureRequests.IgnoreQueryFilters() on document.SignatureRequestId equals request.Id
            where
                document.SealedFileId != null
                && (tenantId == null || request.TenantId == tenantId)
                && !db
                    .SignatureAuditEvents.IgnoreQueryFilters()
                    .Any(line =>
                        line.SignatureRequestId == document.SignatureRequestId
                        && line.Kind == SignatureAuditEventKind.RequestSealed
                        && (
                            line.PayloadJson.Contains(document.Id.ToString())
                            || line.PayloadJson.Contains(document.SealedFileId.Value.ToString())
                        )
                    )
            select new SealedAuditGap(
                request.TenantId,
                document.SignatureRequestId,
                document.Id,
                document.SealedFileId!.Value,
                document.DocumentHashPost == null ? null : document.DocumentHashPost.Value,
                document.CertificateFileId,
                document.SealedAtUtc ?? request.CompletedAtUtc ?? request.UpdatedAtUtc
            );

        return await rows.AsNoTracking().ToListAsync(ct);
    }
}
