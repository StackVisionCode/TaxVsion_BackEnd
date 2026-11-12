using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Growth.Api.Common;
using TaxVision.Referrals.Application.Abstractions;
using TaxVision.Referrals.Application.Admin;
using TaxVision.Referrals.Application.Codes.IssueTenantReferralCode;
using TaxVision.Referrals.Application.Codes.RegenerateTenantReferralCode;
using TaxVision.Referrals.Application.Codes.RevokeReferralCode;
using Wolverine;
using ActorType = BuildingBlocks.ActorTypeAuthorization.ActorType;
using AllowActorTypesAttribute = BuildingBlocks.Web.ActorTypeAuthorization.AllowActorTypesAttribute;
using HasPermissionAttribute = BuildingBlocks.Web.ActorTypeAuthorization.HasPermissionAttribute;

namespace TaxVision.Growth.Api.Controllers;

/// <summary>
/// Soporte de la plataforma sobre los códigos de referido de las oficinas (PlatformAdmin): listar, generar uno para una
/// oficina (el anterior queda revocado) y revocar sin reemplazo. Una oficina nunca llega aquí. Los comandos corren con el
/// tenant de la OFICINA dueña en contexto (<c>bus.TenantId</c>), como el resto de operaciones de plataforma sobre un
/// tenant; el tenant del llamador se restaura al terminar.
/// </summary>
[ApiController]
[Route("growth/referrals/admin/codes")]
[Authorize]
[AllowActorTypes(ActorType.PlatformAdmin)]
public sealed class ReferralCodesAdminController(IMessageBus bus, IReferralCodeTokenGenerator tokenGenerator)
    : ControllerBase
{
    public sealed record GenerateCodeRequest(Guid TenantId);

    public sealed record RevokeCodeRequest(Guid TenantId, string Reason);

    public sealed record GeneratedCodeResponse(
        Guid ReferralCodeId,
        Guid TenantId,
        string Status,
        string DisplayPrefix,
        string LastFour,
        DateTime ExpiresAtUtc,
        string ReferralCode
    )
    {
        public override string ToString() =>
            $"{nameof(GeneratedCodeResponse)} {{ ReferralCodeId = {ReferralCodeId}, TenantId = {TenantId}, ReferralCode = <redacted> }}";
    }

    [HttpGet]
    [RateLimit("growth.f.referral_read")]
    [HasPermission(GrowthPermissions.ReferralsAdminCodeManage)]
    [ProducesResponseType<PagedResult<ReferralCodeAdminRow>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        CancellationToken ct,
        [FromQuery] Guid? tenantId = null,
        [FromQuery] string? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int size = 20
    )
    {
        if (!this.TryGetTenantAndUser(out var callerTenant, out _) || callerTenant != PlatformTenant.Id)
            return Forbid();

        var result = await bus.InvokeAsync<Result<PagedResult<ReferralCodeAdminRow>>>(
            new ListReferralCodesAdminQuery(tenantId, status, page, size),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>Genera un código para una oficina (soporte). Si ya tenía uno activo, queda revocado como «Superseded».</summary>
    [HttpPost]
    [RateLimit("growth.g.referral_program_admin")]
    [HasPermission(GrowthPermissions.ReferralsAdminCodeManage)]
    [ProducesResponseType<GeneratedCodeResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Generate(
        GenerateCodeRequest request,
        [FromHeader(Name = "Idempotency-Key")] string idempotencyKey,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out var callerTenant, out var actorId) || callerTenant != PlatformTenant.Id)
            return Forbid();
        if (request.TenantId == Guid.Empty || request.TenantId == PlatformTenant.Id)
            return BadRequest(new Error("ReferralCode.InvalidTenant", "Choose the office to generate the code for."));

        var result = await InTenantAsync(
            request.TenantId,
            () =>
                bus.InvokeAsync<Result<IssueTenantReferralCodeResult>>(
                    new RegenerateTenantReferralCodeCommand(request.TenantId, actorId, idempotencyKey),
                    ct
                )
        );
        if (result.IsFailure)
            return StatusCode(result.Error.ToHttpStatusCode(), result.Error);

        var generated = tokenGenerator.Generate(result.Value.ProgramId, request.TenantId, idempotencyKey);
        if (generated.IsFailure)
            return StatusCode(generated.Error.ToHttpStatusCode(), generated.Error);

        var value = result.Value;
        return Ok(
            new GeneratedCodeResponse(
                value.ReferralCodeId,
                request.TenantId,
                value.Status.ToString(),
                value.DisplayPrefix,
                value.LastFour,
                value.ExpiresAtUtc,
                generated.Value.Reveal()
            )
        );
    }

    /// <summary>Revoca el código de una oficina sin reemplazo (p. ej. abuso). Los referidos ya atribuidos conservan su premio.</summary>
    [HttpPost("{codeId:guid}/revoke")]
    [RateLimit("growth.g.referral_program_admin")]
    [HasPermission(GrowthPermissions.ReferralsAdminCodeManage)]
    [ProducesResponseType<RevokeReferralCodeResult>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Revoke(Guid codeId, RevokeCodeRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var callerTenant, out var actorId) || callerTenant != PlatformTenant.Id)
            return Forbid();
        if (request.TenantId == Guid.Empty)
            return BadRequest(new Error("ReferralCode.InvalidTenant", "The office that owns the code is required."));

        var result = await InTenantAsync(
            request.TenantId,
            () =>
                bus.InvokeAsync<Result<RevokeReferralCodeResult>>(
                    new RevokeReferralCodeCommand(request.TenantId, codeId, request.Reason, actorId),
                    ct
                )
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    private async Task<T> InTenantAsync<T>(Guid tenantId, Func<Task<T>> action)
    {
        var caller = bus.TenantId;
        bus.TenantId = tenantId.ToString();
        try
        {
            return await action();
        }
        finally
        {
            bus.TenantId = caller;
        }
    }
}
