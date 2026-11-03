using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Correspondence.Api.Requests;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Application.Search;
using Wolverine;

namespace TaxVision.Correspondence.Api.Controllers;

/// <summary>
/// Búsqueda unificada del módulo Mail (buscador tipo Outlook): correos y archivos de todas las carpetas con el
/// mismo control de acceso que el Inbox (buzón visible + cliente asignado). Los clientes los busca el front en
/// su directorio. TenantId siempre del JWT.
/// </summary>
[ApiController]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class SearchController(
    IMessageBus bus,
    IMailboxVisibilityResolver visibility,
    ICustomerAccessResolver customerAccess
) : ControllerBase
{
    private static readonly Error TermTooShort = new(
        "Correspondence.SearchTermTooShort",
        $"q must have at least {SearchTerm.MinLength} characters."
    );

    /// <summary>
    /// <paramref name="q"/> recortado (2–200 caracteres); <paramref name="limit"/> por grupo, 10 por defecto, máximo 50.
    /// Filtros opcionales en el servidor: <paramref name="customerId"/> o <paramref name="unlinked"/> (sin cliente),
    /// rango <paramref name="fromUtc"/>/<paramref name="toUtc"/>; <paramref name="hasAttachments"/> y
    /// <paramref name="unreadOnly"/> solo filtran correos.
    /// </summary>
    [HttpGet("correspondence/search")]
    [HasPermission(CorrespondencePermissions.Read)]
    [RateLimit("correspondence.h.mail_search")]
    [ProducesResponseType<MailSearchResult>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Search(
        [FromQuery] string? q,
        [FromQuery] int limit,
        [FromQuery] Guid? customerId,
        [FromQuery] bool unlinked,
        [FromQuery] bool hasAttachments,
        [FromQuery] bool unreadOnly,
        [FromQuery] DateTime? fromUtc,
        [FromQuery] DateTime? toUtc,
        CancellationToken ct
    )
    {
        var term = SearchTerm.Normalize(q);
        if (term is null || term.Length < SearchTerm.MinLength)
            return BadRequest(TermTooShort);
        var onlyCustomer = customerId is { } id && id != Guid.Empty ? customerId : null;
        if (unlinked && onlyCustomer is not null)
            return BadRequest(MailListParams.CustomerFilterConflict);
        if (!MailListParams.TryDateRange(fromUtc, toUtc, out var from, out var to))
            return BadRequest(MailListParams.InvalidDateRange);
        if (!User.TryGetTenantId(out var tenantId) || !User.TryGetUserId(out var userId))
            return Forbid();

        var visible = await visibility.ResolveVisibleAccountIdsAsync(User, tenantId, ct);
        var restrictTo = await customerAccess.ResolveRestrictedUserIdAsync(User, ct);
        var result = await bus.InvokeAsync<MailSearchResult>(
            new SearchMailQuery(
                new MailSearchCriteria(
                    tenantId,
                    term,
                    limit,
                    visible,
                    restrictTo,
                    // Borradores: los propios, salvo quien ve el buzón de oficina (mismo criterio que GET /drafts).
                    visible is null
                        ? null
                        : userId,
                    onlyCustomer,
                    unlinked,
                    hasAttachments,
                    unreadOnly,
                    from,
                    to
                )
            ),
            ct
        );
        return Ok(result);
    }
}
