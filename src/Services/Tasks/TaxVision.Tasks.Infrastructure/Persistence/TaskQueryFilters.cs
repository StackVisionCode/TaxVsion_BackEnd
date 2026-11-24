namespace TaxVision.Tasks.Infrastructure.Persistence;

/// <summary>
/// Filtros globales con nombre. Las lecturas entre oficinas ignoran solo <see cref="Tenant"/>: así lo que está en la
/// papelera sigue fuera aunque la consulta fije la oficina a mano.
/// </summary>
public static class TaskQueryFilters
{
    public const string Tenant = "Tenant";
    public const string Trash = "Trash";

    /// <summary>Para <c>IgnoreQueryFilters</c>: un arreglo y no una colección literal, válido dentro de subconsultas.</summary>
    public static readonly string[] TenantOnly = [Tenant];
}
