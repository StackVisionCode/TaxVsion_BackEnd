using System.Security.Claims;

namespace TaxVision.Correspondence.Application.Abstractions;

/// <summary>
/// Resuelve qué buzones puede ver el usuario: sus personales y, con <c>connectors.accounts.office.read</c> o write,
/// también los de oficina. Nunca los personales de otro empleado. El set puede ser vacío (no ve correo); <c>null</c>
/// (sin filtro de buzón) ya no lo devuelve el resolver real.
/// </summary>
public interface IMailboxVisibilityResolver
{
    Task<IReadOnlyCollection<Guid>?> ResolveVisibleAccountIdsAsync(
        ClaimsPrincipal user,
        Guid tenantId,
        CancellationToken ct = default
    );
}
