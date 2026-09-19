namespace TaxVision.Signature.Application.Templates.Commands.Instantiate;

/// <summary>
/// Instancia una plantilla publicada como una <c>SignatureRequest</c> concreta. La
/// plantilla aporta metadata, defaults, slots y fields precolocados; el caller aporta
/// el binding real de cada slot (email + nombre) y, opcionalmente, el archivo original.
/// </summary>
/// <remarks>
/// P7: <see cref="OriginalFileId"/> es opcional. Si viene <c>null</c> y la plantilla tiene
/// <c>BaseDocumentFileId</c>, se usa ese documento base (se reutiliza la misma referencia de
/// CloudStorage — el original es inmutable, el sellado crea un archivo nuevo). Si viene, hace override.
/// </remarks>
public sealed record CreateSignatureRequestFromTemplateCommand(
    Guid TenantId,
    Guid CreatedByUserId,
    Guid TemplateId,
    Guid? OriginalFileId,
    IReadOnlyList<SlotBinding> SlotBindings,
    string? DescriptionOverride
);
