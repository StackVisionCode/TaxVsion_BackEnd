using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Correspondence.Application.Messages;
using TaxVision.Correspondence.Application.Threads;
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
public sealed class ThreadsController(IMessageBus bus) : ControllerBase
{
    private const int DefaultSize = 20;

    [HttpGet("correspondence/customers/{customerId:guid}/threads")]
    [HasPermission(CorrespondencePermissions.Read)]
    public async Task<IActionResult> ListCustomerThreads(
        Guid customerId,
        [FromQuery] int page,
        [FromQuery] int size,
        CancellationToken ct
    )
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Forbid();

        var result = await bus.InvokeAsync<PagedResult<ThreadSummary>>(
            new ListCustomerThreadsQuery(tenantId, customerId, NormalizePage(page), NormalizeSize(size)),
            ct
        );
        return Ok(result);
    }

    [HttpGet("correspondence/threads/{threadId:guid}/messages")]
    [HasPermission(CorrespondencePermissions.Read)]
    public async Task<IActionResult> ListThreadMessages(
        Guid threadId,
        [FromQuery] int page,
        [FromQuery] int size,
        CancellationToken ct
    )
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Forbid();

        var result = await bus.InvokeAsync<Result<PagedResult<MessageSummary>>>(
            new ListThreadMessagesQuery(tenantId, threadId, NormalizePage(page), NormalizeSize(size)),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("correspondence/threads/{threadId:guid}/archive")]
    [HasPermission(CorrespondencePermissions.Read)]
    public async Task<IActionResult> Archive(Guid threadId, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Forbid();

        var result = await bus.InvokeAsync<Result>(new ArchiveThreadCommand(tenantId, threadId), ct);
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    // Defaults/clamping finales viven en el repositorio (EmailThreadRepository/IncomingEmailRepository,
    // mismo criterio que SignatureRequestReadService); acá solo se evita mandar un page/size
    // negativo o cero explícito desde un query string ausente ([FromQuery] int deja 0 por default).
    private static int NormalizePage(int page) => page < 1 ? 1 : page;

    private static int NormalizeSize(int size) => size < 1 ? DefaultSize : size;
}
