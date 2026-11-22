using Microsoft.EntityFrameworkCore;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Domain.Files;

namespace TaxVision.CloudStorage.Infrastructure.Persistence.Repositories;

/// <summary>
/// Resumen del widget Documents con la rama Documents de la biblioteca (<see cref="LibraryReadRepository"/>): sin
/// origen declarado, dueño cliente u oficina y carpeta de documentos. Así quedan fuera notas, chat, correo y PDF de
/// factura o firma. A diferencia de la biblioteca, también cuenta los estados en curso.
/// </summary>
public sealed class FileSummaryReader(CloudStorageDbContext db) : IFileSummaryReader
{
    public async Task<FileSummaryRows> ReadAsync(FileSummaryFilter filter, CancellationToken ct)
    {
        var documents = Documents(filter.TenantId, filter.AssignedToUserId);
        // Estados como constantes: SQL Server usa los índices por estado.
        var uploads = documents.Where(file =>
            file.Status != FileStatus.PendingUpload && file.Status != FileStatus.SoftDeleted
        );
        var stamps = await uploads
            .Where(file => file.CreatedAtUtc >= filter.FromUtc)
            .OrderByDescending(file => file.CreatedAtUtc)
            .Select(file => file.CreatedAtUtc)
            .Take(filter.MaxStamps + 1)
            .ToListAsync(ct);
        var inReview = documents.Where(file => file.Status == FileStatus.PendingReview);
        var pending = await inReview.CountAsync(ct);
        var stale = pending == 0 ? 0 : await inReview.CountAsync(file => file.CreatedAtUtc < filter.StaleBeforeUtc, ct);
        var latest = await uploads
            .OrderByDescending(file => file.CreatedAtUtc)
            .ThenByDescending(file => file.Id)
            .Take(filter.Latest)
            .Select(file => new FileSummaryItem(
                file.Id,
                file.OriginalName,
                file.OwnerType,
                file.Status,
                file.CreatedAtUtc
            ))
            .ToListAsync(ct);
        return new FileSummaryRows(stamps, pending, stale, latest);
    }

    // Mismo predicado que la rama Documents de LibraryReadRepository.Visible, sin el filtro de estado. El recorte es un
    // EXISTS contra la proyección por usuario (T-1), no una lista de ids que se compara fila a fila.
    private IQueryable<FileObject> Documents(Guid tenantId, Guid? assignedToUserId)
    {
        var documents = db
            .Files.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(file =>
                file.TenantId == tenantId
                && file.SourceResourceType == null
                && (file.OwnerType == OwnerType.Customer || file.OwnerType == OwnerType.Tenant)
                && (
                    file.FolderType == FolderType.Documents
                    || file.FolderType == FolderType.Receipts
                    || file.FolderType == FolderType.Invoices
                )
            );
        if (assignedToUserId is not { } userId)
            return documents;
        var assigned = db
            .CustomerAssignmentProjections.IgnoreQueryFilters()
            .Where(a => a.TenantId == tenantId && a.UserId == userId);
        return documents.Where(file =>
            file.OwnerType == OwnerType.Tenant
            || (file.OwnerId != null && assigned.Any(a => a.CustomerId == file.OwnerId))
        );
    }
}
