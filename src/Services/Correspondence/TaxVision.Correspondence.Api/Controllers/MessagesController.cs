using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Correspondence.Api.Requests;
using TaxVision.Correspondence.Application.Compose;
using TaxVision.Correspondence.Application.Messages;
using Wolverine;

namespace TaxVision.Correspondence.Api.Controllers;

/// <summary>
/// Primer endpoint HTTP de Correspondence alcanzable por un usuario final real (empleado del
/// tenant viendo su inbox) — no M2M. TenantId siempre del JWT, nunca de la ruta/query.
/// </summary>
[ApiController]
[Route("correspondence/messages")]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class MessagesController(IMessageBus bus) : ControllerBase
{
    /// <summary>Fase 9 — metadata de UN mensaje, mismo DTO que el listado paginado del hilo. Nunca llama a Connectors (a diferencia de <see cref="GetBody"/>).</summary>
    [HttpGet("{id:guid}")]
    [HasPermission(CorrespondencePermissions.Read)]
    public async Task<IActionResult> GetMetadata(Guid id, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Forbid();

        var result = await bus.InvokeAsync<Result<MessageSummary>>(new GetMessageMetadataQuery(tenantId, id), ct);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet("{id:guid}/body")]
    [HasPermission(CorrespondencePermissions.Read)]
    public async Task<IActionResult> GetBody(Guid id, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Forbid();

        var result = await bus.InvokeAsync<Result<MessageBodyResult>>(new GetMessageBodyQuery(tenantId, id), ct);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet("{id:guid}/attachments")]
    [HasPermission(CorrespondencePermissions.Read)]
    public async Task<IActionResult> GetAttachments(Guid id, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Forbid();

        var result = await bus.InvokeAsync<Result<IReadOnlyList<AttachmentSummary>>>(
            new ListMessageAttachmentsQuery(tenantId, id),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>Fase 8 — dispara la descarga bajo demanda. Idempotente: si ya está Downloaded, devuelve el CloudStorageFileId cacheado sin volver a pedirle nada a Connectors.</summary>
    [HttpPost("{id:guid}/attachments/{attachmentId:guid}/download")]
    [HasPermission(CorrespondencePermissions.AttachmentDownload)]
    public async Task<IActionResult> DownloadAttachment(Guid id, Guid attachmentId, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId) || !User.TryGetUserId(out var userId))
            return Forbid();

        var result = await bus.InvokeAsync<Result<DownloadAttachmentResult>>(
            new DownloadAttachmentCommand(tenantId, id, attachmentId, userId),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>Fase 8 — URL presignada de un attachment ya descargado. 409 si todavía no está listo (ver <c>GetAttachmentDownloadUrlHandler</c>).</summary>
    [HttpGet("{id:guid}/attachments/{attachmentId:guid}/download-url")]
    [HasPermission(CorrespondencePermissions.AttachmentDownload)]
    public async Task<IActionResult> GetAttachmentDownloadUrl(Guid id, Guid attachmentId, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Forbid();

        var result = await bus.InvokeAsync<Result<AttachmentDownloadUrlResult>>(
            new GetAttachmentDownloadUrlQuery(tenantId, id, attachmentId),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>
    /// Fase 10/11 — arranca (o reutiliza) un reply sobre este mensaje. Vive acá y no en
    /// <see cref="DraftsController"/> porque la URL cuelga del recurso "mensaje", mismo criterio
    /// que <c>/body</c>/<c>/attachments</c> de arriba — aunque el resultado tenga forma de draft.
    /// Devuelve <c>{ draftId, subject, replyContext }</c> tal cual lo arma
    /// <see cref="StartReplyResult"/> (Fase 10) para pre-poblar el composer del frontend.
    ///
    /// <para>
    /// RBAC Fase 4 (RBAC_Hardening_Plan.md) — deliberadamente SIN chequeo de resource ownership acá
    /// (a diferencia de <see cref="DraftsController"/>): este endpoint es get-or-create, no una
    /// mutación directa sobre un draft ya identificado — no se sabe de antemano si va a reutilizar
    /// un draft existente de OTRO colega o crear uno nuevo hasta que <c>StartReplyHandler</c> ya
    /// resolvió cuál es. Reutilizar el reply abierto de un colega sobre el MISMO hilo (mismo tenant,
    /// mismo customer) es un riesgo mucho menor que los casos que sí motivan la Fase 4 (revocar
    /// el share de otro, cancelar la firma de otro) — no estaba en el alcance que pidió el plan.
    /// </para>
    /// </summary>
    [HttpPost("{id:guid}/reply/draft")]
    [HasPermission(CorrespondencePermissions.Reply)]
    public async Task<IActionResult> StartReplyDraft(Guid id, [FromBody] StartReplyBody body, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId) || !User.TryGetUserId(out var userId))
            return Forbid();

        var result = await bus.InvokeAsync<Result<StartReplyResult>>(
            new StartReplyCommand(tenantId, id, body.AccountId, userId),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
