using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Auth.Api.Common;
using TaxVision.Auth.Application.TenantDomains;
using TaxVision.Auth.Application.TenantDomains.Commands;
using TaxVision.Auth.Application.TenantDomains.Queries;
using TaxVision.Auth.Domain.Roles;
using Wolverine;

namespace TaxVision.Auth.Api.Controllers;

/// <summary>
/// Fase A5 — administración de dominios propios (custom hostnames) del tenant.
/// Los subdominios *.taxprocore.com no se gestionan aquí: se crean automáticamente
/// al nacer el tenant (Fase A3, TenantCreatedConsumer) y nunca requieren
/// provisioning en Cloudflare.
/// </summary>
[ApiController]
[Route("auth/tenant-domains")]
[HasPermission(PermissionCatalog.TenantDomainsManage)]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class TenantDomainsController(IMessageBus bus) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<TenantDomainResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<IReadOnlyList<TenantDomainResponse>>>(
            new GetTenantDomainsQuery(tenantId),
            ct
        );
        return Ok(result.Value);
    }

    public sealed record CreateTenantDomainRequest(string Hostname);

    /// <summary>Inicia el alta de un dominio propio: registra el intento y arranca el provisioning en Cloudflare.</summary>
    [HttpPost]
    [ProducesResponseType<TenantDomainCreatedResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateTenantDomainRequest request, CancellationToken ct)
    {
        if (!User.TryGetUserId(out var userId) || !User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<TenantDomainCreatedResponse>>(
            new CreateTenantDomainCommand(tenantId, userId, request.Hostname),
            ct
        );

        return result.IsSuccess
            ? Created($"/auth/tenant-domains/{result.Value.Domain.Id}", result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>Consulta (sin mutar) el estado de verificación DNS/TLS reportado por Cloudflare.</summary>
    [HttpPut("{domainId:guid}/verify")]
    [ProducesResponseType<TenantDomainVerificationResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Verify(Guid domainId, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<TenantDomainVerificationResponse>>(
            new RequestTenantDomainVerificationCommand(tenantId, domainId),
            ct
        );

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>Confirma con Cloudflare que el hostname está listo y lo pasa a Active.</summary>
    [HttpPut("{domainId:guid}/activate")]
    [ProducesResponseType<TenantDomainResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Activate(Guid domainId, CancellationToken ct)
    {
        if (!User.TryGetUserId(out var userId) || !User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<TenantDomainResponse>>(
            new ActivateTenantDomainCommand(tenantId, domainId, userId),
            ct
        );

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPut("{domainId:guid}/disable")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Disable(Guid domainId, CancellationToken ct)
    {
        if (!User.TryGetUserId(out var userId) || !User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(new DisableTenantDomainCommand(tenantId, domainId, userId), ct);

        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    public sealed record ChangeSubdomainRequest(string? NewSlug);

    /// <summary>Renombra el subdominio primario ya activo (Fase A7) — no aplica a custom hostnames.</summary>
    [HttpPut("{domainId:guid}/subdomain")]
    [ProducesResponseType<TenantDomainResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ChangeSubdomain(
        Guid domainId,
        ChangeSubdomainRequest request,
        CancellationToken ct
    )
    {
        if (!User.TryGetUserId(out var userId) || !User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<TenantDomainResponse>>(
            new ChangeSubdomainCommand(tenantId, domainId, request.NewSlug, userId),
            ct
        );

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
