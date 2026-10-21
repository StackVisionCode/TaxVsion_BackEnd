using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Tasks.Api.Requests.ClientForms;
using TaxVision.Tasks.ClientForms.Application.Requests;
using TaxVision.Tasks.ClientForms.Application.Requests.Commands;
using TaxVision.Tasks.ClientForms.Application.Requests.Queries;
using Wolverine;

namespace TaxVision.Tasks.Api.Controllers.Portal.ClientForms;

[ApiController]
[Route("tasks/portal/forms")]
[AllowActorTypes(ActorType.CustomerPortal)]
[HasPermission(TasksPermissions.PortalForms)]
public sealed class PortalFormRequestsController(IMessageBus bus) : ControllerBase
{
    [HttpGet]
    [RateLimit("task.f.portal_form_read")]
    public async Task<IActionResult> List([FromQuery] bool onlyOpen = true, CancellationToken ct = default)
    {
        if (!TryIdentity(out var tenantId, out _, out var customerId))
            return Forbid();
        return Ok(await bus.InvokeAsync<IReadOnlyList<PortalFormRequestSummaryResponse>>(
            new ListPortalFormRequestsQuery(tenantId, customerId, onlyOpen),
            ct
        ));
    }

    [HttpGet("{id:guid}")]
    [RateLimit("task.f.portal_form_read")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        if (!TryIdentity(out var tenantId, out _, out var customerId))
            return Forbid();
        var result = await bus.InvokeAsync<Result<PortalFormRequestDetailResponse>>(
            new GetPortalFormRequestQuery(tenantId, customerId, id),
            ct
        );
        return result.IsFailure ? StatusCode(result.Error.ToHttpStatusCode(), result.Error) : Ok(result.Value);
    }

    [HttpPost("{id:guid}/open")]
    [RateLimit("task.h.portal_form_draft")]
    public async Task<IActionResult> Open(Guid id, CancellationToken ct)
    {
        if (!TryIdentity(out var tenantId, out var userId, out var customerId))
            return Forbid();
        var result = await bus.InvokeAsync<Result>(
            new OpenPortalFormRequestCommand(ParticipantContext.Portal(tenantId, customerId, userId), id, DateTime.UtcNow),
            ct
        );
        return result.IsFailure ? StatusCode(result.Error.ToHttpStatusCode(), result.Error) : NoContent();
    }

    [HttpPut("{id:guid}/draft")]
    [RequestSizeLimit(1_200_000)]
    [RateLimit("task.h.portal_form_draft")]
    public async Task<IActionResult> SaveDraft(Guid id, SavePortalFormDraftRequest request, CancellationToken ct)
    {
        if (!TryIdentity(out var tenantId, out var userId, out var customerId))
            return Forbid();
        if (!TryRowVersion(request.RowVersion, out var rowVersion))
            return BadRequest(RowVersionError);
        var result = await bus.InvokeAsync<Result<FormDraftSaveOutcome>>(
            new SavePortalFormDraftCommand(
                ParticipantContext.Portal(tenantId, customerId, userId),
                id,
                request.Answers.GetRawText(),
                rowVersion,
                DateTime.UtcNow
            ),
            ct
        );
        if (result.IsFailure)
            return StatusCode(result.Error.ToHttpStatusCode(), result.Error);
        return result.Value.Saved ? Ok(result.Value) : UnprocessableEntity(result.Value.Issues);
    }

    [HttpPost("{id:guid}/attachments")]
    [RateLimit("task.h.portal_form_attachment")]
    public async Task<IActionResult> RegisterAttachment(Guid id, RegisterPortalFormAttachmentRequest request, CancellationToken ct)
    {
        if (!TryIdentity(out var tenantId, out var userId, out var customerId))
            return Forbid();
        var result = await bus.InvokeAsync<Result<FormAttachmentResponse>>(
            new RegisterPortalFormAttachmentCommand(
                ParticipantContext.Portal(tenantId, customerId, userId),
                id,
                request.ComponentId,
                request.RowId,
                request.FileId,
                request.DisplayName,
                request.ContentType,
                request.SizeBytes,
                DateTime.UtcNow
            ),
            ct
        );
        return result.IsFailure
            ? StatusCode(result.Error.ToHttpStatusCode(), result.Error)
            : CreatedAtAction(nameof(Get), new { id }, result.Value);
    }

    [HttpDelete("{id:guid}/attachments/{attachmentId:guid}")]
    [RateLimit("task.h.portal_form_attachment")]
    public async Task<IActionResult> DetachAttachment(Guid id, Guid attachmentId, CancellationToken ct)
    {
        if (!TryIdentity(out var tenantId, out var userId, out var customerId))
            return Forbid();
        var result = await bus.InvokeAsync<Result>(
            new DetachPortalFormAttachmentCommand(
                ParticipantContext.Portal(tenantId, customerId, userId),
                id,
                attachmentId,
                DateTime.UtcNow
            ),
            ct
        );
        return result.IsFailure ? StatusCode(result.Error.ToHttpStatusCode(), result.Error) : NoContent();
    }

    [HttpPost("{id:guid}/sensitive-answers")]
    [RateLimit("task.h.portal_form_sensitive")]
    public async Task<IActionResult> CaptureSensitive(Guid id, CapturePortalSensitiveAnswerRequest request, CancellationToken ct)
    {
        if (!TryIdentity(out var tenantId, out var userId, out var customerId))
            return Forbid();
        var result = await bus.InvokeAsync<Result<CapturedSensitiveAnswerResponse>>(
            new CapturePortalSensitiveAnswerCommand(
                ParticipantContext.Portal(tenantId, customerId, userId),
                id,
                request.ComponentId,
                request.RowId,
                request.Kind,
                request.Value,
                DateTime.UtcNow
            ),
            ct
        );
        Response.Headers.CacheControl = "no-store";
        return result.IsFailure
            ? StatusCode(result.Error.ToHttpStatusCode(), result.Error)
            : CreatedAtAction(nameof(Get), new { id }, result.Value);
    }

    [HttpPost("{id:guid}/submit")]
    [RateLimit("task.h.portal_form_submit")]
    public async Task<IActionResult> Submit(Guid id, SubmitPortalFormRequestRequest request, CancellationToken ct)
    {
        if (!TryIdentity(out var tenantId, out var userId, out var customerId))
            return Forbid();
        if (!TryRowVersion(request.RowVersion, out var rowVersion))
            return BadRequest(RowVersionError);
        var result = await bus.InvokeAsync<Result<FormSubmitOutcome>>(
            new SubmitPortalFormRequestCommand(
                ParticipantContext.Portal(tenantId, customerId, userId),
                id,
                rowVersion,
                DateTime.UtcNow
            ),
            ct
        );
        if (result.IsFailure)
            return StatusCode(result.Error.ToHttpStatusCode(), result.Error);
        return result.Value.Submitted ? Ok(result.Value) : UnprocessableEntity(result.Value.Issues);
    }

    private bool TryIdentity(out Guid tenantId, out Guid userId, out Guid customerId)
    {
        customerId = Guid.Empty;
        return this.TryGetTenantAndUser(out tenantId, out userId) && User.TryGetCustomerId(out customerId);
    }

    private static bool TryRowVersion(string value, out byte[] rowVersion)
    {
        try
        {
            rowVersion = Convert.FromBase64String(value);
            return rowVersion.Length > 0;
        }
        catch (FormatException)
        {
            rowVersion = [];
            return false;
        }
    }

    private static readonly Error RowVersionError = new("FormRequest.InvalidRowVersion", "The row version is invalid.");
}
