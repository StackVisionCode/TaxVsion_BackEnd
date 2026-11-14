using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.FinancialInsights.Api.Results;
using TaxVision.FinancialInsights.Application.Imports;
using TaxVision.FinancialInsights.Application.Imports.Commands;
using TaxVision.FinancialInsights.Application.Imports.Queries;
using TaxVision.FinancialInsights.Domain.Imports;
using Wolverine;

namespace TaxVision.FinancialInsights.Api.Controllers;

/// <summary>
/// Lotes de estados de cuenta para el staff. Tenant y usuario salen SIEMPRE del JWT; permiso y módulo los valida
/// [HasPermission] contra las proyecciones locales; la visibilidad por cliente la decide el handler.
/// </summary>
[ApiController]
[Authorize]
[Route("financial/import-batches")]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class ImportBatchesController(IMessageBus bus, IUserPermissionsSource permissions) : ControllerBase
{
    public sealed record CreateImportBatchRequest(Guid CustomerId, IReadOnlyList<Guid>? FileIds);

    [HttpPost]
    [HasPermission(FinancialInsightsPermissions.Import)]
    [RateLimit("financial.i.import_batch_create")]
    [ProducesResponseType<ImportBatchView>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] CreateImportBatchRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<ImportBatchView>>(
            new CreateImportBatchCommand(tenantId, userId, request.CustomerId, request.FileIds, await CanViewAllCustomers(ct)),
            ct
        );
        return Respond(result, view => StatusCode(StatusCodes.Status201Created, view));
    }

    [HttpGet]
    [HasPermission(FinancialInsightsPermissions.View)]
    [RateLimit("financial.f.import_batch_read")]
    [ProducesResponseType<PagedResult<ImportBatchSummaryView>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] Guid? customerId,
        [FromQuery] ImportDocumentStatus? status,
        [FromQuery] int page,
        [FromQuery] int pageSize,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<PagedResult<ImportBatchSummaryView>>(
            new ListImportBatchesQuery(tenantId, userId, await CanViewAllCustomers(ct), customerId, status, page, pageSize),
            ct
        );
        Response.Headers.CacheControl = "no-store";
        return Ok(result);
    }

    [HttpGet("{batchId:guid}")]
    [HasPermission(FinancialInsightsPermissions.View)]
    [RateLimit("financial.f.import_batch_read")]
    [ProducesResponseType<ImportBatchDetailView>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid batchId, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<ImportBatchDetailView>>(
            new GetImportBatchQuery(tenantId, userId, batchId, await CanViewAllCustomers(ct)),
            ct
        );
        return Respond(result, Ok);
    }

    [HttpPost("{batchId:guid}/documents/{documentId:guid}/retry")]
    [HasPermission(FinancialInsightsPermissions.Import)]
    [RateLimit("financial.i.import_document_retry")]
    [ProducesResponseType<ImportDocumentView>(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Retry(Guid batchId, Guid documentId, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<ImportDocumentView>>(
            new RetryImportDocumentCommand(tenantId, userId, batchId, documentId, await CanViewAllCustomers(ct)),
            ct
        );
        return Respond(result, Accepted);
    }

    // Visibilidad por asignación: con customers.view_all ve a todos los clientes del tenant.
    private Task<bool> CanViewAllCustomers(CancellationToken ct) =>
        permissions.HasPermissionAsync(User, CustomersPermissions.ViewAll, ct);

    private IActionResult Respond<T>(Result<T> result, Func<T, IActionResult> onSuccess)
    {
        Response.Headers.CacheControl = "no-store";
        return result.IsSuccess
            ? onSuccess(result.Value)
            : StatusCode(result.Error.ToFinancialHttpStatusCode(), result.Error);
    }
}
