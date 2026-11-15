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
using Wolverine;

namespace TaxVision.FinancialInsights.Api.Controllers;

/// <summary>El cliente solo registra y lee lotes de su propio customer_id (del JWT); lo pendiente espera el escaneo.</summary>
[ApiController]
[Authorize]
[AllowActorTypes(ActorType.CustomerPortal)]
[Route("financial/portal/import-batches")]
public sealed class PortalImportBatchesController(IMessageBus bus, IUserPermissionsSource permissions) : ControllerBase
{
    public sealed record CreatePortalImportBatchRequest(IReadOnlyList<Guid>? FileIds);

    [HttpPost]
    [HasPermission(FinancialInsightsPermissions.PortalImport)]
    [RateLimit("financial.i.portal_import_batch_create")]
    [ProducesResponseType<ImportBatchView>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] CreatePortalImportBatchRequest request, CancellationToken ct)
    {
        if (!TryGetPortalIdentity(out var tenantId, out var userId, out var customerId))
            return Unauthorized();

        // Subir al portal exige además cloudstorage.file.upload, también desde la proyección local.
        var canUpload = await permissions.HasPermissionAsync(User, CloudStoragePermissions.FileUpload, ct);
        var result = await bus.InvokeAsync<Result<ImportBatchView>>(
            new CreatePortalImportBatchCommand(tenantId, userId, customerId, request.FileIds, canUpload),
            ct
        );
        return Respond(result, view => StatusCode(StatusCodes.Status201Created, view));
    }

    [HttpGet]
    [HasPermission(FinancialInsightsPermissions.PortalImport)]
    [RateLimit("financial.f.import_batch_read")]
    [ProducesResponseType<PagedResult<ImportBatchSummaryView>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] int page, [FromQuery] int pageSize, CancellationToken ct)
    {
        if (!TryGetPortalIdentity(out var tenantId, out _, out var customerId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<PagedResult<ImportBatchSummaryView>>>(
            new ListPortalImportBatchesQuery(tenantId, customerId, page, pageSize),
            ct
        );
        return Respond(result, Ok);
    }

    [HttpGet("{batchId:guid}")]
    [HasPermission(FinancialInsightsPermissions.PortalImport)]
    [RateLimit("financial.f.import_batch_read")]
    [ProducesResponseType<ImportBatchView>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid batchId, CancellationToken ct)
    {
        if (!TryGetPortalIdentity(out var tenantId, out _, out var customerId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<ImportBatchView>>(
            new GetPortalImportBatchQuery(tenantId, customerId, batchId),
            ct
        );
        return Respond(result, Ok);
    }

    private bool TryGetPortalIdentity(out Guid tenantId, out Guid userId, out Guid customerId)
    {
        customerId = Guid.Empty;
        return this.TryGetTenantAndUser(out tenantId, out userId)
            && User.TryGetCustomerId(out customerId)
            && customerId != Guid.Empty;
    }

    private IActionResult Respond<T>(Result<T> result, Func<T, IActionResult> onSuccess)
    {
        Response.Headers.CacheControl = "no-store";
        return result.IsSuccess
            ? onSuccess(result.Value)
            : StatusCode(result.Error.ToFinancialHttpStatusCode(), result.Error);
    }
}
