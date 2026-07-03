using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Payment.Application.TenantPayments.Commands;
using TaxVision.Payment.Application.TenantPayments.Queries;
using Wolverine;

namespace TaxVision.Payment.Api.Controllers;

/// <summary>
/// API for tenant-side payments: configuring a payment provider and charging the tenant's own customers.
/// Restricted to <c>TenantAdmin</c>. All operations are scoped to the calling tenant's context.
/// <para>
/// Provider credentials are stored AES-encrypted. Secret keys are accepted in requests but never
/// returned in responses — <see cref="TenantPaymentConfigDto"/> exposes only the public key.
/// </para>
/// </summary>
[ApiController]
[Route("payments/tenant")]
public sealed class TenantPaymentsController(IMessageBus bus) : ControllerBase
{
    /// <summary>Returns the current payment provider configuration for the calling tenant. Returns 404 if not configured.</summary>
    [HttpGet("config")]
    [Authorize(Roles = "TenantAdmin")]
    [ProducesResponseType<TenantPaymentConfigDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetConfig(CancellationToken ct)
    {
        if (!TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<TenantPaymentConfigDto>>(
            new GetTenantPaymentConfigQuery(tenantId), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("config")]
    [Authorize(Roles = "TenantAdmin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Configure(ConfigureTenantProviderCommand command, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result>(command, ct);
        return result.IsSuccess
            ? NoContent()
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("charge")]
    [Authorize(Roles = "TenantAdmin")]
    [ProducesResponseType<ProcessTenantPaymentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Charge(ProcessTenantPaymentCommand command, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<ProcessTenantPaymentResponse>>(command, ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet("transactions")]
    [Authorize(Roles = "TenantAdmin")]
    [ProducesResponseType<List<TenantTransactionDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTransactions(
        [FromQuery] int page = 1,
        [FromQuery] int size = 20,
        CancellationToken ct = default)
    {
        if (!TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<List<TenantTransactionDto>>>(
            new GetTenantTransactionsQuery(tenantId, page, size), ct);
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
