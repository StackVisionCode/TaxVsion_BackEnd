using TaxVision.CloudStorage.Domain.Files;

namespace TaxVision.CloudStorage.Application.Abstractions;

/// <summary>Lecturas del resumen del widget Documents: solo la rama Documents de la biblioteca.</summary>
public interface IFileSummaryReader
{
    Task<FileSummaryRows> ReadAsync(FileSummaryFilter filter, CancellationToken ct);
}

/// <param name="AssignedToUserId">Solo clientes asignados a este usuario (EXISTS contra la proyección). Null = sin recorte.</param>
/// <param name="MaxStamps">Tope de marcas de la ventana; se lee una más para detectar el corte.</param>
public sealed record FileSummaryFilter(
    Guid TenantId,
    Guid? AssignedToUserId,
    DateTime FromUtc,
    DateTime StaleBeforeUtc,
    int MaxStamps,
    int Latest
);

/// <param name="CreatedInWindow">Altas de la ventana, de la más nueva a la más vieja.</param>
public sealed record FileSummaryRows(
    IReadOnlyList<DateTime> CreatedInWindow,
    int PendingReview,
    int StalePendingReview,
    IReadOnlyList<FileSummaryItem> Latest
);

public sealed record FileSummaryItem(
    Guid Id,
    string OriginalName,
    OwnerType OwnerType,
    FileStatus Status,
    DateTime CreatedAtUtc
);
