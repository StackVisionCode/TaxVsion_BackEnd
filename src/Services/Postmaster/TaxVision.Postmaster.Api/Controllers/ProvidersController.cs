using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Postmaster.Api.Authorization;
using TaxVision.Postmaster.Api.Common;
using TaxVision.Postmaster.Api.Requests;
using TaxVision.Postmaster.Application.Providers.Commands.DisableTenantEmailProvider;
using TaxVision.Postmaster.Application.Providers.Commands.UpsertSystemEmailProvider;
using TaxVision.Postmaster.Application.Providers.Commands.UpsertTenantEmailProvider;
using TaxVision.Postmaster.Application.Providers.Queries.GetProviderStatus;
using TaxVision.Postmaster.Application.Providers.Queries.GetTenantEmailProvider;
using Wolverine;

namespace TaxVision.Postmaster.Api.Controllers;

[ApiController]
[Route("postmaster")]
public sealed class ProvidersController(IMessageBus bus) : ControllerBase
{
    [HttpGet("providers/status")]
    [HasPermission(PostmasterPermissions.ProvidersRead)]
    public async Task<IActionResult> GetStatus([FromQuery] Guid? tenantId, CancellationToken ct)
    {
        if (!TryResolveTenantId(tenantId, out var resolvedTenantId))
            return Forbid();

        var status = await bus.InvokeAsync<ProviderStatusDto>(new GetProviderStatusQuery(resolvedTenantId), ct);
        return Ok(status);
    }

    [HttpGet("tenants/{tenantId:guid}/provider")]
    [HasPermission(PostmasterPermissions.ProvidersRead)]
    public async Task<IActionResult> GetProvider(Guid tenantId, [FromQuery] string providerCode, CancellationToken ct)
    {
        if (!TryResolveTenantId(tenantId, out var resolvedTenantId))
            return Forbid();

        var result = await bus.InvokeAsync<Result<TenantEmailProviderDto>>(
            new GetTenantEmailProviderQuery(resolvedTenantId, providerCode),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("tenants/{tenantId:guid}/provider")]
    [HasPermission(PostmasterPermissions.ProvidersWrite)]
    public Task<IActionResult> CreateProvider(
        Guid tenantId,
        [FromBody] UpsertTenantEmailProviderRequest body,
        CancellationToken ct
    ) => UpsertProvider(tenantId, body, ct);

    [HttpPut("tenants/{tenantId:guid}/provider")]
    [HasPermission(PostmasterPermissions.ProvidersWrite)]
    public Task<IActionResult> UpdateProvider(
        Guid tenantId,
        [FromBody] UpsertTenantEmailProviderRequest body,
        CancellationToken ct
    ) => UpsertProvider(tenantId, body, ct);

    [HttpDelete("tenants/{tenantId:guid}/provider")]
    [HasPermission(PostmasterPermissions.ProvidersWrite)]
    public async Task<IActionResult> DisableProvider(
        Guid tenantId,
        [FromQuery] string providerCode,
        CancellationToken ct
    )
    {
        if (!TryResolveTenantId(tenantId, out var resolvedTenantId))
            return Forbid();

        var result = await bus.InvokeAsync<Result>(
            new DisableTenantEmailProviderCommand(resolvedTenantId, providerCode),
            ct
        );
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>Configura el proveedor "default" de plataforma. Solo PlatformAdmin — nunca un tenant admin.</summary>
    [HttpPut("system/provider/{providerCode}")]
    [HasPermission(PostmasterPermissions.ProvidersWrite)]
    public async Task<IActionResult> UpsertSystemProvider(
        string providerCode,
        [FromBody] UpsertSystemEmailProviderRequest body,
        CancellationToken ct
    )
    {
        if (!User.IsInRole("PlatformAdmin"))
            return Forbid();

        var cmd = new UpsertSystemEmailProviderCommand(
            providerCode,
            body.DisplayName,
            body.ProviderType,
            body.FromAddressDefault,
            body.FromDisplayNameDefault,
            body.Host,
            body.Port,
            body.UseTls,
            body.Username,
            body.Password,
            body.RateLimitPerMinute
        );
        var result = await bus.InvokeAsync<Result>(cmd, ct);
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    private async Task<IActionResult> UpsertProvider(
        Guid tenantId,
        UpsertTenantEmailProviderRequest body,
        CancellationToken ct
    )
    {
        if (!TryResolveTenantId(tenantId, out var resolvedTenantId))
            return Forbid();

        if (!User.TryGetUserId(out var actingUserId))
            return Unauthorized();

        var cmd = new UpsertTenantEmailProviderCommand(
            resolvedTenantId,
            actingUserId,
            body.ProviderCode,
            body.DisplayName,
            body.ProviderType,
            body.FromAddressDefault,
            body.FromDisplayNameDefault,
            body.Host,
            body.Port,
            body.UseTls,
            body.Username,
            body.Password,
            body.RateLimitPerMinute
        );
        var result = await bus.InvokeAsync<Result>(cmd, ct);
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>PlatformAdmin puede operar sobre cualquier tenant; el resto solo sobre el propio (claim del JWT).</summary>
    private bool TryResolveTenantId(Guid? requestedTenantId, out Guid tenantId)
    {
        tenantId = Guid.Empty;
        if (!User.TryGetTenantId(out var tokenTenantId) && !User.IsInRole("PlatformAdmin"))
            return false;

        if (requestedTenantId is null)
        {
            tenantId = tokenTenantId;
            return tenantId != Guid.Empty;
        }

        if (User.IsInRole("PlatformAdmin") || requestedTenantId == tokenTenantId)
        {
            tenantId = requestedTenantId.Value;
            return true;
        }

        return false;
    }
}
