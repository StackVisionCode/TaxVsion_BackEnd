using Microsoft.EntityFrameworkCore;
using TaxVision.Signature.Application.Abstractions;

namespace TaxVision.Signature.Infrastructure.Persistence.Queries;

/// <summary>
/// Una oficina tiene pocas plantillas: se leen todos sus archivos base y se cruzan en memoria (sin listas IN largas).
/// Cuentan también las archivadas: su archivo sigue reutilizado por las solicitudes que ya crearon.
/// </summary>
internal sealed class SignatureTemplateFileQuery(SignatureDbContext db) : ISignatureTemplateFileQuery
{
    public async Task<IReadOnlySet<Guid>> TemplateBaseFilesAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> fileIds,
        CancellationToken ct = default
    )
    {
        if (fileIds.Count == 0)
            return new HashSet<Guid>();

        var templates = db.SignatureTemplates.IgnoreQueryFilters().AsNoTracking().Where(t => t.TenantId == tenantId);
        var documentFiles = await db
            .TemplateDocuments.IgnoreQueryFilters()
            .AsNoTracking()
            .Join(
                templates,
                document => document.SignatureTemplateId,
                template => template.Id,
                (document, _) => document.FileId
            )
            .ToListAsync(ct);
        var baseFiles = await templates
            .Where(template => template.BaseDocumentFileId != null)
            .Select(template => template.BaseDocumentFileId!.Value)
            .ToListAsync(ct);
        return documentFiles.Concat(baseFiles).Where(fileIds.Contains).ToHashSet();
    }
}
