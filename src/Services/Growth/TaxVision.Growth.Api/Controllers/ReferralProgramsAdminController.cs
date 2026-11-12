using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Growth.Api.Common;
using TaxVision.Referrals.Application.Programs.ActivateTenantReferralProgram;
using TaxVision.Referrals.Application.Programs.Common;
using TaxVision.Referrals.Application.Programs.CreateTenantReferralProgram;
using TaxVision.Referrals.Application.Programs.SuspendTenantReferralProgram;
using TaxVision.Referrals.Application.Programs.UpdateTenantReferralProgramPolicy;
using Wolverine;
using ActorType = BuildingBlocks.ActorTypeAuthorization.ActorType;
using AllowActorTypesAttribute = BuildingBlocks.Web.ActorTypeAuthorization.AllowActorTypesAttribute;
using HasPermissionAttribute = BuildingBlocks.Web.ActorTypeAuthorization.HasPermissionAttribute;

namespace TaxVision.Growth.Api.Controllers;

/// <summary>
/// Administración del programa de referidos de plataforma (PlatformAdmin): crear, cambiar la política
/// (% del plan que gana el referidor, tope, espera, cupo anual), activar y suspender. Una oficina nunca
/// llega aquí: el programa es de la plataforma, no del tenant.
/// </summary>
[ApiController]
[Route("growth/referrals/programs")]
[Authorize]
[AllowActorTypes(ActorType.PlatformAdmin)]
public sealed class ReferralProgramsAdminController(IMessageBus bus) : ControllerBase
{
    public sealed record CreateProgramRequest(
        string ProgramCode,
        string Name,
        DateTime? StartsAtUtc,
        DateTime? EndsAtUtc,
        TenantReferralProgramPolicyInput? Policy
    );

    [HttpPost]
    [RateLimit("growth.g.referral_program_admin")]
    [HasPermission(GrowthPermissions.ReferralsProgramManage)]
    [ProducesResponseType<TenantReferralProgramResult>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create(
        CreateProgramRequest request,
        [FromHeader(Name = "Idempotency-Key")] string idempotencyKey,
        CancellationToken ct
    )
    {
        if (!User.TryGetUserId(out var actorId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<TenantReferralProgramResult>>(
            new CreateTenantReferralProgramCommand(
                request.ProgramCode,
                request.Name,
                request.StartsAtUtc ?? DateTime.UtcNow,
                request.EndsAtUtc,
                request.Policy,
                actorId,
                idempotencyKey
            ),
            ct
        );

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>Nueva versión de la política. Los campos omitidos conservan su valor actual; los premios ya
    /// solicitados conservan su snapshot y solo cambian los que se califiquen después.</summary>
    [HttpPut("{programId:guid}/policy")]
    [RateLimit("growth.g.referral_program_admin")]
    [HasPermission(GrowthPermissions.ReferralsProgramManage)]
    [ProducesResponseType<TenantReferralProgramResult>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdatePolicy(
        Guid programId,
        TenantReferralProgramPolicyInput request,
        [FromHeader(Name = "Idempotency-Key")] string idempotencyKey,
        CancellationToken ct
    )
    {
        if (!User.TryGetUserId(out var actorId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<TenantReferralProgramResult>>(
            new UpdateTenantReferralProgramPolicyCommand(programId, request, actorId, idempotencyKey),
            ct
        );

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("{programId:guid}/activate")]
    [RateLimit("growth.g.referral_program_admin")]
    [HasPermission(GrowthPermissions.ReferralsProgramManage)]
    [ProducesResponseType<TenantReferralProgramResult>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Activate(
        Guid programId,
        [FromHeader(Name = "Idempotency-Key")] string idempotencyKey,
        CancellationToken ct
    )
    {
        if (!User.TryGetUserId(out var actorId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<TenantReferralProgramResult>>(
            new ActivateTenantReferralProgramCommand(programId, actorId, idempotencyKey),
            ct
        );

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("{programId:guid}/suspend")]
    [RateLimit("growth.g.referral_program_admin")]
    [HasPermission(GrowthPermissions.ReferralsProgramManage)]
    [ProducesResponseType<TenantReferralProgramResult>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Suspend(
        Guid programId,
        [FromHeader(Name = "Idempotency-Key")] string idempotencyKey,
        CancellationToken ct
    )
    {
        if (!User.TryGetUserId(out var actorId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<TenantReferralProgramResult>>(
            new SuspendTenantReferralProgramCommand(programId, actorId, idempotencyKey),
            ct
        );

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
