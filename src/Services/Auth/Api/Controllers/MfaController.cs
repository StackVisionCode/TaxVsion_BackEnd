using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Auth.Api.Authorization;
using TaxVision.Auth.Api.Common;
using TaxVision.Auth.Application.Mfa.Commands;
using TaxVision.Auth.Application.Mfa.Queries;
using TaxVision.Auth.Application.Users.Commands;
using TaxVision.Auth.Domain.Roles;
using Wolverine;

namespace TaxVision.Auth.Api.Controllers;

[ApiController]
[Route("auth/mfa")]
public sealed class MfaController(IMessageBus bus) : ControllerBase
{
    /// <summary>Paso 2 del login: verifica el código del desafío MFA.</summary>
    [HttpPost("verify")]
    [AllowAnonymous]
    [ProducesResponseType<AuthTokensResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Verify(VerifyMfaChallengeCommand command, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<AuthTokensResponse>>(command, ct);

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("totp/setup")]
    [Authorize]
    [ProducesResponseType<SetupTotpResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SetupTotp(CancellationToken ct)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<SetupTotpResponse>>(new SetupTotpCommand(userId), ct);

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    public sealed record ConfirmTotpRequest(string Code);

    [HttpPost("totp/confirm")]
    [Authorize]
    [ProducesResponseType<ConfirmTotpResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ConfirmTotp(ConfirmTotpRequest request, CancellationToken ct)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<ConfirmTotpResponse>>(
            new ConfirmTotpCommand(userId, request.Code),
            ct
        );

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    public sealed record DisableMfaRequest(string Password);

    [HttpPost("disable")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Disable(DisableMfaRequest request, CancellationToken ct)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(new DisableMfaCommand(userId, request.Password), ct);

        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    public sealed record RegenerateRecoveryCodesRequest(string Password);

    [HttpPost("recovery-codes/regenerate")]
    [Authorize]
    [ProducesResponseType<RegenerateRecoveryCodesResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RegenerateRecoveryCodes(
        RegenerateRecoveryCodesRequest request,
        CancellationToken ct
    )
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<RegenerateRecoveryCodesResponse>>(
            new RegenerateRecoveryCodesCommand(userId, request.Password),
            ct
        );

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpDelete("trusted-devices/{deviceId:guid}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RevokeTrustedDevice(Guid deviceId, CancellationToken ct)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(new RevokeTrustedDeviceCommand(userId, deviceId), ct);

        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet("status")]
    [Authorize]
    [ProducesResponseType<MfaStatusResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Status(CancellationToken ct)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<MfaStatusResponse>>(new GetMyMfaStatusQuery(userId), ct);

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet("policy")]
    [HasPermission(PermissionCatalog.SettingsManage)]
    [ProducesResponseType<TenantMfaPolicyResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPolicy(CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<TenantMfaPolicyResponse>>(new GetTenantMfaPolicyQuery(tenantId), ct);

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    public sealed record UpdateMfaPolicyRequest(
        bool RequireForEmployees,
        bool RequireForCustomerPortal,
        int TrustedDeviceDays
    );

    [HttpPut("policy")]
    [HasPermission(PermissionCatalog.SettingsManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> UpdatePolicy(UpdateMfaPolicyRequest request, CancellationToken ct)
    {
        if (!User.TryGetUserId(out var userId) || !User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(
            new UpdateTenantMfaPolicyCommand(
                tenantId,
                userId,
                request.RequireForEmployees,
                request.RequireForCustomerPortal,
                request.TrustedDeviceDays
            ),
            ct
        );

        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
