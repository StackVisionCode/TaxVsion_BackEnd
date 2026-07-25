using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.ResourceAuthorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TaxVision.Signature.Api.Requests;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Requests;
using TaxVision.Signature.Application.Requests.Commands.AddSigner;
using TaxVision.Signature.Application.Requests.Commands.Cancel;
using TaxVision.Signature.Application.Requests.Commands.ClearPractitionerPin;
using TaxVision.Signature.Application.Requests.Commands.ClearPreparer;
using TaxVision.Signature.Application.Requests.Commands.Create;
using TaxVision.Signature.Application.Requests.Commands.ExtendExpiration;
using TaxVision.Signature.Application.Requests.Commands.LegalHold;
using TaxVision.Signature.Application.Requests.Commands.PlaceField;
using TaxVision.Signature.Application.Requests.Commands.RemoveField;
using TaxVision.Signature.Application.Requests.Commands.RemoveSigner;
using TaxVision.Signature.Application.Requests.Commands.ReorderSigners;
using TaxVision.Signature.Application.Requests.Commands.ResendSignerInvitation;
using TaxVision.Signature.Application.Requests.Commands.Send;
using TaxVision.Signature.Application.Requests.Commands.SetPractitionerPin;
using TaxVision.Signature.Application.Requests.Commands.SetPreparer;
using TaxVision.Signature.Application.Requests.Commands.SignAsPreparer;
using TaxVision.Signature.Application.Requests.Queries.GetById;
using TaxVision.Signature.Application.Requests.Queries.List;
using TaxVision.Signature.Domain.Requests;
using Wolverine;

namespace TaxVision.Signature.Api.Controllers;

