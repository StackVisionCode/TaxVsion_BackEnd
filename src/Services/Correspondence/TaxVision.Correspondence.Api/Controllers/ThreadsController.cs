using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Application.Messages;
using TaxVision.Correspondence.Application.Threads;
using TaxVision.Correspondence.Domain.Inbox;
using Wolverine;

namespace TaxVision.Correspondence.Api.Controllers;

/// <summary>
/// Fase 9 — rutas de thread del inbox del cliente final. Dos familias de recursos conviven acá
/// (hilos de un customer, mensajes/acciones de UN hilo) porque ambas giran en torno a
/// <see cref="Domain.Inbox.EmailThread"/>; <c>GET /correspondence/messages/{id}</c> (metadata de
/// UN mensaje) vive en <see cref="MessagesController"/> en cambio, junto a
/// <c>/body</c> y <c>/attachments</c>, que son del mismo recurso. TenantId siempre del JWT,
/// nunca de la ruta/query — mismo criterio que el resto de Correspondence.Api.
/// </summary>
[ApiController]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class ThreadsController(
    IMessageBus bus,
    IMailboxVisibilityResolver visibility,
    ICustomerAccessResolver customerAccess,
    ICorrelationContext correlation
) : ControllerBase
{
    private const int DefaultSize = 20;

    /// <summary>
    /// Inbox de la oficina: hilos de todos los clientes que el usuario puede ver (buzón visible y, sin
    /// <c>customers.view_all</c>, solo clientes asignados). <paramref name="status"/> Active (por defecto), Archived o all;
    /// <paramref name="customerId"/> filtra a un cliente; <paramref name="q"/> busca en asunto, remitente y vista previa.
    /// </summary>
    [HttpGet("correspondence/threads")]
    [HasPermission(CorrespondencePermissions.Read)]
    [RateLimit("correspondence.f.thread_read")]
    public async Task<IActionResult> ListInboxThreads(
        [FromQuery] string? status,
        [FromQuery] Guid? customerId,
        [FromQuery] bool unreadOnly,
        [FromQuery] string? q,
        [FromQuery] int page,
        [FromQuery] int size,
        CancellationToken ct
    )
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Forbid();
        if (!TryParseStatus(status, out var threadStatus))
            return BadRequest(InvalidStatus);

        var visible = await visibility.ResolveVisibleAccountIdsAsync(User, tenantId, ct);
        var restrictTo = await customerAccess.ResolveRestrictedUserIdAsync(User, ct);
        var result = await bus.InvokeAsync<PagedResult<InboxThreadSummary>>(
            new ListInboxThreadsQuery(
                new InboxThreadFilter(
                    tenantId,
                    threadStatus,
                    customerId is { } id && id != Guid.Empty ? id : null,
                    unreadOnly,
                    string.IsNullOrWhiteSpace(q) ? null : q.Trim()[..Math.Min(q.Trim().Length, MaxSearchLength)],
                    NormalizePage(page),
                    NormalizeSize(size),
                    visible,
                    restrictTo
                )
            ),
            ct
        );
        return Ok(result);
    }

    /// <summary>No leídos de los hilos activos que el usuario puede ver (badges del menú y del Inbox).</summary>
    [HttpGet("correspondence/unread-count")]
    [HasPermission(CorrespondencePermissions.Read)]
    [RateLimit("correspondence.f.thread_read")]
    public async Task<IActionResult> GetUnreadCount(CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Forbid();

        var visible = await visibility.ResolveVisibleAccountIdsAsync(User, tenantId, ct);
        var restrictTo = await customerAccess.ResolveRestrictedUserIdAsync(User, ct);
        var result = await bus.InvokeAsync<UnreadMailCount>(
            new GetUnreadMailCountQuery(tenantId, visible, restrictTo),
            ct
        );
        return Ok(result);
    }

    [HttpGet("correspondence/customers/{customerId:guid}/threads")]
    [HasPermission(CorrespondencePermissions.Read)]
    [RateLimit("correspondence.f.thread_read")]
    public async Task<IActionResult> ListCustomerThreads(
        Guid customerId,
        [FromQuery] int page,
        [FromQuery] int size,
        CancellationToken ct
    )
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Forbid();

        var visible = await visibility.ResolveVisibleAccountIdsAsync(User, tenantId, ct);
        var restrictTo = await customerAccess.ResolveRestrictedUserIdAsync(User, ct);
        var result = await bus.InvokeAsync<PagedResult<ThreadSummary>>(
            new ListCustomerThreadsQuery(
                tenantId,
                customerId,
                NormalizePage(page),
                NormalizeSize(size),
                visible,
                restrictTo
            ),
            ct
        );
        return Ok(result);
    }

    [HttpGet("correspondence/threads/{threadId:guid}/messages")]
    [HasPermission(CorrespondencePermissions.Read)]
    [RateLimit("correspondence.f.thread_read")]
    public async Task<IActionResult> ListThreadMessages(
        Guid threadId,
        [FromQuery] int page,
        [FromQuery] int size,
        CancellationToken ct
    )
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Forbid();

        var visible = await visibility.ResolveVisibleAccountIdsAsync(User, tenantId, ct);
        var result = await bus.InvokeAsync<Result<PagedResult<MessageSummary>>>(
            new ListThreadMessagesQuery(
                tenantId,
                threadId,
                NormalizePage(page),
                NormalizeSize(size),
                visible,
                await customerAccess.ResolveRestrictedUserIdAsync(User, ct)
            ),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("correspondence/threads/{threadId:guid}/archive")]
    [HasPermission(CorrespondencePermissions.Manage)]
    [RateLimit("correspondence.g.thread_manage")]
    public async Task<IActionResult> Archive(Guid threadId, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Forbid();

        var (visible, restrictTo, actor) = await ResolveActionScopeAsync(tenantId, ct);
        var result = await bus.InvokeAsync<Result>(
            new ArchiveThreadCommand(tenantId, threadId, visible, restrictTo, actor, correlation.CorrelationId),
            ct
        );
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("correspondence/threads/{threadId:guid}/unarchive")]
    [HasPermission(CorrespondencePermissions.Manage)]
    [RateLimit("correspondence.g.thread_manage")]
    public async Task<IActionResult> Unarchive(Guid threadId, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Forbid();

        var (visible, restrictTo, actor) = await ResolveActionScopeAsync(tenantId, ct);
        var result = await bus.InvokeAsync<Result>(
            new UnarchiveThreadCommand(tenantId, threadId, visible, restrictTo, actor, correlation.CorrelationId),
            ct
        );
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>Marca TODOS los correos inbound del hilo como leídos ("mark all as read"). Idempotente.</summary>
    [HttpPost("correspondence/threads/{threadId:guid}/read")]
    [HasPermission(CorrespondencePermissions.Read)]
    [RateLimit("correspondence.g.thread_manage")]
    public Task<IActionResult> MarkAllRead(Guid threadId, CancellationToken ct) =>
        SetThreadReadState(threadId, isRead: true, ct);

    /// <summary>Marca TODOS los correos inbound del hilo como NO leídos ("all as unread"). Idempotente.</summary>
    [HttpPost("correspondence/threads/{threadId:guid}/unread")]
    [HasPermission(CorrespondencePermissions.Read)]
    [RateLimit("correspondence.g.thread_manage")]
    public Task<IActionResult> MarkAllUnread(Guid threadId, CancellationToken ct) =>
        SetThreadReadState(threadId, isRead: false, ct);

    private async Task<IActionResult> SetThreadReadState(Guid threadId, bool isRead, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Forbid();

        var (visible, restrictTo, actor) = await ResolveActionScopeAsync(tenantId, ct);
        var result = await bus.InvokeAsync<Result>(
            new SetThreadReadStateCommand(
                tenantId,
                threadId,
                isRead,
                visible,
                restrictTo,
                actor,
                correlation.CorrelationId
            ),
            ct
        );
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>Gate de las acciones sobre un hilo (buzón y cliente asignado) y quién actúa, para el aviso en vivo.</summary>
    private async Task<(IReadOnlyCollection<Guid>? Visible, Guid? RestrictTo, Guid? Actor)> ResolveActionScopeAsync(
        Guid tenantId,
        CancellationToken ct
    )
    {
        var visible = await visibility.ResolveVisibleAccountIdsAsync(User, tenantId, ct);
        var restrictTo = await customerAccess.ResolveRestrictedUserIdAsync(User, ct);
        return (visible, restrictTo, User.TryGetUserId(out var userId) ? userId : null);
    }

    // Defaults/clamping finales viven en el repositorio (EmailThreadRepository/IncomingEmailRepository,
    // mismo criterio que SignatureRequestReadService); acá solo se evita mandar un page/size
    // negativo o cero explícito desde un query string ausente ([FromQuery] int deja 0 por default).
    private static int NormalizePage(int page) => page < 1 ? 1 : page;

    private const int MaxSearchLength = 200;

    private static readonly Error InvalidStatus = new(
        "Correspondence.InvalidThreadStatus",
        "status must be Active, Archived or all."
    );

    // Sin status = Active (el inbox); Archived = carpeta de archivados; all = ambos (una sola lista que la UI separa).
    private static bool TryParseStatus(string? raw, out EmailThreadStatus? status)
    {
        status = EmailThreadStatus.Active;
        if (string.IsNullOrWhiteSpace(raw))
            return true;
        if (string.Equals(raw, "all", StringComparison.OrdinalIgnoreCase))
        {
            status = null;
            return true;
        }
        if (Enum.TryParse<EmailThreadStatus>(raw, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
        {
            status = parsed;
            return true;
        }
        return false;
    }

    private static int NormalizeSize(int size) => size < 1 ? DefaultSize : size;
}
