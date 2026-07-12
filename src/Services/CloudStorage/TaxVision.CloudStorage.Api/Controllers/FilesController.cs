using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Files;
using TaxVision.CloudStorage.Application.Files.Commands;
using TaxVision.CloudStorage.Application.Files.Queries;
using Wolverine;

namespace TaxVision.CloudStorage.Api.Controllers;

[ApiController]
[Route("storage/files")]
[Authorize]
public sealed class FilesController(IMessageBus bus, ICorrelationContext correlation) : ControllerBase
{
    [HttpPost("uploads")]
    [Authorize(Policy = CloudStoragePermissions.FileUpload)]
    [ProducesResponseType<InitiatedUploadResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> InitiateUpload(InitiateUploadRequest request, CancellationToken ct)
    {
        if (!TryGetIdentity(out var tenantId, out var actorId, out var scope))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<InitiatedUploadResponse>>(
            new InitiateUploadCommand(tenantId, actorId, scope, request, AuditContext()),
            ct
        );
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetById), new { fileId = result.Value.FileId }, result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("{fileId:guid}/complete")]
    [Authorize(Policy = CloudStoragePermissions.FileUpload)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> CompleteUpload(Guid fileId, CancellationToken ct)
    {
        if (!TryGetIdentity(out var tenantId, out var actorId, out var scope))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(
            new CompleteUploadCommand(tenantId, actorId, scope, fileId, AuditContext()),
            ct
        );
        return result.IsSuccess
            ? AcceptedAtAction(nameof(GetById), new { fileId }, new { fileId, status = "PendingScan" })
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet("{fileId:guid}")]
    [Authorize(Policy = CloudStoragePermissions.FileView)]
    [ProducesResponseType<FileResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetById(Guid fileId, CancellationToken ct)
    {
        if (!TryGetIdentity(out var tenantId, out _, out var scope))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<FileResponse>>(new GetFileQuery(tenantId, scope, fileId), ct);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet]
    [Authorize(Policy = CloudStoragePermissions.FileView)]
    [ProducesResponseType<IReadOnlyList<FileResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default
    )
    {
        if (!TryGetIdentity(out var tenantId, out _, out var scope))
            return Unauthorized();

        var result = await bus.InvokeAsync<IReadOnlyList<FileResponse>>(
            new ListFilesQuery(tenantId, scope, skip, take),
            ct
        );
        return Ok(result);
    }

    [HttpPost("{fileId:guid}/download-url")]
    [Authorize(Policy = CloudStoragePermissions.FileDownload)]
    [ProducesResponseType<DownloadUrlResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> IssueDownloadUrl(Guid fileId, CancellationToken ct)
    {
        if (!TryGetIdentity(out var tenantId, out var actorId, out var scope))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<DownloadUrlResponse>>(
            new IssueDownloadUrlQuery(tenantId, actorId, scope, fileId, AuditContext()),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpDelete("{fileId:guid}")]
    [Authorize(Policy = CloudStoragePermissions.FileDelete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid fileId, CancellationToken ct)
    {
        if (!TryGetIdentity(out var tenantId, out var actorId, out var scope))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(
            new DeleteFileCommand(tenantId, actorId, scope, fileId, AuditContext()),
            ct
        );
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    private RequestAuditContext AuditContext() =>
        new(
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            Request.Headers.UserAgent.ToString(),
            correlation.CorrelationId
        );

    private bool TryGetIdentity(out Guid tenantId, out Guid actorId, out StorageActorScope scope)
    {
        actorId = Guid.Empty;
        scope = new StorageActorScope(false, null);
        if (!Guid.TryParse(User.FindFirst("tenant_id")?.Value, out tenantId))
            return false;
        var subject =
            User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(subject, out actorId))
            return false;

        var isCustomerPortal = string.Equals(
            User.FindFirst("actor_type")?.Value,
            "CustomerPortal",
            StringComparison.OrdinalIgnoreCase
        );
        Guid? customerId = Guid.TryParse(User.FindFirst("customer_id")?.Value, out var parsedCustomerId)
            ? parsedCustomerId
            : null;
        scope = new StorageActorScope(isCustomerPortal, customerId);
        return true;
    }
}
