using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Signature.Application.Limits;
using TaxVision.Signature.Application.Platform;
using Wolverine;

namespace TaxVision.Signature.Api.Controllers;

/// <summary>
/// Límites vigentes para los frontends: el wizard de firma y la pantalla de ajustes leen de aquí cuántos
/// documentos y firmantes, qué tamaño, qué horas y qué canales (sin números propios).
/// </summary>
[ApiController]
[Route("signature/settings/effective-limits")]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class SignatureLimitsController(IMessageBus bus) : ControllerBase
{
    [HttpGet]
    [HasPermission(SignaturePermissions.RequestRead)]
    [RateLimit("signature.f.settings_read")]
    [ProducesResponseType(typeof(EffectiveSignatureLimitsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        return Ok(await bus.InvokeAsync<EffectiveSignatureLimitsResponse>(new GetEffectiveSignatureLimitsQuery(tenantId), ct));
    }
}

/// <summary>
/// Valores de plataforma de Signature (PlatformAdmin): código de verificación, imagen de firma y topes de
/// cantidad que no vende el plan. Cambian sin redeploy.
/// </summary>
[ApiController]
[Route("signature/admin/platform-settings")]
[AllowActorTypes(ActorType.PlatformAdmin)]
[HasPermission(SignaturePermissions.PlanConstraintsManage)]
public sealed class SignaturePlatformSettingsController(IMessageBus bus) : ControllerBase
{
    public sealed record UpdateSignaturePlatformSettingsBody(
        int OtpLength,
        int OtpLifetimeMinutes,
        int OtpResendCooldownSeconds,
        int MaxVerificationAttempts,
        int VerificationLockMinutes,
        int MaxSignatureImageBytes,
        int MaxProfilesPerPreparer,
        int MaxRolesPerTemplate,
        int MaxRemindersPerRequest,
        int MaxFieldsPerSigner,
        int AutoFontMinPt,
        int AutoFontMaxPt
    );

    [HttpGet]
    [RateLimit("signature.g.admin_constraints_manage")]
    [ProducesResponseType(typeof(SignaturePlatformSettingsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken ct) =>
        Ok(await bus.InvokeAsync<SignaturePlatformSettingsResponse>(new GetSignaturePlatformSettingsQuery(), ct));

    [HttpPut]
    [RateLimit("signature.g.admin_constraints_manage")]
    [ProducesResponseType(typeof(SignaturePlatformSettingsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Update([FromBody] UpdateSignaturePlatformSettingsBody body, CancellationToken ct)
    {
        if (!User.TryGetUserId(out var actorId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<SignaturePlatformSettingsResponse>>(
            new UpdateSignaturePlatformSettingsCommand(
                body.OtpLength,
                body.OtpLifetimeMinutes,
                body.OtpResendCooldownSeconds,
                body.MaxVerificationAttempts,
                body.VerificationLockMinutes,
                body.MaxSignatureImageBytes,
                body.MaxProfilesPerPreparer,
                body.MaxRolesPerTemplate,
                body.MaxRemindersPerRequest,
                body.MaxFieldsPerSigner,
                body.AutoFontMinPt,
                body.AutoFontMaxPt,
                actorId
            ),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
