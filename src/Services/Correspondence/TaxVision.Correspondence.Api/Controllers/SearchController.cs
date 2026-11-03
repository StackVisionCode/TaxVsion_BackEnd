using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Correspondence.Api.Requests;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Application.Search;
using Wolverine;

namespace TaxVision.Correspondence.Api.Controllers;

/// <summary>
/// Búsqueda unificada del módulo Mail (buscador tipo Outlook): correos, archivos y personas de todas las carpetas con
/// el mismo control de acceso que el Inbox (buzón visible + cliente asignado). Los clientes los busca el front en
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
    /// <summary>
    /// Texto: <paramref name="q"/> (en cualquier parte) y/o <paramref name="from"/>, <paramref name="to"/>,
    /// <paramref name="subject"/> (solo ese campo); al menos uno, cada uno recortado a 2–200 caracteres, todos con Y.
    /// <paramref name="limit"/> por grupo, 10 por defecto, máximo 50. <paramref name="folder"/> restringe las fuentes;
    /// <paramref name="groups"/> (emails,files,people; por defecto los tres) evita calcular lo que no se muestra.
    /// Filtros opcionales en el servidor: <paramref name="customerId"/> o <paramref name="unlinked"/> (sin cliente),
    /// rango <paramref name="fromUtc"/>/<paramref name="toUtc"/>; <paramref name="hasAttachments"/> y
    /// <paramref name="unreadOnly"/> no filtran archivos.
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
        [FromQuery(Name = "from")] string? from,
        [FromQuery(Name = "to")] string? to,
        [FromQuery] string? subject,
        [FromQuery] string? folder,
        [FromQuery] string? groups,
        CancellationToken ct
    )
    {
        if (!MailSearchParams.TryText(q, out var term))
            return BadRequest(MailSearchParams.TermTooShort(nameof(q)));
        if (!MailSearchParams.TryText(from, out var sender))
            return BadRequest(MailSearchParams.TermTooShort(nameof(from)));
        if (!MailSearchParams.TryText(to, out var recipient))
            return BadRequest(MailSearchParams.TermTooShort(nameof(to)));
        if (!MailSearchParams.TryText(subject, out var subjectText))
            return BadRequest(MailSearchParams.TermTooShort(nameof(subject)));
        if (term is null && sender is null && recipient is null && subjectText is null)
            return BadRequest(MailSearchParams.CriteriaRequired);
        if (!MailSearchParams.TryFolder(folder, out var onlyFolder))
            return BadRequest(MailSearchParams.InvalidFolder);
        if (!MailSearchParams.TryGroups(groups, out var wanted))
            return BadRequest(MailSearchParams.InvalidGroups);
        var onlyCustomer = customerId is { } id && id != Guid.Empty ? customerId : null;
        if (unlinked && onlyCustomer is not null)
            return BadRequest(MailListParams.CustomerFilterConflict);
        if (!MailListParams.TryDateRange(fromUtc, toUtc, out var rangeFrom, out var rangeTo))
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
                    rangeFrom,
                    rangeTo,
                    sender,
                    recipient,
                    subjectText,
                    onlyFolder,
                    wanted
                )
            ),
            ct
        );
        return Ok(result);
    }
}
