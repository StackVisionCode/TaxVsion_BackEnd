namespace TaxVision.Signature.Application.Categories;

/// <summary>
/// Una categoría disponible para el tenant. Las de sistema no tienen <see cref="Id"/> (son fijas);
/// las custom sí, y pueden estar archivadas.
/// </summary>
public sealed record SignatureCategoryResponse(Guid? Id, string Name, bool IsSystem, bool IsArchived);
