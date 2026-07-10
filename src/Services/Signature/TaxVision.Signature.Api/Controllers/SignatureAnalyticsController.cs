using BuildingBlocks.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Signature.Api.Authorization;
using TaxVision.Signature.Api.Common;
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
public sealed class SignatureAnalyticsController(IMessageBus bus) : ControllerBase
{
    // ---------- GET /signature/analytics/summary ----------
    [HttpGet("summary")]
    [HasPermission(SignaturePermissions.RequestRead)]
    [ProducesResponseType<SignatureAnalyticsSummary>(StatusCodes.Status200OK)]
    public async Task<ActionResult<SignatureAnalyticsSummary>> Summary(
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        CancellationToken ct = default
    )
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var (fromDay, toDay) = ResolveDefaultRange(from, to);
        var result = await bus.InvokeAsync<SignatureAnalyticsSummary>(
            new SignatureAnalyticsSummaryQuery(tenantId, fromDay, toDay),
            ct
        );
        return Ok(result);
    }

    // ---------- GET /signature/analytics/timeline ----------
    [HttpGet("timeline")]
    [HasPermission(SignaturePermissions.RequestRead)]
    [ProducesResponseType<SignatureAnalyticsTimeline>(StatusCodes.Status200OK)]
    public async Task<ActionResult<SignatureAnalyticsTimeline>> Timeline(
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        CancellationToken ct = default
    )
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var (fromDay, toDay) = ResolveDefaultRange(from, to);
        var result = await bus.InvokeAsync<SignatureAnalyticsTimeline>(
            new SignatureAnalyticsTimelineQuery(tenantId, fromDay, toDay),
            ct
        );
        return Ok(result);
    }

    // ---------- GET /signature/analytics/by-category ----------
    [HttpGet("by-category")]
    [HasPermission(SignaturePermissions.RequestRead)]
    [ProducesResponseType<SignatureAnalyticsByCategory>(StatusCodes.Status200OK)]
    public async Task<ActionResult<SignatureAnalyticsByCategory>> ByCategory(
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        CancellationToken ct = default
    )
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var (fromDay, toDay) = ResolveDefaultRange(from, to);
        var result = await bus.InvokeAsync<SignatureAnalyticsByCategory>(
            new SignatureAnalyticsByCategoryQuery(tenantId, fromDay, toDay),
            ct
        );
        return Ok(result);
    }

    // ------------------------------------------------------------------
    // Métodos privados: cada uno una única responsabilidad
    // ------------------------------------------------------------------

    private static (DateOnly From, DateOnly To) ResolveDefaultRange(DateOnly? from, DateOnly? to)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var effectiveTo = to ?? today;
        var effectiveFrom = from ?? effectiveTo.AddDays(-29); // 30 días por defecto
        return (effectiveFrom, effectiveTo);
    }
}
