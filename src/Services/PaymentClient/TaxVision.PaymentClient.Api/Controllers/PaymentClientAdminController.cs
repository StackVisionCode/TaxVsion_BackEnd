using System.Text;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.Csv;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.PaymentClient.Application.Admin.Queries;
using TaxVision.PaymentClient.Domain.TenantPayments;
using Wolverine;

namespace TaxVision.PaymentClient.Api.Controllers;

/// <summary>
/// Cross-tenant por diseño (§42.6) — a diferencia de <see cref="TenantPaymentsController"/>
/// (que resuelve el tenant del JWT), acá el tenant es un filtro OPCIONAL: sin
/// <c>tenantId</c> trae pagos de todos los tenants. Gateado por
/// <see cref="PaymentClientPermissions.AdminCrossTenant"/>, no por pertenencia a un tenant.
/// </summary>
[ApiController]
[Route("payments-client/admin")]
[Authorize]
[AllowActorTypes(ActorType.PlatformAdmin)]
public sealed class PaymentClientAdminController(IMessageBus bus) : ControllerBase
{
    [HttpGet("payments")]
    [HasPermission(PaymentClientPermissions.AdminCrossTenant)]
    [ProducesResponseType<IReadOnlyList<TenantPaymentAdminResponse>>(StatusCodes.Status200OK)]
    public Task<IActionResult> SearchAllTenants(
        [FromQuery] PaymentStatus? status,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int page,
        [FromQuery] int pageSize,
        CancellationToken ct
    ) => Search(tenantId: null, status, from, to, page, pageSize, ct);

    [HttpGet("tenants/{tenantId:guid}/payments")]
    [HasPermission(PaymentClientPermissions.AdminCrossTenant)]
    [ProducesResponseType<IReadOnlyList<TenantPaymentAdminResponse>>(StatusCodes.Status200OK)]
    public Task<IActionResult> SearchForTenant(
        Guid tenantId,
        [FromQuery] PaymentStatus? status,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int page,
        [FromQuery] int pageSize,
        CancellationToken ct
    ) => Search(tenantId, status, from, to, page, pageSize, ct);

    private const int ExportMaxRows = 5000;

    /// <summary>Mismos filtros que <see cref="SearchAllTenants"/> — un solo request, sin
    /// paginación (capado a <see cref="ExportMaxRows"/>; para volúmenes mayores el reporte
    /// debería moverse a un job async, fuera de scope de J.3).</summary>
    [HttpGet("payments/export")]
    [HasPermission(PaymentClientPermissions.AdminCrossTenant)]
    [Produces("text/csv")]
    public async Task<IActionResult> ExportCsv(
        [FromQuery] Guid? tenantId,
        [FromQuery] PaymentStatus? status,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken ct
    )
    {
        var result = await bus.InvokeAsync<Result<IReadOnlyList<TenantPaymentAdminResponse>>>(
            new SearchTenantPaymentsAdminQuery(tenantId, status, from, to, Page: 1, PageSize: ExportMaxRows),
            ct
        );

        if (result.IsFailure)
            return StatusCode(result.Error.ToHttpStatusCode(), result.Error);

        var csv = CsvWriter.Write(
            [
                "Id",
                "TenantId",
                "Status",
                "AmountCents",
                "Currency",
                "TaxpayerId",
                "PurposeKind",
                "ProviderCode",
                "ExternalChargeReference",
                "FailureCode",
                "PaidAtUtc",
                "CreatedAtUtc",
            ],
            result.Value.Select(p =>
                (IReadOnlyList<string?>)
                    [
                        p.Id.ToString(),
                        p.TenantId.ToString(),
                        p.Status,
                        p.AmountCents.ToString(),
                        p.Currency,
                        p.TaxpayerId?.ToString(),
                        p.PurposeKind,
                        p.ProviderCode,
                        p.ExternalChargeReference,
                        p.FailureCode,
                        p.PaidAtUtc?.ToString("O"),
                        p.CreatedAtUtc.ToString("O"),
                    ]
            )
        );

        return File(Encoding.UTF8.GetBytes(csv), "text/csv", $"tenant-payments-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv");
    }

    private async Task<IActionResult> Search(
        Guid? tenantId,
        PaymentStatus? status,
        DateTime? from,
        DateTime? to,
        int page,
        int pageSize,
        CancellationToken ct
    )
    {
        var result = await bus.InvokeAsync<Result<IReadOnlyList<TenantPaymentAdminResponse>>>(
            new SearchTenantPaymentsAdminQuery(
                tenantId,
                status,
                from,
                to,
                page <= 0 ? 1 : page,
                pageSize <= 0 ? 50 : pageSize
            ),
            ct
        );

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
