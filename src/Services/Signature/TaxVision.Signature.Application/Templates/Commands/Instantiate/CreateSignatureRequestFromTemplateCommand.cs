namespace TaxVision.Signature.Application.Templates.Commands.Instantiate;

/// <summary>
/// Instancia una plantilla publicada como una <c>SignatureRequest</c> concreta. La
/// plantilla aporta metadata, defaults, slots y fields precolocados; el caller aporta
/// el archivo original y el binding real de cada slot (email + nombre).
/// </summary>
public sealed record CreateSignatureRequestFromTemplateCommand(
    Guid TenantId,
    Guid CreatedByUserId,
    Guid TemplateId,
    Guid OriginalFileId,
    IReadOnlyList<SlotBinding> SlotBindings,
    string? DescriptionOverride
);
