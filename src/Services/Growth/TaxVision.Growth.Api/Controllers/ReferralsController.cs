using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.Results;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Growth.Api.Common;
using TaxVision.Referrals.Application.Abstractions;
using TaxVision.Referrals.Application.Attributions.CreateReferralAttribution;
using TaxVision.Referrals.Application.Codes.GetMyReferralCode;
using TaxVision.Referrals.Application.Codes.IssueTenantReferralCode;
using TaxVision.Referrals.Application.Codes.RecordReferralCodeShare;
using TaxVision.Referrals.Application.Codes.RegenerateTenantReferralCode;
using TaxVision.Referrals.Application.Programs.GetActiveReferralProgram;
using TaxVision.Referrals.Application.Queries;
using TaxVision.Referrals.Domain.Participants;
using TaxVision.Referrals.Domain.Shares;
using Wolverine;
using ActorType = BuildingBlocks.ActorTypeAuthorization.ActorType;
using AllowActorTypesAttribute = BuildingBlocks.Web.ActorTypeAuthorization.AllowActorTypesAttribute;
// Alias puntual — ver mismo comentario en CodesController.cs.
using HasPermissionAttribute = BuildingBlocks.Web.ActorTypeAuthorization.HasPermissionAttribute;

namespace TaxVision.Growth.Api.Controllers;

