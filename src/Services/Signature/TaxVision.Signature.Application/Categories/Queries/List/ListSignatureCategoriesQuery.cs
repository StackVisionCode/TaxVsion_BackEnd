namespace TaxVision.Signature.Application.Categories.Queries.List;

public sealed record ListSignatureCategoriesQuery(Guid TenantId, bool IncludeArchived);

public sealed record ListSignatureCategoriesResult(IReadOnlyList<SignatureCategoryResponse> Categories);
