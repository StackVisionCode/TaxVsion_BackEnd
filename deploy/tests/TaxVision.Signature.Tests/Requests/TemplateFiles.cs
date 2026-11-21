using TaxVision.Signature.Application.Abstractions;

namespace TaxVision.Signature.Tests.Requests;

/// <summary>Plantillas falsas: los archivos dados son PDF base de alguna plantilla del tenant.</summary>
internal sealed class TemplateFiles(params Guid[] baseFiles) : ISignatureTemplateFileQuery
{
    public static readonly TemplateFiles None = new();

    public Task<IReadOnlySet<Guid>> TemplateBaseFilesAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> fileIds,
        CancellationToken ct = default
    ) => Task.FromResult<IReadOnlySet<Guid>>(fileIds.Where(baseFiles.Contains).ToHashSet());
}
