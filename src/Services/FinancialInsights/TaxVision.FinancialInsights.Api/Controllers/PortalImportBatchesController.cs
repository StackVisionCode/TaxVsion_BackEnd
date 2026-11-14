using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TaxVision.FinancialInsights.Api.Imports;
using TaxVision.FinancialInsights.Application.Imports;
using TaxVision.FinancialInsights.Domain.Imports;
using TaxVision.FinancialInsights.Infrastructure.Persistence;

namespace TaxVision.FinancialInsights.Api.Controllers;

/// <summary>El cliente solo puede registrar archivos de su propio customer_id; los pendientes esperan el escaneo.</summary>
[ApiController]
[Authorize]
[AllowActorTypes(ActorType.CustomerPortal)]
[Route("financial/portal/import-batches")]
public sealed class PortalImportBatchesController(UpstreamImportAccess access, FinancialInsightsDbContext db) : ControllerBase
{
    public sealed record CreatePortalImportRequest(IReadOnlyList<Guid> FileIds);

    [HttpPost]
    [HasPermission(FinancialInsightsPermissions.PortalImport)]
    [RateLimit("financial.i.import_batch_create")]
    public async Task<IActionResult> Create([FromBody] CreatePortalImportRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId)
            || !User.TryGetCustomerId(out var customerId) || customerId == Guid.Empty) return Unauthorized();
        if (request.FileIds is null || request.FileIds.Count is < 1 or > 20
            || request.FileIds.Any(id => id == Guid.Empty) || request.FileIds.Distinct().Count() != request.FileIds.Count)
            return BadRequest(new { code = "Import.InvalidBatch" });
        var bearer = Bearer();
        if (bearer is null) return Unauthorized();
        var permission = await access.CanPortalImportAsync(bearer, ct);
        if (permission == ImportAccessResult.Unavailable) return StatusCode(StatusCodes.Status503ServiceUnavailable);
        if (permission == ImportAccessResult.ModuleUnavailable)
            return StatusCode(StatusCodes.Status403Forbidden, new { code = "Financial.ModuleUnavailable" });
        if (permission != ImportAccessResult.Granted) return Forbid();

        var tenant = await db.TenantDirectoryProjections.SingleOrDefaultAsync(ct);
        if (tenant is { IsActive: false }) return StatusCode(StatusCodes.Status403Forbidden, new { code = "Tenant.Inactive" });
        var customer = await db.CustomerDirectoryProjections.AsNoTracking()
            .SingleOrDefaultAsync(x => x.CustomerId == customerId, ct);
        if (customer is not { IsActive: true }) return NotFound();

        var fileStates = new Dictionary<Guid, PortalFileEligibility>();
        foreach (var fileId in request.FileIds)
        {
            var eligible = await access.CanRegisterPortalFileAsync(fileId, customerId, bearer, ct);
            if (eligible == PortalFileEligibility.Unavailable) return StatusCode(StatusCodes.Status503ServiceUnavailable);
            if (eligible == PortalFileEligibility.Ineligible) return UnprocessableEntity(new { code = "Import.FileNotEligible", fileId });
            fileStates[fileId] = eligible;
        }
        if (await db.ImportDocuments.AnyAsync(x => request.FileIds.Contains(x.FileId), ct))
            return Conflict(new { code = "Import.FileAlreadyRegistered" });

        var batch = ImportBatch.Create(tenantId, customerId, userId, request.FileIds);
        foreach (var document in batch.Documents)
            if (fileStates[document.FileId] == PortalFileEligibility.Ready) document.MarkReady();
        db.ImportBatches.Add(batch);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        { return Conflict(new { code = "Import.FileAlreadyRegistered" }); }
        // El evento FileAvailable puede adelantarse al commit del lote; la lectura posterior
        // cierra esa carrera y deja el documento listo aunque el navegador ya se haya cerrado.
        var changed = false;
        foreach (var document in batch.Documents.Where(d => fileStates[d.FileId] == PortalFileEligibility.AwaitingScan))
        {
            var current = await access.CanRegisterPortalFileAsync(document.FileId, customerId, bearer, ct);
            if (current == PortalFileEligibility.Ready) changed |= document.MarkReady();
            else if (current == PortalFileEligibility.Ineligible) changed |= document.Reject("FileNotEligible");
        }
        if (changed) await db.SaveChangesAsync(ct);
        Response.Headers.CacheControl = "no-store";
        return StatusCode(StatusCodes.Status201Created, ImportBatchResponse.From(batch));
    }

    [HttpGet("{batchId:guid}")]
    [HasPermission(FinancialInsightsPermissions.PortalImport)]
    [RateLimit("financial.f.import_batch_read")]
    public async Task<IActionResult> Get(Guid batchId, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out _, out _) || !User.TryGetCustomerId(out var customerId)) return Unauthorized();
        var bearer = Bearer();
        if (bearer is null) return Unauthorized();
        var permission = await access.CanPortalImportAsync(bearer, ct);
        if (permission == ImportAccessResult.Unavailable) return StatusCode(StatusCodes.Status503ServiceUnavailable);
        if (permission == ImportAccessResult.ModuleUnavailable)
            return StatusCode(StatusCodes.Status403Forbidden, new { code = "Financial.ModuleUnavailable" });
        if (permission != ImportAccessResult.Granted) return Forbid();
        var batch = await db.ImportBatches.AsNoTracking().Include(x => x.Documents)
            .SingleOrDefaultAsync(x => x.Id == batchId && x.CustomerId == customerId, ct);
        if (batch is null) return NotFound();
        var customer = await db.CustomerDirectoryProjections.AsNoTracking()
            .SingleOrDefaultAsync(x => x.CustomerId == customerId, ct);
        if (customer is not { IsActive: true }) return NotFound();
        Response.Headers.CacheControl = "no-store";
        return Ok(new { batch.Id, batch.CustomerId, batch.CreatedAtUtc,
            Documents = batch.Documents.Select(x => new { x.Id, x.FileId, x.Status, x.FailureReason }) });
    }

    private string? Bearer()
    {
        var token = Request.Headers.Authorization.ToString();
        return token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) && token.Length > 7
            ? token[7..].Trim() : null;
    }
}
