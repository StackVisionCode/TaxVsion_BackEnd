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

namespace TaxVision.Tasks.Api.Controllers;

/// <summary>
/// El lado de la firma: pedir documentación y cerrar lo que llega. Lo que ve el cliente vive en
/// <c>Portal/PortalClientRequestsController</c>, que devuelve otra forma del mismo pedido.
/// </summary>
[ApiController]
[Route("tasks/client-requests")]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class ClientRequestsController(IMessageBus bus) : ControllerBase
{
    [HttpPost]
    [HasPermission(TasksPermissions.ClientRequestsManage)]
    [RateLimit("task.h.client_requests_write")]
    public async Task<IActionResult> Create([FromBody] CreateClientRequestRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Forbid();

        var result = await bus.InvokeAsync<Result<ClientRequestResponse>>(
            new CreateClientRequestCommand(
                tenantId,
                userId,
                request.CustomerId,
                request.TaskId,
                request.Title,
                request.Details,
                request.DueAtUtc
            ),
            ct
        );

        return result.IsFailure ? StatusCode(result.Error.ToHttpStatusCode(), result.Error) : Ok(result.Value);
    }

    /// <summary>
    /// Aceptar, rechazar o cancelar. Rechazar exige motivo: el cliente tiene que saber qué corregir.
    /// </summary>
    [HttpPost("{clientRequestId:guid}/resolve")]
    [HasPermission(TasksPermissions.ClientRequestsManage)]
    [RateLimit("task.h.client_requests_write")]
    public async Task<IActionResult> Resolve(
        Guid clientRequestId,
        [FromBody] ResolveClientRequestRequest request,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Forbid();

        var result = await bus.InvokeAsync<Result<ClientRequestResponse>>(
            new ResolveClientRequestCommand(tenantId, userId, clientRequestId, request.Resolution, request.Note),
            ct
        );

        return result.IsFailure ? StatusCode(result.Error.ToHttpStatusCode(), result.Error) : Ok(result.Value);
    }

    /// <summary>Lo que se le pidió al cliente por este encargo.</summary>
    [HttpGet("by-task/{taskId:guid}")]
    [HasPermission(TasksPermissions.Read)]
    [RateLimit("task.f.read")]
    public async Task<IActionResult> ByTask(Guid taskId, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Forbid();

        var result = await bus.InvokeAsync<Result<IReadOnlyList<ClientRequestResponse>>>(
            new ListTaskClientRequestsQuery(tenantId, taskId),
            ct
        );

        return result.IsFailure ? StatusCode(result.Error.ToHttpStatusCode(), result.Error) : Ok(result.Value);
    }
}
