using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Payment.Application.SaaSPayments.Queries;
using Wolverine;

namespace TaxVision.Payment.Api.Controllers;

/// <summary>
/// Read-only API for SaaS platform payments (TaxVision → tenant charges via Stripe).
/// Restricted to <c>PlatformAdmin</c>; tenant admins cannot access each other's billing records.
/// </summary>
[ApiController]
[Route("payments/saas")]
public sealed class SaaSPaymentsController(IMessageBus bus) : ControllerBase
{
    /// <summary>Returns a single SaaS payment by ID. Returns 404 if not found.</summary>
    [HttpGet("{id:guid}")]
    [Authorize(Roles = "PlatformAdmin")]
    [ProducesResponseType<SaaSPaymentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<SaaSPaymentDto>>(new GetSaaSPaymentQuery(id), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet]
    [Authorize(Roles = "PlatformAdmin")]
    [ProducesResponseType<List<SaaSPaymentDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetByTenant(CancellationToken ct)
    {
        if (!TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<List<SaaSPaymentDto>>>(
            new GetSaaSPaymentsByTenantQuery(tenantId), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    private bool TryGetTenantId(out Guid tenantId)
    {
        var raw = User.FindFirst("tenant_id")?.Value ?? User.FindFirst("tenantId")?.Value;
        return Guid.TryParse(raw, out tenantId);
    }
}
