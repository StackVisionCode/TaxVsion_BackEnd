using System.Text;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.Csv;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.PaymentApp.Api.Authorization;
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
public sealed class PaymentAppAdminController(IMessageBus bus) : ControllerBase
{
    [HttpGet("payments")]
    [HasPermission(PaymentAppPermissions.AdminCrossTenant)]
    [ProducesResponseType<IReadOnlyList<SaaSPaymentAdminResponse>>(StatusCodes.Status200OK)]
    public Task<IActionResult> SearchAllTenants(
        [FromQuery] PaymentStatus? status, [FromQuery] SaaSPaymentType? type,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] int page, [FromQuery] int pageSize, CancellationToken ct) =>
        Search(tenantId: null, status, type, from, to, page, pageSize, ct);

    [HttpGet("tenants/{tenantId:guid}/payments")]
    [HasPermission(PaymentAppPermissions.AdminCrossTenant)]
    [ProducesResponseType<IReadOnlyList<SaaSPaymentAdminResponse>>(StatusCodes.Status200OK)]
    public Task<IActionResult> SearchForTenant(
        Guid tenantId, [FromQuery] PaymentStatus? status, [FromQuery] SaaSPaymentType? type,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] int page, [FromQuery] int pageSize, CancellationToken ct) =>
        Search(tenantId, status, type, from, to, page, pageSize, ct);

    private const int ExportMaxRows = 5000;

    /// <summary>Mismos filtros que <see cref="SearchAllTenants"/> — un solo request, sin
    /// paginación (capado a <see cref="ExportMaxRows"/>; para volúmenes mayores el reporte
    /// debería moverse a un job async, fuera de scope de J.3).</summary>
    [HttpGet("payments/export")]
    [HasPermission(PaymentAppPermissions.AdminCrossTenant)]
    [Produces("text/csv")]
    public async Task<IActionResult> ExportCsv(
        [FromQuery] Guid? tenantId, [FromQuery] PaymentStatus? status, [FromQuery] SaaSPaymentType? type,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<IReadOnlyList<SaaSPaymentAdminResponse>>>(
            new SearchSaaSPaymentsAdminQuery(tenantId, status, type, from, to, Page: 1, PageSize: ExportMaxRows), ct);

        if (result.IsFailure)
            return StatusCode(result.Error.ToHttpStatusCode(), result.Error);

        var csv = CsvWriter.Write(
            ["Id", "TenantId", "Status", "Type", "AmountCents", "Currency", "ProviderCode", "ExternalChargeReference", "FailureCode", "PaidAtUtc", "CreatedAtUtc"],
            result.Value.Select(p => (IReadOnlyList<string?>)
            [
                p.Id.ToString(), p.TenantId.ToString(), p.Status, p.Type, p.AmountCents.ToString(), p.Currency,
                p.ProviderCode, p.ExternalChargeReference, p.FailureCode, p.PaidAtUtc?.ToString("O"), p.CreatedAtUtc.ToString("O"),
            ]));

        return File(Encoding.UTF8.GetBytes(csv), "text/csv", $"saas-payments-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv");
    }

    private async Task<IActionResult> Search(
        Guid? tenantId, PaymentStatus? status, SaaSPaymentType? type, DateTime? from, DateTime? to, int page, int pageSize, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<IReadOnlyList<SaaSPaymentAdminResponse>>>(
            new SearchSaaSPaymentsAdminQuery(tenantId, status, type, from, to, page <= 0 ? 1 : page, pageSize <= 0 ? 50 : pageSize), ct);

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
