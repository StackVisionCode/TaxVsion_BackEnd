using Microsoft.EntityFrameworkCore;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Requests;

namespace TaxVision.Signature.Infrastructure.Persistence.Queries;

/// <summary>Originales y clientes firmantes de todas las solicitudes del tenant, sin cargar el aggregate.</summary>
internal sealed class SignatureOriginalFileQuery(SignatureDbContext db) : ISignatureOriginalFileQuery
{
    public async Task<IReadOnlyList<RequestOriginalFiles>> ListAsync(Guid tenantId, CancellationToken ct = default)
    {
        var rows = await db
            .SignatureRequests.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(request => request.TenantId == tenantId && request.Documents.Any())
            .Select(request => new
            {
                request.Id,
                Files = request.Documents.Select(document => document.OriginalFileId).ToList(),
                Customers = request.Signers.Select(signer => signer.MappedCustomerId).ToList(),
            })
            .ToListAsync(ct);
        return [.. rows.Select(row => new RequestOriginalFiles(row.Id, row.Files, row.Customers))];
    }

    public async Task<IReadOnlyList<SignatureRequest>> ListOwningFilesAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> fileIds,
        CancellationToken ct = default
    ) =>
        await db
            .SignatureRequests.IgnoreQueryFilters()
            .AsNoTracking()
            .Include(request => request.Signers)
            .Include(request => request.Documents)
            .Include(request => request.CustomerCopies)
            .AsSplitQuery()
            .Where(request =>
                request.TenantId == tenantId
                && (
                    (request.CertificateFileId != null && fileIds.Contains(request.CertificateFileId.Value))
                    || request.Documents.Any(document =>
                        fileIds.Contains(document.OriginalFileId)
                        || (document.SealedFileId != null && fileIds.Contains(document.SealedFileId.Value))
                        || (document.CertificateFileId != null && fileIds.Contains(document.CertificateFileId.Value))
                    )
                    || request.CustomerCopies.Any(copy => fileIds.Contains(copy.CopyFileId))
                )
            )
            .ToListAsync(ct);
}
