namespace TaxVision.Customer.Application.Customers;

/// <summary>
/// Filtro del listado por persona del staff: <see cref="PreparerUserId"/> = preparador primary;
/// <see cref="AssignedUserId"/> = asignado (primary o adicional). Se combina con los demás filtros y el ABAC.
/// </summary>
public sealed record CustomerStaffFilter(Guid? PreparerUserId = null, Guid? AssignedUserId = null)
{
    /// <summary>null si no viene ninguno (el listado queda igual que antes).</summary>
    public static CustomerStaffFilter? From(Guid? preparerUserId, Guid? assignedUserId) =>
        preparerUserId is null && assignedUserId is null ? null : new(preparerUserId, assignedUserId);

    /// <summary>True si alguno apunta a alguien distinto del actor (exige <c>users.work.view</c>).</summary>
    public bool TargetsOtherThan(Guid actorUserId) =>
        PreparerUserId is { } preparer && preparer != actorUserId
        || AssignedUserId is { } assigned && assigned != actorUserId;
}
