using CustomerEntity = TaxVision.Customer.Domain.Customers.Customer;

namespace TaxVision.Customer.Application.Customers;

/// <summary>
/// Quién puede tocar un cliente ajeno. La lectura ya se filtra por asignación; escribir sobre uno que
/// no le tocó no puede quedar abierto solo porque el permiso de módulo lo alcance.
/// </summary>
public static class CustomerAccessPolicy
{
    /// <summary>
    /// <paramref name="canViewAllCustomers"/> (permiso <c>customers.view_all</c>) es el bypass de la
    /// oficina: el administrador trabaja sobre toda la cartera. Sin él, solo los clientes asignados.
    /// El cliente llega con sus asignaciones cargadas, así que no cuesta una consulta extra.
    /// </summary>
    public static bool CanAccess(CustomerEntity customer, Guid userId, bool canViewAllCustomers) =>
        canViewAllCustomers || customer.Assignments.Any(a => a.UserId == userId);
}
