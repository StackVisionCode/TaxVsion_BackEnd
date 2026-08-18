using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Tasks.Api.Requests;
using TaxVision.Tasks.Application.ClientRequests;
using TaxVision.Tasks.Application.ClientRequests.Commands;
using TaxVision.Tasks.Application.ClientRequests.Queries;
using Wolverine;

namespace TaxVision.Tasks.Api.Controllers.Portal;

/// <summary>
/// La lista del cliente: qué le pidió su contador y qué ya mandó. Es la única superficie de Task
/// abierta a <see cref="ActorType.CustomerPortal"/>, y por eso vive en su propio namespace — la
/// fitness function deja pasar <c>Portal</c> y bloquea al resto.
///
/// <para>
/// <b>El identificador del cliente sale del token, nunca de la petición.</b> Aceptarlo del cliente
/// convierte cambiar un id en la URL en leer el expediente de otro.
/// </para>
/// </summary>
[ApiController]
[Route("tasks/portal/client-requests")]
[AllowActorTypes(ActorType.CustomerPortal)]
[HasPermission(TasksPermissions.PortalClientRequests)]
public sealed class PortalClientRequestsController(IMessageBus bus) : ControllerBase
{
    [HttpGet]
    [RateLimit("task.f.portal_read")]
    public async Task<IActionResult> List([FromQuery] bool onlyOpen = true, CancellationToken ct = default)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _) || !User.TryGetCustomerId(out var customerId))
            return Forbid();

        var result = await bus.InvokeAsync<Result<IReadOnlyList<PortalClientRequestResponse>>>(
            new ListPortalClientRequestsQuery(tenantId, customerId, onlyOpen),
            ct
        );

        return result.IsFailure ? StatusCode(result.Error.ToHttpStatusCode(), result.Error) : Ok(result.Value);
    }

    /// <summary>
    /// Registra el archivo que el cliente acaba de subir a CloudStorage con su propio token. Por aquí
    /// no pasa el byte: llega el id y el pedido queda a la espera del escaneo.
    /// </summary>
    [HttpPost("{clientRequestId:guid}/documents")]
    [RateLimit("task.h.portal_submit")]
    public async Task<IActionResult> SubmitDocument(
        Guid clientRequestId,
        [FromBody] SubmitClientDocumentRequest request,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _) || !User.TryGetCustomerId(out var customerId))
            return Forbid();

        var result = await bus.InvokeAsync<Result<PortalClientRequestResponse>>(
            new SubmitClientDocumentCommand(
                tenantId,
                customerId,
                clientRequestId,
                request.FileId,
                request.DisplayName,
                request.ContentType,
                request.SizeBytes
            ),
            ct
        );

        return result.IsFailure ? StatusCode(result.Error.ToHttpStatusCode(), result.Error) : Ok(result.Value);
    }
}
