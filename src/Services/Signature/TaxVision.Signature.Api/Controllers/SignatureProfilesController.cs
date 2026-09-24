using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Signature.Api.Requests;
using TaxVision.Signature.Application.Profiles;
using TaxVision.Signature.Application.Profiles.Commands.Archive;
using TaxVision.Signature.Application.Profiles.Commands.Create;
using TaxVision.Signature.Application.Profiles.Commands.Delete;
using TaxVision.Signature.Application.Profiles.Commands.Rename;
using TaxVision.Signature.Application.Profiles.Commands.SetDefault;
using TaxVision.Signature.Application.Profiles.EffectiveSignature;
using TaxVision.Signature.Application.Profiles.Queries.List;
using Wolverine;

namespace TaxVision.Signature.Api.Controllers;

/// <summary>
/// Firmas reutilizables del preparador/oficina (My Signature, plan F1). El listado combina las
/// firmas personales del usuario con las de oficina. Crear/gestionar la firma personal usa el permiso
/// de autoría (signature.request.create); la firma de oficina (scope=office) además exige ser admin.
/// La imagen vive en CloudStorage; aquí solo se maneja el metadato + el FileId.
/// </summary>
[ApiController]
[Route("signature/profiles")]
[Authorize]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class SignatureProfilesController(IMessageBus bus, IEffectiveSignatureResolver effectiveResolver)
    : ControllerBase
{
    // ---------- GET /signature/profiles/effective ----------
    // La firma que se estamparía por el usuario actual (su default si el tenant lo permite, o la de oficina).
    [HttpGet("effective")]
    [HasPermission(SignaturePermissions.RequestRead)]
    [RateLimit("signature.f.request_read")]
    [ProducesResponseType<SignatureProfileResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Effective(CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await effectiveResolver.ResolveAsync(tenantId, userId, ct);
        return result.IsSuccess
            ? Ok(SignatureProfileResponse.From(result.Value))
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    // ---------- GET /signature/profiles ----------
    [HttpGet]
    [HasPermission(SignaturePermissions.RequestRead)]
    [RateLimit("signature.f.request_read")]
    [ProducesResponseType<ListSignatureProfilesResult>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ListSignatureProfilesResult>> List(
        [FromQuery] bool includeArchived = false,
        CancellationToken ct = default
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<ListSignatureProfilesResult>(
            new ListSignatureProfilesQuery(tenantId, userId, IsAdmin(), includeArchived),
            ct
        );
        return Ok(result);
    }

    // ---------- POST /signature/profiles ----------
    [HttpPost]
    [HasPermission(SignaturePermissions.RequestCreate)]
    [RateLimit("signature.g.request_manage")]
    [ProducesResponseType<SignatureProfileResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateSignatureProfileBody body, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        if (!TryDecodeBase64(body.ImageBase64, out var content))
            return BadRequest(new Error("Signature.Profile.BadImage", "The signature image is not valid base64."));

        var isOffice = string.Equals(body.Scope, "office", StringComparison.OrdinalIgnoreCase);
        var result = await bus.InvokeAsync<Result<SignatureProfileResponse>>(
            new CreateSignatureProfileCommand(
                tenantId,
                userId,
                IsAdmin(),
                OwnerUserId: isOffice ? null : userId,
                body.Label,
                content
            ),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    // ---------- PUT /signature/profiles/{id} ----------
    [HttpPut("{id:guid}")]
    [HasPermission(SignaturePermissions.RequestCreate)]
    [RateLimit("signature.g.request_manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Rename(
        [FromRoute] Guid id,
        [FromBody] RenameSignatureProfileBody body,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(
            new RenameSignatureProfileCommand(tenantId, id, userId, IsAdmin(), body.Label),
            ct
        );
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    // ---------- POST /signature/profiles/{id}/default ----------
    [HttpPost("{id:guid}/default")]
    [HasPermission(SignaturePermissions.RequestCreate)]
    [RateLimit("signature.g.request_manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetDefault([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(
            new SetDefaultSignatureProfileCommand(tenantId, id, userId, IsAdmin()),
            ct
        );
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    // ---------- POST /signature/profiles/{id}/archive ----------
    [HttpPost("{id:guid}/archive")]
    [HasPermission(SignaturePermissions.RequestCreate)]
    [RateLimit("signature.g.request_manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public Task<IActionResult> Archive([FromRoute] Guid id, CancellationToken ct) => SetArchived(id, true, ct);

    // ---------- POST /signature/profiles/{id}/unarchive ----------
    [HttpPost("{id:guid}/unarchive")]
    [HasPermission(SignaturePermissions.RequestCreate)]
    [RateLimit("signature.g.request_manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public Task<IActionResult> Unarchive([FromRoute] Guid id, CancellationToken ct) => SetArchived(id, false, ct);

    // ---------- DELETE /signature/profiles/{id} ----------
    [HttpDelete("{id:guid}")]
    [HasPermission(SignaturePermissions.RequestCreate)]
    [RateLimit("signature.g.request_manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(
            new DeleteSignatureProfileCommand(tenantId, id, userId, IsAdmin()),
            ct
        );
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    private async Task<IActionResult> SetArchived(Guid id, bool archived, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(
            new SetSignatureProfileArchivedCommand(tenantId, id, userId, IsAdmin(), archived),
            ct
        );
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    // Admin del tenant o de plataforma: única identidad que puede gestionar la firma de oficina.
    private bool IsAdmin()
    {
        var actorType = User.GetActorType();
        return actorType is ActorType.TenantAdmin or ActorType.PlatformAdmin;
    }

    private static bool TryDecodeBase64(string? value, out byte[] content)
    {
        content = [];
        if (string.IsNullOrWhiteSpace(value))
            return false;
        try
        {
            content = Convert.FromBase64String(value);
            return content.Length > 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
