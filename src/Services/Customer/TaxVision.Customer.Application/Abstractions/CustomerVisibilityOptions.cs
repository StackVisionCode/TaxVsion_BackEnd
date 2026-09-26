namespace TaxVision.Customer.Application.Abstractions;

/// <summary>
/// Flag de la visibilidad por asignación (acceso a clientes). Cuando <see cref="Enabled"/> es true, las
/// lecturas restringen: un empleado ve solo los clientes asignados (salvo <c>customers.view_all</c>).
/// Default FALSE = comportamiento actual (todos ven todo) — así se puede desplegar el modelo sin cortar
/// acceso hasta terminar el backfill; se enciende cuando el tenant está listo. Mismo criterio que el flag
/// de resource-ownership de Notes/Signature.
/// </summary>
public sealed class CustomerVisibilityOptions
{
    public const string SectionName = "Customers:AssignmentVisibility";

    public bool Enabled { get; set; }
}
