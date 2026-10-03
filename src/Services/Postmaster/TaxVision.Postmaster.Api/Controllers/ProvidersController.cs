using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Postmaster.Api.Requests;
using TaxVision.Postmaster.Application.Providers.Commands.UpsertSystemEmailProvider;
using TaxVision.Postmaster.Application.Providers.Queries.GetProviderStatus;
using Wolverine;

namespace TaxVision.Postmaster.Api.Controllers;

[ApiController]
[Route("postmaster")]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class ProvidersController(IMessageBus bus) : ControllerBase
{
    [HttpGet("providers/status")]
    [HasPermission(PostmasterPermissions.ProvidersRead)]
    [RateLimit("postmaster.f.providers_read")]
    public async Task<IActionResult> GetStatus([FromQuery] Guid? tenantId, CancellationToken ct)
    {
        if (!this.TryResolveTenantId(tenantId, out var resolvedTenantId))
            return Forbid();

        var status = await bus.InvokeAsync<ProviderStatusDto>(new GetProviderStatusQuery(resolvedTenantId), ct);
        return Ok(status);
    }

    /// <summary>Configura el proveedor "default" de plataforma. Solo PlatformAdmin — nunca un tenant admin.</summary>
    [HttpPut("system/provider/{providerCode}")]
    [HasPermission(PostmasterPermissions.ProvidersWrite)]
    [AllowActorTypes(ActorType.PlatformAdmin)]
    [RateLimit("postmaster.g.providers_manage")]
    public async Task<IActionResult> UpsertSystemProvider(
        string providerCode,
        [FromBody] UpsertSystemEmailProviderRequest body,
        CancellationToken ct
    )
    {
        // RBAC Fase 2: chequeo defensivo redundante retirado — [AllowActorTypes(ActorType.PlatformAdmin)]
        // de esta acción ya bloquea a cualquier no-PlatformAdmin antes de llegar acá (Layer 2).

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
            body.RateLimitPerMinute,
            body.BulkRateLimitPerMinute
        );
        var result = await bus.InvokeAsync<Result>(cmd, ct);
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
