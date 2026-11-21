using BuildingBlocks.Contracts.Files;

namespace TaxVision.CloudStorage.Application.Library;

/// <summary>
/// Lo que quien llama puede leer de la biblioteca, ya resuelto en el controlador (permisos, módulos y clientes
/// asignados). El handler no vuelve a consultar permisos.
/// </summary>
/// <param name="ExplorerRestrict">Flag <c>CloudStorage:AssignmentVisibility</c>.</param>
/// <param name="AssignedCustomerIds">Vacío con <c>customers.view_all</c>.</param>
/// <param name="Variants">Variantes cumplidas de los orígenes que las tienen.</param>
public sealed record LibraryAccess(
    IReadOnlyList<LibrarySource> Readable,
    bool CanDownload,
    bool ViewAll,
    bool ExplorerRestrict,
    IReadOnlyCollection<Guid> AssignedCustomerIds,
    IReadOnlyDictionary<LibrarySource, IReadOnlyList<string>>? Variants = null
);

/// <summary>Un origen pedido y si se recorta a clientes asignados.</summary>
/// <param name="ResourceTypes">Tipos de recurso de sus variantes cumplidas; null = todos.</param>
public sealed record LibrarySourceScope(
    LibrarySource Source,
    bool Restricted,
    IReadOnlyList<string>? ResourceTypes = null
);

/// <summary>Lo que el repositorio necesita para escribir el predicado de la biblioteca.</summary>
/// <param name="Communication">Ámbito de Communication de esta petición, si se pidió.</param>
public sealed record LibraryReadScope(
    IReadOnlyList<LibrarySourceScope> Sources,
    IReadOnlyCollection<Guid> AssignedCustomerIds,
    CommunicationFileScope? Communication = null
)
{
    public bool Includes(LibrarySource source) => Sources.Any(s => s.Source == source);

    public bool IsRestricted(LibrarySource source) => Sources.Any(s => s.Source == source && s.Restricted);
}

/// <summary>Reglas puras de acceso a la biblioteca.</summary>
public static class LibraryAccessPolicy
{
    // Los orígenes por fila no se recortan en SQL: su regla la aplica el dueño.
    public static bool IsRestricted(LibraryAccess access, LibrarySource source) =>
        LibrarySourceCatalog.Get(source) is { RowScoped: false } definition
        && !access.ViewAll
        && (definition.AlwaysRestrict || access.ExplorerRestrict);

    /// <summary>Orígenes legibles que pide el filtro (todos si va vacío), en el orden del catálogo.</summary>
    public static IReadOnlyList<LibrarySource> Requested(
        LibraryAccess access,
        IReadOnlyCollection<LibrarySource> filterSources
    ) =>
        LibrarySourceCatalog
            .All.Select(d => d.Source)
            .Where(s => access.Readable.Contains(s) && (filterSources.Count == 0 || filterSources.Contains(s)))
            .ToList();

    public static LibraryReadScope ScopeFor(
        LibraryAccess access,
        IReadOnlyCollection<LibrarySource> sources,
        CommunicationFileScope? communication = null
    ) =>
        new(
            LibrarySourceCatalog
                .All.Select(d => d.Source)
                .Where(sources.Contains)
                .Select(s => new LibrarySourceScope(s, IsRestricted(access, s), AdmittedResourceTypes(access, s)))
                .ToList(),
            access.AssignedCustomerIds,
            communication
        );

    /// <summary>Tipos de recurso de las variantes cumplidas; null si el origen no tiene variantes.</summary>
    public static IReadOnlyList<string>? AdmittedResourceTypes(LibraryAccess access, LibrarySource source)
    {
        if (LibrarySourceCatalog.Get(source).Variants is not { } variants)
            return null;
        var met = access.Variants?.GetValueOrDefault(source) ?? [];
        return variants.Where(v => met.Contains(v.Id)).SelectMany(v => v.ResourceTypes).ToList();
    }

    /// <summary>Una fila solo entra si su tipo de recurso es de una variante cumplida.</summary>
    public static bool Admits(LibraryAccess access, LibrarySource source, string? resourceType) =>
        AdmittedResourceTypes(access, source) is not { } types
        || (resourceType is not null && types.Contains(resourceType));

    // Hoy previsualizar y descargar piden el mismo permiso.
    public static bool CanPreview(LibraryAccess access) => access.CanDownload;

    public static bool CanDownload(LibraryAccess access) => access.CanDownload;

    public static bool AssignmentRestricted(LibraryAccess access) =>
        access.Readable.Any(source => IsRestricted(access, source));
}