/// <summary>
/// Referidos de la oficina (tenant-to-tenant). La identidad del tenant y del usuario sale siempre del JWT validado,
/// nunca del cuerpo. Reglas de producto:
/// <list type="bullet">
/// <item>Solo se beneficia el REFERIDOR (premio en % del plan, a su Wallet); el referido no recibe nada.</item>
/// <item>El owner del tenant (<c>referrals.code.manage</c>) genera/regenera su código; al hacerlo el anterior queda
/// revocado. El resto de usuarios solo lo ve, lo copia y lo comparte (<c>referrals.own.read</c>) — y cada envío
/// registra su canal.</item>
/// </list>
/// Taxpayer-to-taxpayer sigue deliberadamente no disponible — solo staff, nunca CustomerPortal.
/// </summary>
[ApiController]
[Route("growth/referrals")]
[Authorize]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class ReferralsController(IMessageBus bus, IReferralCodeTokenGenerator tokenGenerator) : ControllerBase
{
    public sealed record CreateAttributionRequest(Guid ProgramId, string ReferralCode)
    {
        public override string ToString() =>
            $"{nameof(CreateAttributionRequest)} {{ ProgramId = {ProgramId}, ReferralCode = <redacted> }}";
    }

    public sealed record CreateAttributionResponse(Guid AttributionId, string Status, bool WasReplay);

    /// <summary>
    /// El token en claro se calcula aquí, en el borde de entrega, y nunca entra al resultado persistido por idempotencia:
    /// el generador es determinista sobre (ProgramId, TenantId, IdempotencyKey del código), así que se puede volver a
    /// derivar en cada lectura autenticada para copiarlo, sin guardar secretos.
    /// </summary>
    public sealed record ReferralCodeResponse(
        Guid ReferralCodeId,
        Guid ProgramId,
        string Status,
        string DisplayPrefix,
        string LastFour,
        DateTime ExpiresAtUtc,
        string ReferralCode
    )
    {
        public override string ToString() =>
            $"{nameof(ReferralCodeResponse)} {{ ReferralCodeId = {ReferralCodeId}, ProgramId = {ProgramId}, "
            + $"Status = {Status}, DisplayPrefix = {DisplayPrefix}, LastFour = {LastFour}, "
            + $"ExpiresAtUtc = {ExpiresAtUtc}, ReferralCode = <redacted> }}";
    }

    public sealed record ShareRequest(ReferralShareChannel Channel, string? RecipientHint);

    [HttpPost("attributions")]
    [RateLimit("growth.h.referral_attribution_create")]
    [HasPermission(GrowthPermissions.ReferralsOwnRead)]
    [ProducesResponseType<CreateAttributionResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateAttribution(
        CreateAttributionRequest request,
        [FromHeader(Name = "Idempotency-Key")] string idempotencyKey,
        CancellationToken ct
    )
    {
        if (!User.TryGetTenantId(out var tenantId) || !User.TryGetUserId(out var actorId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<CreateReferralAttributionResult>>(
            new CreateReferralAttributionCommand(
                tenantId,
                request.ProgramId,
                request.ReferralCode,
                ReferralParticipantType.Tenant,
                tenantId,
                idempotencyKey,
                actorId
            ),
            ct
        );

        return result.IsSuccess
            ? Ok(
                new CreateAttributionResponse(
                    result.Value.AttributionId,
                    result.Value.Status.ToString(),
                    result.Value.WasReplay
                )
            )
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>Términos del programa vigente (premio en % del plan, tope, espera, cupo) para mostrarlos a
    /// la oficina. No recibe ningún Id: el programa vigente es el único activo de plataforma.</summary>
    [HttpGet("programs/active")]
    [RateLimit("growth.f.referral_read")]
    [HasPermission(GrowthPermissions.ReferralsProgramRead)]
    [ProducesResponseType<ReferralProgramTermsResult>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetActiveProgram(CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<ReferralProgramTermsResult>>(new GetActiveReferralProgramQuery(), ct);

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>El código ACTIVO de la oficina, para verlo, copiarlo y compartirlo. Cualquier usuario con acceso de
    /// lectura puede leerlo; no puede modificarlo.</summary>
    [HttpGet("codes/mine")]
    [RateLimit("growth.f.referral_read")]
    [HasPermission(GrowthPermissions.ReferralsOwnRead)]
    [ProducesResponseType<ReferralCodeResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyCode(CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<MyReferralCodeResult>>(new GetMyReferralCodeQuery(tenantId), ct);
        if (result.IsFailure)
            return StatusCode(result.Error.ToHttpStatusCode(), result.Error);

        var value = result.Value;
        var generated = tokenGenerator.Generate(value.ProgramId, tenantId, value.IdempotencyKey);
        if (generated.IsFailure)
            return StatusCode(generated.Error.ToHttpStatusCode(), generated.Error);

        return Ok(
            new ReferralCodeResponse(
                value.ReferralCodeId,
                value.ProgramId,
                value.Status.ToString(),
                value.DisplayPrefix,
                value.LastFour,
                value.ExpiresAtUtc,
                generated.Value.Reveal()
            )
        );
    }

    /// <summary>
    /// Solo el owner del tenant: genera su código de referido. Si ya tenía uno activo, queda revocado y no sirve para
    /// nada más (los referidos ya atribuidos conservan su premio). Cada generación lleva su propio Idempotency-Key.
    /// </summary>
    [HttpPost("codes/regenerate")]
    [RateLimit("growth.g.referral_code_issue")]
    [HasPermission(GrowthPermissions.ReferralsCodeManage)]
    [ProducesResponseType<ReferralCodeResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RegenerateCode(
        [FromHeader(Name = "Idempotency-Key")] string idempotencyKey,
        CancellationToken ct
    )
    {
        if (!User.TryGetTenantId(out var tenantId) || !User.TryGetUserId(out var actorId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<IssueTenantReferralCodeResult>>(
            new RegenerateTenantReferralCodeCommand(tenantId, actorId, idempotencyKey),
            ct
        );
        if (result.IsFailure)
            return StatusCode(result.Error.ToHttpStatusCode(), result.Error);

        var generated = tokenGenerator.Generate(result.Value.ProgramId, tenantId, idempotencyKey);
        if (generated.IsFailure)
            return StatusCode(generated.Error.ToHttpStatusCode(), generated.Error);

        var value = result.Value;
        return Ok(
            new ReferralCodeResponse(
                value.ReferralCodeId,
                value.ProgramId,
                value.Status.ToString(),
                value.DisplayPrefix,
                value.LastFour,
                value.ExpiresAtUtc,
                generated.Value.Reveal()
            )
        );
    }

    /// <summary>Invitaciones de la oficina (paginadas) con canal de llegada y estado del premio.</summary>
    [HttpGet("invites")]
    [RateLimit("growth.f.referral_read")]
    [HasPermission(GrowthPermissions.ReferralsRewardRead)]
    [ProducesResponseType<PagedResult<ReferralInviteRow>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListInvites(
        CancellationToken ct,
        [FromQuery] int page = 1,
        [FromQuery] int size = 20
    )
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<PagedResult<ReferralInviteRow>>>(
            new ListReferralInvitesQuery(tenantId, page, size),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>Historial de envíos del código (canal, quién, cuántos registros y pagos produjo).</summary>
    [HttpGet("shares")]
    [RateLimit("growth.f.referral_read")]
    [HasPermission(GrowthPermissions.ReferralsRewardRead)]
    [ProducesResponseType<PagedResult<ReferralShareRow>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListShares(
        CancellationToken ct,
        [FromQuery] int page = 1,
        [FromQuery] int size = 20
    )
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<PagedResult<ReferralShareRow>>>(
            new ListReferralSharesQuery(tenantId, page, size),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>Totales: ganado (acreditado al Wallet), pendiente, registrados y acreditados.</summary>
    [HttpGet("rewards")]
    [RateLimit("growth.f.referral_read")]
    [HasPermission(GrowthPermissions.ReferralsRewardRead)]
    [ProducesResponseType<ReferralRewardSummary>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRewards(CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<ReferralRewardSummary>>(
            new GetReferralRewardSummaryQuery(tenantId),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>
    /// Registra que alguien de la oficina copió o compartió el código por un canal. Compartir NO modifica el código,
    /// por eso basta el acceso de lectura. Devuelve el Id del envío, que viaja en el enlace (<c>sid</c>) para que la
    /// atribución del referido quede con el canal real.
    /// </summary>
    [HttpPost("codes/{codeId:guid}/shares")]
    [RateLimit("growth.g.referral_code_issue")]
    [HasPermission(GrowthPermissions.ReferralsOwnRead)]
    [ProducesResponseType<ReferralCodeShareResult>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RecordShare(Guid codeId, ShareRequest request, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId) || !User.TryGetUserId(out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<ReferralCodeShareResult>>(
            new RecordReferralCodeShareCommand(tenantId, codeId, request.Channel, userId, request.RecipientHint),
            ct
        );

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
