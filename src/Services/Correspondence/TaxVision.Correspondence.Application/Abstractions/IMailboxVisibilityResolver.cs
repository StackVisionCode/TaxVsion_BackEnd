using System.Security.Claims;

namespace TaxVision.Correspondence.Application.Abstractions;

/// <summary>
/// Resuelve qué buzones puede ver el usuario, para ocultar el correo del buzón de oficina en los
/// listados a quien no tiene <c>connectors.accounts.office.read</c>. Devuelve <c>null</c> = ve todo
/// (tiene office.read o write) → el camino común no filtra nada; si no, el set de AccountIds
/// visibles (sus personales), que puede ser vacío (sin personal ⇒ no ve correo).
/// </summary>
public interface IMailboxVisibilityResolver
{
    Task<IReadOnlyCollection<Guid>?> ResolveVisibleAccountIdsAsync(
        ClaimsPrincipal user,
        Guid tenantId,
        CancellationToken ct = default
    );
}
