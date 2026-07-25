using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.ResourceAuthorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TaxVision.Correspondence.Api.Requests;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Application.Compose;
using TaxVision.Correspondence.Domain.Compose;
using Wolverine;

namespace TaxVision.Correspondence.Api.Controllers;

/// <summary>
/// Fase 11 — CRUD de <c>Draft</c> (crear, leer, autoguardar, descartar). Fase 12 agrega
/// <c>/drafts/{id}/attachments/...</c> — referencias a archivos ya subidos a CloudStorage, nunca
/// bytes. Fase 14 agrega <c>/drafts/{id}/send</c> — llamada síncrona y bloqueante a Postmaster, el
/// cierre de la cadena completa (plan §0/§14). <c>POST /correspondence/messages/{id}/reply/draft</c>
/// vive en <see cref="MessagesController"/> en cambio, junto a <c>/body</c>/<c>/attachments</c>:
/// aunque devuelve forma de draft, la URL cuelga del recurso "mensaje" (mismo criterio que
/// <see cref="ThreadsController"/> ya documenta para <c>GET /messages/{id}</c>). TenantId siempre
/// del JWT, nunca de la ruta/body.
/// </summary>
[ApiController]
[Route("correspondence/drafts")]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class DraftsController(
    IMessageBus bus,
    IDraftRepository drafts,
    IAuthorizationService authorizationService,
    IOptionsMonitor<ResourceOwnershipOptions> ownershipOptions
) : ControllerBase
{
    private const int DefaultSize = 20;

    /// <summary>
    /// RBAC Fase 4 (RBAC_Hardening_Plan.md) — chequeo de ownership tras flag, compartido por
    /// AutoSave/Discard/AttachFile/RemoveAttachment/Send. Sin permiso "manage" de override para
    /// Draft (a diferencia de ShareLink/SignatureRequest): el plan no lo pidió para Correspondence,
    /// así que solo el creador (o PlatformAdmin) puede operar sobre un draft ajeno una vez
    /// activado. Los drafts previos a esta fase tienen <see cref="Draft.CreatedByUserId"/> en
    /// <see cref="Guid.Empty"/> (backfill imposible — no hay fuente de verdad histórica) y por lo
    /// tanto quedan fuera del alcance de cualquier no-PlatformAdmin mientras el flag esté prendido.
    /// </summary>
    private async Task<IActionResult?> CheckOwnershipAsync(
        Guid tenantId,
        Guid draftId,
        Microsoft.AspNetCore.Authorization.Infrastructure.OperationAuthorizationRequirement operation,
        CancellationToken ct
    )
    {
        if (!ownershipOptions.CurrentValue.Enabled)
            return null;

        var existing = await drafts.GetByIdAsync(tenantId, draftId, ct);
        if (existing is null)
            return null;

        var authorized = await authorizationService.AuthorizeAsync(User, existing, operation);
        return authorized.Succeeded
            ? null
            : StatusCode(
                StatusCodes.Status403Forbidden,
                new Error("Draft.NotOwner", "Only the draft's creator can perform this action.")
            );
    }

    /// <summary>
    /// Fase 15 — "retomar un autoguardado": drafts abiertos (<c>Status=Draft</c>) del customer,
    /// más reciente primero. Lean por diseño (<see cref="DraftListItem"/>) — para el composer
    /// completo de UNO, ver <see cref="GetById"/>.
    /// </summary>
    [HttpGet]
    [HasPermission(CorrespondencePermissions.Compose)]
    public async Task<IActionResult> List(
        [FromQuery] Guid customerId,
        [FromQuery] int page,
        [FromQuery] int size,
        CancellationToken ct
    )
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Forbid();

        var result = await bus.InvokeAsync<PagedResult<DraftListItem>>(
            new ListDraftsQuery(tenantId, customerId, NormalizePage(page), NormalizeSize(size)),
            ct
        );
        return Ok(result);
    }

    [HttpPost]
    [HasPermission(CorrespondencePermissions.Compose)]
    public async Task<IActionResult> Create([FromBody] CreateDraftBody body, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId) || !User.TryGetUserId(out var userId))
            return Forbid();

        var result = await bus.InvokeAsync<Result<Guid>>(
            new CreateDraftCommand(tenantId, body.CustomerId, body.AccountId, userId),
            ct
        );
        return result.IsSuccess
            ? Ok(new { draftId = result.Value })
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet("{id:guid}")]
    [HasPermission(CorrespondencePermissions.Compose)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Forbid();

        var result = await bus.InvokeAsync<Result<DraftDetail>>(new GetDraftQuery(tenantId, id), ct);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>204, sin body — mismo criterio que no hay precedente PATCH previo en este servicio, así que se elige la convención más estándar para un PATCH que no tiene nada útil que devolver.</summary>
    [HttpPatch("{id:guid}")]
    [HasPermission(CorrespondencePermissions.Compose)]
    public async Task<IActionResult> AutoSave(Guid id, [FromBody] AutoSaveDraftBody body, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Forbid();

        var forbidden = await CheckOwnershipAsync(tenantId, id, Operations.Update, ct);
        if (forbidden is not null)
            return forbidden;

        var result = await bus.InvokeAsync<Result>(ToCommand(tenantId, id, body), ct);
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpDelete("{id:guid}")]
    [HasPermission(CorrespondencePermissions.Compose)]
    public async Task<IActionResult> Discard(Guid id, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Forbid();

        var forbidden = await CheckOwnershipAsync(tenantId, id, Operations.Delete, ct);
        if (forbidden is not null)
            return forbidden;

        var result = await bus.InvokeAsync<Result>(new DiscardDraftCommand(tenantId, id), ct);
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>204, sin body — mismo criterio que <see cref="AutoSave"/>.</summary>
    [HttpPost("{id:guid}/attachments")]
    [HasPermission(CorrespondencePermissions.Compose)]
    public async Task<IActionResult> AttachFile(Guid id, [FromBody] AttachFileToDraftBody body, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Forbid();

        var forbidden = await CheckOwnershipAsync(tenantId, id, Operations.Update, ct);
        if (forbidden is not null)
            return forbidden;

        var command = new AttachFileToDraftCommand(
            tenantId,
            id,
            body.FileId,
            body.Filename,
            body.ContentType,
            body.SizeBytes
        );
        var result = await bus.InvokeAsync<Result>(command, ct);
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpDelete("{id:guid}/attachments/{fileId:guid}")]
    [HasPermission(CorrespondencePermissions.Compose)]
    public async Task<IActionResult> RemoveAttachment(Guid id, Guid fileId, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Forbid();

        var forbidden = await CheckOwnershipAsync(tenantId, id, Operations.Update, ct);
        if (forbidden is not null)
            return forbidden;

        var result = await bus.InvokeAsync<Result>(new RemoveDraftAttachmentCommand(tenantId, id, fileId), ct);
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>
    /// Fase 14 — llama a Postmaster de forma síncrona y bloqueante; esta request HTTP no responde
    /// hasta tener el resultado real del envío (plan §0/§14). Permiso separado de
    /// <see cref="CorrespondencePermissions.Compose"/> (plan §27): redactar es reversible, enviar
    /// no lo es.
    /// </summary>
    [HttpPost("{id:guid}/send")]
    [HasPermission(CorrespondencePermissions.Send)]
    public async Task<IActionResult> Send(Guid id, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId) || !User.TryGetUserId(out var userId))
            return Forbid();

        var forbidden = await CheckOwnershipAsync(tenantId, id, Operations.Send, ct);
        if (forbidden is not null)
            return forbidden;

        var result = await bus.InvokeAsync<Result<SendDraftResult>>(new SendDraftCommand(tenantId, id, userId), ct);
        return result.IsSuccess
            ? Ok(new { sentMessageId = result.Value.SentMessageId, providerMessageId = result.Value.ProviderMessageId })
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    private static AutoSaveDraftCommand ToCommand(Guid tenantId, Guid draftId, AutoSaveDraftBody body) =>
        new(
            tenantId,
            draftId,
            body.Subject,
            body.HtmlBody,
            body.TextBody,
            ToRecipients(body.To),
            ToRecipients(body.Cc),
            ToRecipients(body.Bcc)
        );

    private static IReadOnlyList<AutoSaveDraftRecipientInput>? ToRecipients(IReadOnlyList<DraftRecipientBody>? body) =>
        body?.Select(r => new AutoSaveDraftRecipientInput(r.Address, r.DisplayName)).ToList();

    // Mismo criterio que ThreadsController: defaults/clamping finales viven en el repositorio,
    // acá solo se evita mandar page/size negativo o cero explícito desde un query string ausente.
    private static int NormalizePage(int page) => page < 1 ? 1 : page;

    private static int NormalizeSize(int size) => size < 1 ? DefaultSize : size;
}
