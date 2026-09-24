namespace TaxVision.Signature.Application.Categories.Commands.Create;

public sealed record CreateSignatureCategoryCommand(Guid TenantId, Guid CreatedByUserId, string Name);
