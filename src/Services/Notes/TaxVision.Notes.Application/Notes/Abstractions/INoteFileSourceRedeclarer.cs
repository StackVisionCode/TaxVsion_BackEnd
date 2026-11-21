using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;

namespace TaxVision.Notes.Application.Notes.Abstractions;

/// <summary>
/// Vuelve a declarar a CloudStorage los adjuntos que Notes ya tenía: los de notas no borradas, sin retirados ni
/// rechazados. Solo lee y publica; con <c>dryRun</c> solo cuenta.
/// </summary>
public interface INoteFileSourceRedeclarer
{
    Task<RedeclareFileSourcesReport> RedeclareAsync(Guid tenantId, bool dryRun, CancellationToken ct);

    /// <summary>Las mismas declaraciones, solo de esos archivos de la oficina; los ajenos no salen.</summary>
    Task<IReadOnlyList<FileSourceDeclaredIntegrationEvent>> DeclarationsForAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> fileIds,
        string correlationId,
        CancellationToken ct
    );
}

/// <param name="ResourcesScanned">Notas con al menos un adjunto declarable.</param>
/// <param name="FilesDeclared">Declaraciones publicadas (o que se publicarían con <c>dryRun</c>).</param>
public sealed record RedeclareFileSourcesReport(bool DryRun, int ResourcesScanned, int FilesDeclared);
