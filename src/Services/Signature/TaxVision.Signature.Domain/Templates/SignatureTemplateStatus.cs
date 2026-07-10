namespace TaxVision.Signature.Domain.Templates;

/// <summary>
/// Ciclo de vida de una <c>SignatureTemplate</c>.
/// <list type="bullet">
///   <item><c>Draft</c>: en edición. Se puede modificar todo (slots, campos, metadata).</item>
///   <item><c>Published</c>: instanciable por staff. Ya no se puede editar; solo archivar.</item>
///   <item><c>Archived</c>: no aparece en el picker; no se puede instanciar.</item>
/// </list>
/// </summary>
public enum SignatureTemplateStatus
{
    Draft,
    Published,
    Archived,
}
