namespace TaxVision.Catalog.Application.Categories;

/// <summary>Tope de niveles al subir por el árbol de categorías (CAT-3).</summary>
public sealed class CategoryTreeOptions
{
    public const string SectionName = "Catalog:Categories";

    public int MaxDepth { get; set; } = 50;
}
