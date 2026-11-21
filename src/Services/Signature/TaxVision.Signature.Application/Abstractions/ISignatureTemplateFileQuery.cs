namespace TaxVision.Signature.Application.Abstractions;

/// <summary>
/// Qué archivos son el PDF base de una plantilla del tenant. Las plantillas reutilizan ese archivo en cada solicitud
/// y no entran en la biblioteca (E-9): Signature no lo declara como original.
/// </summary>
public interface ISignatureTemplateFileQuery
{
    Task<IReadOnlySet<Guid>> TemplateBaseFilesAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> fileIds,
        CancellationToken ct = default
    );
}
