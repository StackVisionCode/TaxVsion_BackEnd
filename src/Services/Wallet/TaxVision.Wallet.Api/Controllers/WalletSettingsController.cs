using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Wallet.Application.Settings;
using TaxVision.Wallet.Application.Settings.Commands;
using TaxVision.Wallet.Application.Settings.Queries;
using TaxVision.Wallet.Domain.Settings;
using Wolverine;

namespace TaxVision.Wallet.Api.Controllers;

/// <summary>
/// Ajustes del monedero de la oficina: umbral de saldo bajo y recarga automática con consentimiento expreso (D4).
/// Leer pide <c>wallet.view</c>; cambiar, <c>wallet.manage</c>. TenantId y usuario siempre del JWT.
/// </summary>
[ApiController]
[Route("wallet/settings")]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class WalletSettingsController(IMessageBus bus) : ControllerBase
{
    private const int DefaultSize = 20;

    public sealed record LowBalanceRequest(long ThresholdMicros);

    public sealed record AutoRechargeRequest(
        long AmountCents,
        long ThresholdMicros,
        string? Provider,
        string? MethodReference,
        string? MethodLabel,
        bool ConsentAccepted,
        string? ConsentTextVersion
    );

    [HttpGet]
    [HasPermission(WalletPermissions.View)]
    [RateLimit("wallet.f.get")]
    [ProducesResponseType<WalletSettingsView>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();
        return Ok(await bus.InvokeAsync<WalletSettingsView>(new GetWalletSettingsQuery(tenantId), ct));
    }

    /// <summary>Umbral del aviso de saldo bajo, en micros (0 a $1,000; 0 = sin aviso).</summary>
    [HttpPut("low-balance")]
    [HasPermission(WalletPermissions.Manage)]
    [RateLimit("wallet.g.topup")]
    [ProducesResponseType<WalletSettingsView>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ChangeLowBalance(LowBalanceRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();
        var command = new ChangeLowBalanceThresholdCommand(tenantId, userId, request.ThresholdMicros);
        return ToResponse(await bus.InvokeAsync<Result<WalletSettingsView>>(command, ct));
    }

    /// <summary>
    /// Enciende o cambia la recarga automática. Exige aceptar la versión vigente del texto (versión vieja = 409); el
    /// servidor guarda quién, cuándo, la IP, el texto final, el monto, el umbral y el método.
    /// </summary>
    [HttpPut("auto-recharge")]
    [HasPermission(WalletPermissions.Manage)]
    [RateLimit("wallet.g.topup")]
    [ProducesResponseType<WalletSettingsView>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> EnableAutoRecharge(AutoRechargeRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();
        var command = new EnableAutoRechargeCommand(
            tenantId,
            userId,
            request.AmountCents,
            request.ThresholdMicros,
            request.Provider,
            request.MethodReference,
            request.MethodLabel,
            request.ConsentAccepted,
            request.ConsentTextVersion,
            // IP real del cliente (UseTaxVisionClientIp va primero en el pipeline).
            HttpContext.Connection.RemoteIpAddress?.ToString()
        );
        return ToResponse(await bus.InvokeAsync<Result<WalletSettingsView>>(command, ct));
    }

    /// <summary>Apaga la recarga automática (queda en la historia).</summary>
    [HttpDelete("auto-recharge")]
    [HasPermission(WalletPermissions.Manage)]
    [RateLimit("wallet.g.topup")]
    [ProducesResponseType<WalletSettingsView>(StatusCodes.Status200OK)]
    public async Task<IActionResult> DisableAutoRecharge(CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();
        var command = new DisableAutoRechargeCommand(tenantId, userId);
        return ToResponse(await bus.InvokeAsync<Result<WalletSettingsView>>(command, ct));
    }

    /// <summary>Historia de cambios de los ajustes, más reciente primero.</summary>
    [HttpGet("history")]
    [HasPermission(WalletPermissions.View)]
    [RateLimit("wallet.f.list")]
    [ProducesResponseType<IReadOnlyList<WalletSettingsChangeView>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> History([FromQuery] int page, [FromQuery] int size, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();
        var query = new ListWalletSettingsHistoryQuery(
            tenantId,
            page < 1 ? 1 : page,
            size is < 1 or > 100 ? DefaultSize : size
        );
        return Ok(await bus.InvokeAsync<IReadOnlyList<WalletSettingsChangeView>>(query, ct));
    }

    private IActionResult ToResponse(Result<WalletSettingsView> result)
    {
        if (result.IsSuccess)
            return Ok(result.Value);
        return result.Error == WalletSettingsErrors.ConsentVersionConflict
            ? Conflict(result.Error)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
