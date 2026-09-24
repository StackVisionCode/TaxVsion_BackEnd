namespace TaxVision.Signature.Application.Categories.Commands.Archive;

/// <summary><paramref name="Archived"/> true = archivar, false = restaurar.</summary>
public sealed record SetSignatureCategoryArchivedCommand(Guid TenantId, Guid CategoryId, bool Archived);
