using System.Text;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Csv;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.PaymentApp.Application.Admin.Commands;
using TaxVision.PaymentApp.Application.Admin.Queries;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using Wolverine;

namespace TaxVision.PaymentApp.Api.Controllers;

/// <summary>
/// Cross-tenant por diseño (§42.6) — a diferencia de <see cref="SaaSPaymentsController"/> (que
/// resuelve el tenant del JWT), acá el tenant es un filtro OPCIONAL, no una restricción: sin
/// <c>tenantId</c> trae pagos de todos los tenants. Gateado por
/// <see cref="PaymentAppPermissions.AdminCrossTenant"/>, no por pertenencia a un tenant.
/// </summary>
[ApiController]
[Route("payments-app/admin")]
[Authorize]
[AllowActorTypes(ActorType.PlatformAdmin)]
public sealed class PaymentAppAdminController(IMessageBus bus) : ControllerBase
{
    [HttpGet("payments")]
    [HasPermission(PaymentAppPermissions.AdminCrossTenant)]
    [RateLimit("payment_app.f.admin_read")]
    [ProducesResponseType<IReadOnlyList<SaaSPaymentAdminResponse>>(StatusCodes.Status200OK)]
    public Task<IActionResult> SearchAllTenants(
        [FromQuery] PaymentStatus? status,
        [FromQuery] SaaSPaymentType? type,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int page,
        [FromQuery] int pageSize,
        CancellationToken ct
    ) => Search(tenantId: null, status, type, from, to, page, pageSize, ct);

    [HttpGet("tenants/{tenantId:guid}/payments")]
    [HasPermission(PaymentAppPermissions.AdminCrossTenant)]
    [RateLimit("payment_app.f.admin_read")]
    [ProducesResponseType<IReadOnlyList<SaaSPaymentAdminResponse>>(StatusCodes.Status200OK)]
    public Task<IActionResult> SearchForTenant(
        Guid tenantId,
        [FromQuery] PaymentStatus? status,
        [FromQuery] SaaSPaymentType? type,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int page,
        [FromQuery] int pageSize,
        CancellationToken ct
    ) => Search(tenantId, status, type, from, to, page, pageSize, ct);

    /// <summary>Reenvía <c>OnboardingPaymentSucceededIntegrationEvent</c>/<c>Failed</c> para un
    /// pago de onboarding ya en estado terminal cuyo evento nunca llegó a Auth (p.ej. resuelto
    /// por <c>PendingChargeReconciliationJob</c> antes de que ese job publicara el evento -- ver
    /// <see cref="RepublishOnboardingPaymentResultHandler"/>). No re-ejecuta ningún cobro, solo
    /// reenvía la notificación downstream.</summary>
    [HttpPost("payments/{id:guid}/republish-onboarding-result")]
    [HasPermission(PaymentAppPermissions.AdminCrossTenant)]
    [RateLimit("payment_app.g.admin_manage")]
    public async Task<IActionResult> RepublishOnboardingResult(Guid id, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result>(new RepublishOnboardingPaymentResultCommand(id), ct);
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    private const int ExportMaxRows = 5000;

    /// <summary>Mismos filtros que <see cref="SearchAllTenants"/> — un solo request, sin
    /// paginación (capado a <see cref="ExportMaxRows"/>; para volúmenes mayores el reporte
    /// debería moverse a un job async, fuera de scope de J.3).</summary>
    [HttpGet("payments/export")]
    [HasPermission(PaymentAppPermissions.AdminCrossTenant)]
    [RateLimit("payment_app.h.admin_export")]
    [Produces("text/csv")]
    public async Task<IActionResult> ExportCsv(
        [FromQuery] Guid? tenantId,
        [FromQuery] PaymentStatus? status,
        [FromQuery] SaaSPaymentType? type,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken ct
    )
    {
        var result = await bus.InvokeAsync<Result<IReadOnlyList<SaaSPaymentAdminResponse>>>(
            new SearchSaaSPaymentsAdminQuery(tenantId, status, type, from, to, Page: 1, PageSize: ExportMaxRows),
            ct
        );

        if (result.IsFailure)
            return StatusCode(result.Error.ToHttpStatusCode(), result.Error);

        // BB-17 — WriteWithBom: sin BOM, Excel abre el CSV con la codepage ANSI y los acentos de los
        // nombres de clientes salen como mojibake. Encoding.UTF8.GetBytes() NO emite BOM.
        var csv = CsvWriter.WriteWithBom(
            [
                "Id",
                "TenantId",
                "Status",
                "Type",
                "AmountCents",
                "Currency",
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
                        p.Type,
                        p.AmountCents.ToString(),
                        p.Currency,
                        p.ProviderCode,
                        p.ExternalChargeReference,
                        p.FailureCode,
                        p.PaidAtUtc?.ToString("O"),
                        p.CreatedAtUtc.ToString("O"),
                    ]
            )
        );

        return File(csv, "text/csv", $"saas-payments-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv");
    }

    private async Task<IActionResult> Search(
        Guid? tenantId,
        PaymentStatus? status,
        SaaSPaymentType? type,
        DateTime? from,
        DateTime? to,
        int page,
        int pageSize,
        CancellationToken ct
    )
    {
        var result = await bus.InvokeAsync<Result<IReadOnlyList<SaaSPaymentAdminResponse>>>(
            new SearchSaaSPaymentsAdminQuery(
                tenantId,
                status,
                type,
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
