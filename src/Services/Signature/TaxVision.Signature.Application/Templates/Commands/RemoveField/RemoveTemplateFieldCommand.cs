namespace TaxVision.Signature.Application.Templates.Commands.RemoveField;

public sealed record RemoveTemplateFieldCommand(Guid TenantId, Guid TemplateId, Guid FieldId);
