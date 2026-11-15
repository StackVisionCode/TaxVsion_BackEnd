using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.TimeZones;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Analytics;
using Wolverine;

namespace TaxVision.Signature.Api.Controllers;

/// <summary>
/// Endpoints de analytics/reporting del microservicio Signature. Los datos vienen del
/// snapshot diario poblado por consumers propios (event-sourced read model). TenantId
/// siempre del JWT; sin acceso cross-tenant.
///
/// <para>
/// Permiso: <c>SignaturePermissions.RequestRead</c> — los mismos usuarios que ven la
/// lista de solicitudes ven las agregaciones. Si a futuro se quiere separar, existe la
/// convención <c>SignaturePermissions.DocumentAuditRead</c>.
/// </para>
/// </summary>
[ApiController]
[Route("signature/analytics")]
[Authorize]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class SignatureAnalyticsController(IMessageBus bus, IOfficeTimeZone zones) : ControllerBase
{
    // ---------- GET /signature/analytics/summary ----------
    [HttpGet("summary")]
    [HasPermission(SignaturePermissions.RequestRead)]
    [RateLimit("signature.f.analytics_read")]
    [ProducesResponseType<SignatureAnalyticsSummary>(StatusCodes.Status200OK)]
    public async Task<ActionResult<SignatureAnalyticsSummary>> Summary(
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        CancellationToken ct = default
    )
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var (fromDay, toDay) = await ResolveDefaultRangeAsync(tenantId, from, to, ct);
        var result = await bus.InvokeAsync<SignatureAnalyticsSummary>(
            new SignatureAnalyticsSummaryQuery(tenantId, fromDay, toDay),
            ct
        );
        return Ok(result);
    }

    // ---------- GET /signature/analytics/timeline ----------
    [HttpGet("timeline")]
    [HasPermission(SignaturePermissions.RequestRead)]
    [RateLimit("signature.f.analytics_read")]
    [ProducesResponseType<SignatureAnalyticsTimeline>(StatusCodes.Status200OK)]
    public async Task<ActionResult<SignatureAnalyticsTimeline>> Timeline(
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        CancellationToken ct = default
    )
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var (fromDay, toDay) = await ResolveDefaultRangeAsync(tenantId, from, to, ct);
        var result = await bus.InvokeAsync<SignatureAnalyticsTimeline>(
            new SignatureAnalyticsTimelineQuery(tenantId, fromDay, toDay),
            ct
        );
        return Ok(result);
    }

    // ---------- GET /signature/analytics/by-category ----------
    [HttpGet("by-category")]
    [HasPermission(SignaturePermissions.RequestRead)]
    [RateLimit("signature.f.analytics_read")]
    [ProducesResponseType<SignatureAnalyticsByCategory>(StatusCodes.Status200OK)]
    public async Task<ActionResult<SignatureAnalyticsByCategory>> ByCategory(
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        CancellationToken ct = default
    )
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var (fromDay, toDay) = await ResolveDefaultRangeAsync(tenantId, from, to, ct);
        var result = await bus.InvokeAsync<SignatureAnalyticsByCategory>(
            new SignatureAnalyticsByCategoryQuery(tenantId, fromDay, toDay),
            ct
        );
        return Ok(result);
    }

    // ------------------------------------------------------------------
    // Métodos privados: cada uno una única responsabilidad
    // ------------------------------------------------------------------

    // P21: "hoy" es el día de la oficina, no el UTC.
    private async Task<(DateOnly From, DateOnly To)> ResolveDefaultRangeAsync(
        Guid tenantId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken ct
    )
    {
        var today = CivilDate.Of(DateTime.UtcNow, await zones.ResolveAsync(tenantId, ct));
        var effectiveTo = to ?? today;
        var effectiveFrom = from ?? effectiveTo.AddDays(-29); // 30 días por defecto
        return (effectiveFrom, effectiveTo);
    }
}
