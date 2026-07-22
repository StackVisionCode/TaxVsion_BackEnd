using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.CloudStorage.Api.Common;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Legal;
using Wolverine;

namespace TaxVision.CloudStorage.Api.Controllers;

/// <summary>
/// Fase L1.3 — flujo de notificaciones DMCA (17 U.S.C. § 512): registro, contranotificacion y
/// reinstalacion. Staff-only (equipo legal de plataforma + tenant/uploader) — nunca CustomerPortal,
/// ninguno de los 3 permisos usados aca esta en el bundle default del rol Customer Portal.
/// </summary>
[ApiController]
[Route("storage/legal/dmca-notices")]
[Authorize]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class LegalController(IMessageBus bus, ICorrelationContext correlation) : ControllerBase
{
    public sealed record RegisterDmcaTakedownRequest(
        Guid FileId,
        string ClaimantName,
        string ClaimantEmail,
        string CopyrightedWorkDescription,
        string InfringingMaterialDescription,
        bool SwornStatementAccepted
    );

    /// <summary>Equipo legal de la plataforma registra un takedown: bloquea el archivo y lo pone bajo legal hold.</summary>
    [HttpPost]
    [HasPermission(CloudStoragePermissions.LegalManage)]
    [ProducesResponseType<object>(StatusCodes.Status201Created)]
    public async Task<IActionResult> RegisterTakedown(RegisterDmcaTakedownRequest request, CancellationToken ct)
    {
        if (!User.TryGet(out var tenantId, out var actorId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<Guid>>(
            new RegisterDmcaTakedownCommand(
                tenantId,
                actorId,
                request.FileId,
                request.ClaimantName,
                request.ClaimantEmail,
                request.CopyrightedWorkDescription,
                request.InfringingMaterialDescription,
                request.SwornStatementAccepted,
                AuditContext()
            ),
            ct
        );
        return result.IsSuccess
            ? CreatedAtAction(nameof(RegisterTakedown), new { dmcaNoticeId = result.Value })
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    public sealed record CounterNoticeRequest(string CounterNoticeText);

    /// <summary>El tenant/uploader disputa un takedown recibido sobre un archivo propio.</summary>
    [HttpPost("{dmcaNoticeId:guid}/counter-notice")]
    [HasPermission(CloudStoragePermissions.DmcaCounterNotice)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SubmitCounterNotice(
        Guid dmcaNoticeId,
        CounterNoticeRequest request,
        CancellationToken ct
    )
    {
        if (!User.TryGet(out var tenantId, out var actorId, out var scope))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(
            new SubmitDmcaCounterNoticeCommand(
                tenantId,
                actorId,
                dmcaNoticeId,
                request.CounterNoticeText,
                scope,
                AuditContext()
            ),
            ct
        );
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    public sealed record ReinstateRequest(string? ResolutionNotes);

    /// <summary>Equipo legal de la plataforma cierra el expediente reinstalando el archivo.</summary>
    [HttpPost("{dmcaNoticeId:guid}/reinstate")]
    [HasPermission(CloudStoragePermissions.LegalManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Reinstate(Guid dmcaNoticeId, ReinstateRequest request, CancellationToken ct)
    {
        if (!User.TryGet(out var tenantId, out var actorId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(
            new ReinstateDmcaFileCommand(tenantId, actorId, dmcaNoticeId, request.ResolutionNotes, AuditContext()),
            ct
        );
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    private RequestAuditContext AuditContext() =>
        new(
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            Request.Headers.UserAgent.ToString(),
            correlation.CorrelationId
        );
}
