using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Documents.Application.Branding;
using Wolverine;

namespace TaxVision.Documents.Api.Controllers;

/// <summary>
/// Perfil de marca del tenant (uno por tenant): el tenant lo configura una vez y se aplica a todas sus
/// facturas sin mandarlo en cada request. El tenant sale del JWT.
///
/// Es configuración administrativa del tenant, por eso va detrás de un permiso HUMANO
/// (<see cref="DocumentsPermissions.BrandingManage"/>) — actor-type de tenant (no M2M) + el permiso,
/// enforzado contra la proyección local de permisos (RBAC Fase 7). Los servicios M2M no configuran
/// branding.
/// </summary>
[ApiController]
[Route("internal/document-branding")]
// También expuesto en /documents/branding para que el frontend del tenant lo configure vía el gateway
// (el gateway rutea /documents/* → documents-api). Mismo actor-type + permiso humano.
[Route("documents/branding")]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class InternalDocumentBrandingController(IMessageBus bus) : ControllerBase
{
    public sealed record UpsertBrandingRequest(
        string? DisplayName,
        string? LogoDataUri,
        string? BrandColorHex,
        string? FooterText
    );

    [HttpGet]
    [RateLimit("documents.f.branding_read")]
    [HasPermission(DocumentsPermissions.BrandingManage)]
    [ProducesResponseType<DocumentBrandingDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<DocumentBrandingDto?>>(new GetDocumentBrandingQuery(tenantId), ct);
        if (result.IsFailure)
            return StatusCode(result.Error.ToHttpStatusCode(), result.Error);

        return result.Value is null ? NotFound() : Ok(result.Value);
    }

    [HttpPut]
    [RateLimit("documents.g.branding_upsert")]
    [HasPermission(DocumentsPermissions.BrandingManage)]
    [ProducesResponseType<DocumentBrandingDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Upsert(UpsertBrandingRequest request, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<DocumentBrandingDto>>(
            new UpsertDocumentBrandingCommand(
                tenantId,
                request.DisplayName,
                request.LogoDataUri,
                request.BrandColorHex,
                request.FooterText
            ),
            ct
        );

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
