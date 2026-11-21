namespace TaxVision.CloudStorage.Application.Library;

/// <summary>Topes de la búsqueda con orígenes por fila. Valores por defecto en código.</summary>
public sealed class LibraryOwnerOptions
{
    public const string SectionName = "CloudStorage:Library";

    /// <summary>Candidatos por tramo; igual al tope de ids de una llamada al dueño.</summary>
    public int Window { get; set; } = 100;

    public int MaxRounds { get; set; } = 4;

    public int OwnerTimeoutSeconds { get; set; } = 3;

    /// <summary>Tope del ámbito de Communication; cuenta dentro del presupuesto.</summary>
    public int ScopeTimeoutSeconds { get; set; } = 2;

    /// <summary>Espera total a los dueños por petición; al agotarse se corta y se devuelve el cursor.</summary>
    public int OwnerBudgetSeconds { get; set; } = 6;
}
