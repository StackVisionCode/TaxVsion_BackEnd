using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Moderation;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Moderation;
using Wolverine;

namespace TaxVision.CloudStorage.Api.Controllers;

/// <summary>
/// Moderación de contenido (T-20): política de umbrales y cola de revisión de archivos marcados.
/// Solo PlatformAdmin y cross-tenant: la oficina nunca ve ni decide sobre lo marcado. Sin pantalla
/// todavía; se opera por API.
/// </summary>
[ApiController]
[Route("storage/admin/content-moderation")]
[Authorize]
[AllowActorTypes(ActorType.PlatformAdmin)]
[HasPermission(CloudStoragePermissions.ModerationManage)]
public sealed class ContentModerationAdminController(IMessageBus bus, ICorrelationContext correlation) : ControllerBase
{
    public sealed record UpdatePolicyRequest(
        bool Enabled,
        double BlockThreshold,
        double ReviewThreshold,
        bool ScanVideos,
        int FrameIntervalSeconds,
        int MaxFrames,
        long MaxScanBytes,
        IReadOnlyList<string>? ExcludedFolders,
        bool FailClosed
    );

    public sealed record ReviewNotesRequest(string? Notes);

    [HttpGet("policy")]
    [RateLimit("cloudstorage.f.admin_read")]
    [ProducesResponseType<ContentModerationPolicyView>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPolicy(CancellationToken ct) =>
        ToActionResult(await bus.InvokeAsync<Result<ContentModerationPolicyView>>(new GetContentModerationPolicyQuery(), ct));

    [HttpPut("policy")]
    [RateLimit("cloudstorage.g.settings_manage")]
    [ProducesResponseType<ContentModerationPolicyView>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdatePolicy(UpdatePolicyRequest request, CancellationToken ct)
    {
        if (!User.TryGetUserId(out var actorId))
            return Unauthorized();
        if (!ModerationFolderNames.TryParse(request.ExcludedFolders, out var excluded))
            return BadRequest(ContentModerationErrors.InvalidFolderType);

        var settings = new ContentModerationSettings(
            request.Enabled,
            request.BlockThreshold,
            request.ReviewThreshold,
            request.ScanVideos,
            request.FrameIntervalSeconds,
            request.MaxFrames,
            request.MaxScanBytes,
            excluded,
            request.FailClosed
        );
        return ToActionResult(
            await bus.InvokeAsync<Result<ContentModerationPolicyView>>(
                new UpdateContentModerationPolicyCommand(settings, actorId),
                ct
            )
        );
    }

    [HttpGet("policy/audit")]
    [RateLimit("cloudstorage.f.admin_read")]
    [ProducesResponseType<IReadOnlyList<ContentModerationPolicyAuditView>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> PolicyAudit(
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default
    ) =>
        Ok(
            await bus.InvokeAsync<IReadOnlyList<ContentModerationPolicyAuditView>>(
                new ListContentModerationPolicyAuditQuery(skip, take),
                ct
            )
        );

    /// <summary>status = PendingReview (por decidir, por defecto) o BlockedByPolicy.</summary>
    [HttpGet("review")]
    [RateLimit("cloudstorage.f.admin_read")]
    [ProducesResponseType<IReadOnlyList<ContentReviewItemView>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ReviewQueue(
        [FromQuery] string status = nameof(FileStatus.PendingReview),
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default
    )
    {
        if (!Enum.TryParse<FileStatus>(status, ignoreCase: true, out var parsed) || long.TryParse(status, out _))
            return BadRequest(ContentModerationErrors.NotUnderModeration);
        return ToActionResult(
            await bus.InvokeAsync<Result<IReadOnlyList<ContentReviewItemView>>>(
                new ListContentReviewQueueQuery(parsed, skip, take),
                ct
            )
        );
    }

    [HttpGet("review/{tenantId:guid}/{fileId:guid}/preview")]
    [RateLimit("cloudstorage.f.admin_read")]
    [ProducesResponseType<ContentReviewPreviewView>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Preview(Guid tenantId, Guid fileId, CancellationToken ct)
    {
        if (!User.TryGetUserId(out var actorId))
            return Unauthorized();
        return ToActionResult(
            await bus.InvokeAsync<Result<ContentReviewPreviewView>>(
                new PreviewContentReviewFileCommand(tenantId, fileId, actorId, AuditContext()),
                ct
            )
        );
    }

    [HttpPost("review/{tenantId:guid}/{fileId:guid}/approve")]
    [RateLimit("cloudstorage.g.legal_manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Approve(Guid tenantId, Guid fileId, CancellationToken ct)
    {
        if (!User.TryGetUserId(out var actorId))
            return Unauthorized();
        var result = await bus.InvokeAsync<Result>(
            new ApproveContentReviewCommand(tenantId, fileId, actorId, AuditContext()),
            ct
        );
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("review/{tenantId:guid}/{fileId:guid}/reject")]
    [RateLimit("cloudstorage.g.legal_manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Reject(Guid tenantId, Guid fileId, ReviewNotesRequest request, CancellationToken ct)
    {
        if (!User.TryGetUserId(out var actorId))
            return Unauthorized();
        var result = await bus.InvokeAsync<Result>(
            new RejectContentReviewCommand(tenantId, fileId, actorId, request.Notes, AuditContext()),
            ct
        );
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>Deja constancia de un reporte (p. ej. NCMEC, lo hace una persona) y retiene el archivo.</summary>
    [HttpPost("review/{tenantId:guid}/{fileId:guid}/report")]
    [RateLimit("cloudstorage.g.legal_manage")]
    [ProducesResponseType<ReportContentReviewFileResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Report(Guid tenantId, Guid fileId, ReviewNotesRequest request, CancellationToken ct)
    {
        if (!User.TryGetUserId(out var actorId))
            return Unauthorized();
        return ToActionResult(
            await bus.InvokeAsync<Result<ReportContentReviewFileResponse>>(
                new ReportContentReviewFileCommand(tenantId, fileId, actorId, request.Notes, AuditContext()),
                ct
            )
        );
    }

    private IActionResult ToActionResult<T>(Result<T> result) =>
        result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);

    private RequestAuditContext AuditContext() =>
        new(
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            Request.Headers.UserAgent.ToString(),
            correlation.CorrelationId
        );
}
