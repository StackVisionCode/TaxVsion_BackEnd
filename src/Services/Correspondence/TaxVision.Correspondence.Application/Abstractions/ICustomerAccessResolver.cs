using System.Security.Claims;

namespace TaxVision.Correspondence.Application.Abstractions;

/// <summary>
/// Qué clientes puede ver el usuario en el correo: <c>null</c> = todos (<c>customers.view_all</c>); si no,
/// su userId, y los listados se acotan a los clientes que tiene asignados (proyección
/// <c>CustomerAssignmentProjections</c>). Se suma al gate de buzón, no lo reemplaza.
/// </summary>
public interface ICustomerAccessResolver
{
    Task<Guid?> ResolveRestrictedUserIdAsync(ClaimsPrincipal user, CancellationToken ct = default);
}
