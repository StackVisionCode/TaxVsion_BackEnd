using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;

namespace TaxVision.Tasks.Application.Files;

/// <summary>
/// Vuelve a declarar a CloudStorage los archivos que Tasks ya tenía: adjuntos subidos y vivos de tareas, adjuntos no
/// retirados de formularios y el PDF de cada revisión. Solo lee y publica; con <c>dryRun</c> solo cuenta.
/// </summary>
public interface IFileSourceRedeclarer
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

/// <param name="ResourcesScanned">Tareas y form requests con al menos un archivo declarable.</param>
/// <param name="FilesDeclared">Declaraciones publicadas (o que se publicarían con <c>dryRun</c>).</param>
public sealed record RedeclareFileSourcesReport(bool DryRun, int ResourcesScanned, int FilesDeclared);
