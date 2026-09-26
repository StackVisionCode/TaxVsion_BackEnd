namespace TaxVision.Signature.Application.Templates.Commands.PreparerFields;

/// <summary>Quita un campo de firma del preparador de una plantilla (Draft).</summary>
public sealed record RemoveTemplatePreparerFieldCommand(Guid TenantId, Guid TemplateId, Guid FieldId);