/// <summary>
/// Endpoints staff del ciclo de vida de solicitudes de firma. Todos los TenantId
/// vienen del JWT — nunca del cuerpo/query. Autorización por permisos granulares.
/// </summary>
[ApiController]
[Route("signature/requests")]
[Authorize]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class SignatureRequestsController(
    IMessageBus bus,
    ISignatureRequestRepository signatureRequests,
    IAuthorizationService authorizationService,
    IOptionsMonitor<ResourceOwnershipOptions> ownershipOptions
) : ControllerBase
{
    /// <summary>
    /// RBAC Fase 4 (RBAC_Hardening_Plan.md) — chequeo de ownership tras flag, compartido por
    /// Send/Cancel/ExtendExpiration. Mismo criterio que ShareLinksController.CheckOwnershipAsync:
    /// si el flag está apagado (default) o la solicitud ya no existe, no bloquea nada acá — el 404
    /// real lo sigue devolviendo el handler de siempre.
    /// </summary>
    private async Task<IActionResult?> CheckOwnershipAsync(
        Guid tenantId,
        Guid signatureRequestId,
        Microsoft.AspNetCore.Authorization.Infrastructure.OperationAuthorizationRequirement operation,
        CancellationToken ct
    )
    {
        if (!ownershipOptions.CurrentValue.Enabled)
            return null;

        var existing = await signatureRequests.GetByIdAsync(tenantId, signatureRequestId, ct);
        if (existing is null)
            return null;

        var authorized = await authorizationService.AuthorizeAsync(User, existing, operation);
        return authorized.Succeeded
            ? null
            : StatusCode(
                StatusCodes.Status403Forbidden,
                new Error(
                    "SignatureRequest.NotOwner",
                    "Only the request's creator or a user with signature-request-management permission can perform this action."
                )
            );
    }

    // ---------- POST /signature/requests ----------
    [HttpPost]
    [HasPermission(SignaturePermissions.RequestCreate)]
    [ProducesResponseType<SignatureRequestResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateSignatureRequestBody body, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var cmd = new CreateSignatureRequestCommand(
            tenantId,
            userId,
            body.Title,
            body.Description,
            body.Category,
            body.OriginalFileId,
            body.TokenExpirationHours,
            body.RequiresSequentialSigning,
            body.RequiresConsent,
            body.GenerateCertificate
        );

        var result = await bus.InvokeAsync<Result<SignatureRequestResponse>>(cmd, ct);
        return result.IsSuccess
            ? Created($"/signature/requests/{result.Value.Id}", result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    // ---------- GET /signature/requests ----------
    [HttpGet]
    [HasPermission(SignaturePermissions.RequestRead)]
    [ProducesResponseType<ListSignatureRequestsResult>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ListSignatureRequestsResult>> List(
        [FromQuery] SignatureRequestStatus? status = null,
        [FromQuery] SignatureCategory? category = null,
        [FromQuery] int page = 1,
        [FromQuery] int size = 20,
        CancellationToken ct = default
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<ListSignatureRequestsResult>(
            new ListSignatureRequestsQuery(tenantId, status, category, page, size),
            ct
        );
        return Ok(result);
    }

    // ---------- GET /signature/requests/{id} ----------
    [HttpGet("{id:guid}")]
    [HasPermission(SignaturePermissions.RequestRead)]
    [ProducesResponseType<SignatureRequestResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<SignatureRequestResponse?>(
            new GetSignatureRequestByIdQuery(tenantId, id),
            ct
        );
        return result is null ? NotFound() : Ok(result);
    }

    // ---------- POST /signature/requests/{id}/signers ----------
    [HttpPost("{id:guid}/signers")]
    [HasPermission(SignaturePermissions.RequestCreate)]
    [ProducesResponseType<SignerResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddSigner([FromRoute] Guid id, [FromBody] AddSignerBody body, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var cmd = new AddSignerCommand(tenantId, id, body.Email, body.FullName);
        var result = await bus.InvokeAsync<Result<SignerResponse>>(cmd, ct);
        return result.IsSuccess
            ? Created($"/signature/requests/{id}/signers/{result.Value.Id}", result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    // ---------- DELETE /signature/requests/{id}/signers/{signerId} ----------
    [HttpDelete("{id:guid}/signers/{signerId:guid}")]
    [HasPermission(SignaturePermissions.RequestCreate)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveSigner([FromRoute] Guid id, [FromRoute] Guid signerId, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(new RemoveSignerCommand(tenantId, id, signerId), ct);
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    // ---------- PUT /signature/requests/{id}/signers/order ----------
    [HttpPut("{id:guid}/signers/order")]
    [HasPermission(SignaturePermissions.RequestCreate)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ReorderSigners(
        [FromRoute] Guid id,
        [FromBody] ReorderSignersBody body,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(new ReorderSignersCommand(tenantId, id, body.OrderedSignerIds), ct);
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    // ---------- POST /signature/requests/{id}/fields ----------
    [HttpPost("{id:guid}/fields")]
    [HasPermission(SignaturePermissions.DocumentPrepare)]
    [ProducesResponseType<SignatureFieldResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> PlaceField(
        [FromRoute] Guid id,
        [FromBody] PlaceFieldBody body,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var cmd = new PlaceFieldCommand(
            tenantId,
            id,
            body.SignerId,
            body.Kind,
            body.Page,
            body.X,
            body.Y,
            body.Width,
            body.Height,
            body.Label,
            body.IsRequired
        );
        var result = await bus.InvokeAsync<Result<SignatureFieldResponse>>(cmd, ct);
        return result.IsSuccess
            ? Created($"/signature/requests/{id}/fields/{result.Value.Id}", result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    // ---------- DELETE /signature/requests/{id}/signers/{signerId}/fields/{fieldId} ----------
    [HttpDelete("{id:guid}/signers/{signerId:guid}/fields/{fieldId:guid}")]
    [HasPermission(SignaturePermissions.DocumentPrepare)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RemoveField(
        [FromRoute] Guid id,
        [FromRoute] Guid signerId,
        [FromRoute] Guid fieldId,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(new RemoveFieldCommand(tenantId, id, signerId, fieldId), ct);
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    // ---------- POST /signature/requests/{id}/send ----------
    [HttpPost("{id:guid}/send")]
    [HasPermission(SignaturePermissions.RequestCreate)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Send([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var forbidden = await CheckOwnershipAsync(tenantId, id, Operations.Send, ct);
        if (forbidden is not null)
            return forbidden;

        var result = await bus.InvokeAsync<Result>(new SendSignatureRequestCommand(tenantId, id), ct);
        return result.IsSuccess ? Accepted() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    // ---------- POST /signature/requests/{id}/cancel ----------
    [HttpPost("{id:guid}/cancel")]
    [HasPermission(SignaturePermissions.RequestCancel)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Cancel(
        [FromRoute] Guid id,
        [FromBody] CancelSignatureRequestBody body,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var forbidden = await CheckOwnershipAsync(tenantId, id, Operations.Cancel, ct);
        if (forbidden is not null)
            return forbidden;

        var result = await bus.InvokeAsync<Result>(
            new CancelSignatureRequestCommand(tenantId, id, userId, body.Reason),
            ct
        );
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    // ---------- POST /signature/requests/{id}/extend-expiration ----------
    [HttpPost("{id:guid}/extend-expiration")]
    [HasPermission(SignaturePermissions.RequestResend)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ExtendExpiration(
        [FromRoute] Guid id,
        [FromBody] ExtendExpirationBody body,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var forbidden = await CheckOwnershipAsync(tenantId, id, Operations.Update, ct);
        if (forbidden is not null)
            return forbidden;

        var result = await bus.InvokeAsync<Result>(
            new ExtendExpirationCommand(tenantId, id, userId, body.AdditionalHours),
            ct
        );
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    // ---------- POST /signature/requests/{id}/signers/{signerId}/resend ----------
    [HttpPost("{id:guid}/signers/{signerId:guid}/resend")]
    [HasPermission(SignaturePermissions.RequestResend)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResendSignerInvitation(
        [FromRoute] Guid id,
        [FromRoute] Guid signerId,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(new ResendSignerInvitationCommand(tenantId, id, signerId), ct);
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    // ---------- PUT /signature/requests/{id}/practitioner-pin ----------
    [HttpPut("{id:guid}/practitioner-pin")]
    [HasPermission(SignaturePermissions.RequestCreate)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SetPractitionerPin(
        [FromRoute] Guid id,
        [FromBody] SetPractitionerPinBody body,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(new SetPractitionerPinCommand(tenantId, id, userId, body.Pin), ct);
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    // ---------- DELETE /signature/requests/{id}/practitioner-pin ----------
    [HttpDelete("{id:guid}/practitioner-pin")]
    [HasPermission(SignaturePermissions.RequestCreate)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ClearPractitionerPin([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(new ClearPractitionerPinCommand(tenantId, id), ct);
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    // ---------- POST /signature/requests/{id}/legal-hold ----------
    [HttpPost("{id:guid}/legal-hold")]
    [HasPermission(SignaturePermissions.DocumentAuditRead)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> PlaceLegalHold(
        [FromRoute] Guid id,
        [FromBody] PlaceLegalHoldBody body,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(new PlaceLegalHoldCommand(tenantId, id, userId, body.Reason), ct);
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    // ---------- DELETE /signature/requests/{id}/legal-hold ----------
    [HttpDelete("{id:guid}/legal-hold")]
    [HasPermission(SignaturePermissions.DocumentAuditRead)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> LiftLegalHold([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(new LiftLegalHoldCommand(tenantId, id, userId), ct);
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    // ---------- PUT /signature/requests/{id}/preparer ----------
    [HttpPut("{id:guid}/preparer")]
    [HasPermission(SignaturePermissions.RequestCreate)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SetPreparer(
        [FromRoute] Guid id,
        [FromBody] SetPreparerBody body,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(
            new SetPreparerCommand(tenantId, id, body.PtinOrEfin, body.DisplayName, body.TitleLabel),
            ct
        );
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    // ---------- DELETE /signature/requests/{id}/preparer ----------
    [HttpDelete("{id:guid}/preparer")]
    [HasPermission(SignaturePermissions.RequestCreate)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ClearPreparer([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(new ClearPreparerCommand(tenantId, id), ct);
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    // ---------- POST /signature/requests/{id}/preparer/sign ----------
    [HttpPost("{id:guid}/preparer/sign")]
    [HasPermission(SignaturePermissions.DocumentSign)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SignAsPreparer([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var ua = Request.Headers.UserAgent.ToString();
        var result = await bus.InvokeAsync<Result>(
            new SignAsPreparerCommand(tenantId, id, userId, ip, string.IsNullOrWhiteSpace(ua) ? null : ua),
            ct
        );
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
