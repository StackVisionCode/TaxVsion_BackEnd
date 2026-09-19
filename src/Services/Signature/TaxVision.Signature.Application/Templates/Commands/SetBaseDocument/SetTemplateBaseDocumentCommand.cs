namespace TaxVision.Signature.Application.Templates.Commands.SetBaseDocument;

/// <summary>
/// Fija (o quita con <c>null</c>) el documento base de una plantilla (P7). El archivo ya fue subido a
/// CloudStorage por el caller; aquí solo se guarda la referencia. Solo aplica en Draft.
/// </summary>
public sealed record SetTemplateBaseDocumentCommand(Guid TenantId, Guid TemplateId, Guid? BaseDocumentFileId);
