using BuildingBlocks.Web.Identity;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TaxVision.FinancialInsights.Api.Imports;
using TaxVision.FinancialInsights.Application.Imports;
using TaxVision.FinancialInsights.Domain.Imports;
using TaxVision.FinancialInsights.Domain.Projections;
using TaxVision.FinancialInsights.Infrastructure.Persistence;

namespace TaxVision.FinancialInsights.Api.Controllers;

[ApiController]
[Authorize]
[Route("financial/import-batches")]
public sealed class ImportBatchesController(UpstreamImportAccess access, FinancialInsightsDbContext db) : ControllerBase
{
    [HttpPost("{batchId:guid}/documents/{documentId:guid}/retry")]
    [HasPermission(FinancialInsightsPermissions.Import)]
    [RateLimit("financial.i.import_batch_create")]
    [AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
    public async Task<IActionResult> Retry(Guid batchId, Guid documentId, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _)) return Unauthorized();
        var token = Request.Headers.Authorization.ToString();
        if (!token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) || token.Length <= 7)
            return Unauthorized();
        var bearer = token[7..].Trim();
        var permission = await access.CanImportAsync(bearer, ct);
        if (permission == ImportAccessResult.Unavailable) return StatusCode(StatusCodes.Status503ServiceUnavailable);
        if (permission == ImportAccessResult.PermissionDenied)
            return Deny(AuthorizationDenial.ForPermission(FinancialInsightsPermissions.Import));
        if (permission == ImportAccessResult.ModuleUnavailable)
            return Deny(AuthorizationDenial.ForModule("financial_insights"));
        var batch = await db.ImportBatches.SingleOrDefaultAsync(x => x.Id == batchId && x.TenantId == tenantId, ct);
        if (batch is null) return NotFound();
        var customer = await access.CanAccessCustomerAsync(batch.CustomerId, tenantId, bearer, ct);
        if (customer is null) return StatusCode(StatusCodes.Status503ServiceUnavailable);
        if (!customer.IsAccessible) return NotFound();
        var document = await db.ImportDocuments.SingleOrDefaultAsync(x => x.Id == documentId && x.BatchId == batchId, ct);
        if (document is null) return NotFound();
        if (await db.AiProcessingReservations.AnyAsync(x => x.ImportDocumentId == documentId, ct))
            return Conflict(new { code = "Import.InferenceAlreadyAttempted" });
        if (!document.RetryBeforeInference()) return Conflict(new { code = "Import.NotRetryable" });
        await db.SaveChangesAsync(ct);
        return Accepted(new { document.Id, document.Status });
    }

    [HttpGet("{batchId:guid}")]
    [HasPermission(FinancialInsightsPermissions.View)]
    [RateLimit("financial.f.import_batch_read")]
    [AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
    public async Task<IActionResult> Get(Guid batchId, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _)) return Unauthorized();
        var token = Request.Headers.Authorization.ToString();
        if (!token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) || token.Length <= 7)
            return Unauthorized();
        var bearer = token[7..].Trim();
        var permission = await access.CanViewAsync(bearer, ct);
        if (permission == ImportAccessResult.Unavailable) return StatusCode(StatusCodes.Status503ServiceUnavailable);
        if (permission == ImportAccessResult.PermissionDenied)
            return Deny(AuthorizationDenial.ForPermission(FinancialInsightsPermissions.View));
        if (permission == ImportAccessResult.ModuleUnavailable)
            return Deny(AuthorizationDenial.ForModule("financial_insights"));
        var batch = await db.ImportBatches.Include(x => x.Documents)
            .SingleOrDefaultAsync(x => x.Id == batchId && x.TenantId == tenantId, ct);
        if (batch is null) return NotFound();
        var customer = await access.CanAccessCustomerAsync(batch.CustomerId, tenantId, bearer, ct);
        if (customer is null) return StatusCode(StatusCodes.Status503ServiceUnavailable);
        if (!customer.IsAccessible) return NotFound();
        var documentIds = batch.Documents.Select(x => x.Id).ToArray();
        var movements = await db.FinancialMovements.AsNoTracking()
            .Where(x => x.TenantId == tenantId && documentIds.Contains(x.ImportDocumentId))
            .OrderBy(x => x.TransactionDate).Select(x => new
            {
                x.Id, x.ImportDocumentId, x.TransactionDate, x.Description, x.Amount, x.Currency,
                x.Direction, x.SuggestedCategory, x.SourcePage, x.RequiresReview
            }).ToListAsync(ct);
        Response.Headers.CacheControl = "no-store";
        return Ok(new { batch.Id, batch.CustomerId, batch.CreatedAtUtc,
            Documents = batch.Documents.Select(x => new { x.Id, x.FileId, x.Status, x.FailureReason }),
            Movements = movements });
    }

    [HttpPost]
    [HasPermission(FinancialInsightsPermissions.Import)]
    [RateLimit("financial.i.import_batch_create")]
    [AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
    [ProducesResponseType<ImportBatchResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] CreateImportBatchRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId)) return Unauthorized();
        if (request.CustomerId == Guid.Empty || request.FileIds is null || request.FileIds.Count is < 1 or > 20
            || request.FileIds.Any(id => id == Guid.Empty) || request.FileIds.Distinct().Count() != request.FileIds.Count)
            return BadRequest(new { code = "Import.InvalidBatch" });

        var token = Request.Headers.Authorization.ToString();
        if (!token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) || token.Length <= 7)
            return Unauthorized();
        var tenant = await db.TenantDirectoryProjections.SingleOrDefaultAsync(ct);
        if (tenant is { IsActive: false })
            return StatusCode(StatusCodes.Status403Forbidden, new { code = "Tenant.Inactive" });
        var bearer = token[7..].Trim();
        var permission = await access.CanImportAsync(bearer, ct);
        if (permission == ImportAccessResult.Unavailable) return StatusCode(StatusCodes.Status503ServiceUnavailable);
        if (permission == ImportAccessResult.PermissionDenied)
            return Deny(AuthorizationDenial.ForPermission(FinancialInsightsPermissions.Import));
        if (permission == ImportAccessResult.ModuleUnavailable)
            return Deny(AuthorizationDenial.ForModule("financial_insights"));

        var customerAccess = await access.CanAccessCustomerAsync(request.CustomerId, tenantId, bearer, ct);
        if (customerAccess is null) return StatusCode(StatusCodes.Status503ServiceUnavailable);
        if (!customerAccess.IsAccessible) return NotFound();

        foreach (var fileId in request.FileIds)
        {
            var eligible = await access.CanRegisterFileAsync(fileId, request.CustomerId, bearer, ct);
            if (eligible is null) return StatusCode(StatusCodes.Status503ServiceUnavailable);
            if (!eligible.Value) return UnprocessableEntity(new { code = "Import.FileNotEligible", fileId });
        }

        if (await db.ImportDocuments.AnyAsync(x => request.FileIds.Contains(x.FileId), ct))
            return Conflict(new { code = "Import.FileAlreadyRegistered" });

        var batch = ImportBatch.Create(tenantId, request.CustomerId, userId, request.FileIds);
        foreach (var document in batch.Documents) document.MarkReady();
        var customerProjection = await db.CustomerDirectoryProjections
            .SingleOrDefaultAsync(x => x.CustomerId == request.CustomerId, ct);
        if (customerProjection is null)
            db.CustomerDirectoryProjections.Add(CustomerDirectoryProjection.Create(tenantId,
                request.CustomerId, customerAccess.DisplayName, DateTime.UtcNow));
        else customerProjection.Apply(customerAccess.DisplayName, true, DateTime.UtcNow);
        db.ImportBatches.Add(batch);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        { return Conflict(new { code = "Import.FileAlreadyRegistered" }); }
        Response.Headers.CacheControl = "no-store";
        return StatusCode(StatusCodes.Status201Created, ImportBatchResponse.From(batch));
    }

    private IActionResult Deny(AuthorizationDenial denial) => new ObjectResult(denial.ToProblemDetails(HttpContext))
    {
        StatusCode = StatusCodes.Status403Forbidden,
        ContentTypes = { "application/problem+json" }
    };
}
