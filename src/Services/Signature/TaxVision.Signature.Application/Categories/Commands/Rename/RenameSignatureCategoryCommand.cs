namespace TaxVision.Signature.Application.Categories.Commands.Rename;

public sealed record RenameSignatureCategoryCommand(Guid TenantId, Guid CategoryId, string Name);
