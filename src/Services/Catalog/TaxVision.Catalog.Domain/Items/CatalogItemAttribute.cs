using BuildingBlocks.Domain;

namespace TaxVision.Catalog.Domain.Items;

/// <summary>Atributo/configuración key-value de un ítem (EAV — el mecanismo de variantes/atributos del
/// catálogo viejo). Hijo de <see cref="CatalogItem"/>.</summary>
public sealed class CatalogItemAttribute : BaseEntity
{
    public Guid CatalogItemId { get; private set; }
    public string Key { get; private set; } = default!;
    public string Value { get; private set; } = default!;

    /// <summary>Tipo semántico opcional: "string" | "number" | "boolean" | "json".</summary>
    public string? ValueType { get; private set; }

    private CatalogItemAttribute() { }

    public CatalogItemAttribute(Guid catalogItemId, string key, string value, string? valueType)
    {
        CatalogItemId = catalogItemId;
        Key = key.Trim();
        Value = value;
        ValueType = string.IsNullOrWhiteSpace(valueType) ? null : valueType.Trim();
    }
}
