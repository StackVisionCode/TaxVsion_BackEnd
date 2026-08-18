using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Billing.Api.Authorization;
using TaxVision.Billing.Application.Invoices.IssuerProfile;
using Wolverine;

namespace TaxVision.Billing.Api.Controllers;

/// <summary>Datos de la empresa del tenant (emisor de las facturas). Se configuran una vez y Billing
/// los estampa en cada factura al crearla.</summary>
[ApiController]
[Route("billing/issuer-profile")]
[Authorize]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class IssuerProfileController(IMessageBus bus) : ControllerBase
{
    [HttpGet]
    [RateLimit("billing.f.issuer_profile_read")]
    [HasPermission(BillingPermissions.View)]
    [ProducesResponseType<IssuerProfileResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<IssuerProfileResponse>>(new GetIssuerProfileQuery(tenantId), ct);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    public sealed record UpsertIssuerProfileRequest(
        string Name,
        string? TaxId,
        string? Line1,
        string? City,
        string? State,
        string? Zip,
        string? Country,
        string? Phone,
        string? Email,
        string? Website
    );

    [HttpPut]
    [RateLimit("billing.g.issuer_profile_manage")]
    [HasPermission(BillingPermissions.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Upsert(UpsertIssuerProfileRequest request, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(
            new UpsertIssuerProfileCommand(
                tenantId,
                request.Name,
                request.TaxId,
                request.Line1,
                request.City,
                request.State,
                request.Zip,
                request.Country,
                request.Phone,
                request.Email,
                request.Website
            ),
            ct
        );

        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
