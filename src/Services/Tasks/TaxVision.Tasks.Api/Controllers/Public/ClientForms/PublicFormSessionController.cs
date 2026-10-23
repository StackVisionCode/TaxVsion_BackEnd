using BuildingBlocks.Results;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TaxVision.Tasks.Api.Filters;
using TaxVision.Tasks.Api.Requests.ClientForms;
using TaxVision.Tasks.ClientForms.Application.Consents;
using TaxVision.Tasks.ClientForms.Application.Participants.Commands;
using TaxVision.Tasks.ClientForms.Application.Requests;
using TaxVision.Tasks.ClientForms.Application.Requests.Commands;
using TaxVision.Tasks.ClientForms.Application.Requests.Queries;
using Wolverine;

namespace TaxVision.Tasks.Api.Controllers.Public.ClientForms;

[ApiController]
[ClientFormsEndpoint]
[Route("tasks/public/forms/session")]
[AllowAnonymous]
[SkipClientFormsAccess]
[ServiceFilter(typeof(FormSessionFilter))]
[RateLimitExempt("Anonymous public form sessions are protected by native IP and session validation.")]
public sealed class PublicFormSessionController(IMessageBus bus, PublicFormSessionContext session) : ControllerBase
{
    [HttpGet("request")]
    [EnableRateLimiting("public-forms")]
    public async Task<IActionResult> GetRequest(CancellationToken ct)
    {
        var context = session.Participant!;
        var requestId = context.BoundRequestId!.Value;
        var opened = await bus.InvokeAsync<Result<FormRequestRowVersionResponse>>(
            new OpenPortalFormRequestCommand(context, requestId, DateTime.UtcNow),
            ct
        );
        if (opened.IsFailure)
            return NotFound();
        var result = await bus.InvokeAsync<Result<PortalFormRequestDetailResponse>>(
            new GetExternalFormSessionRequestQuery(context),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : NotFound();
    }

    [HttpPost("consents")]
    [EnableRateLimiting("public-forms")]
    public async Task<IActionResult> AcceptConsent(AcceptFormConsentRequest request, CancellationToken ct)
    {
        var context = session.Participant!;
        var result = await bus.InvokeAsync<Result<AcceptFormConsentResponse>>(
            new AcceptFormConsentCommand(
                context,
                context.BoundRequestId!.Value,
                request.ComponentId,
                ClientIp(),
                UserAgent(),
                DateTime.UtcNow
            ),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("consents/decline")]
    [EnableRateLimiting("public-forms")]
    public async Task<IActionResult> DeclineConsent(AcceptFormConsentRequest request, CancellationToken ct)
    {
        var context = session.Participant!;
        var result = await bus.InvokeAsync<Result<DeclineFormConsentResponse>>(
            new DeclineFormConsentCommand(
                context,
                context.BoundRequestId!.Value,
                request.ComponentId,
                ClientIp(),
                UserAgent(),
                DateTime.UtcNow
            ),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPut("draft")]
    [EnableRateLimiting("public-forms")]
    [RequestSizeLimit(1_200_000)]
    public async Task<IActionResult> SaveDraft(SavePortalFormDraftRequest request, CancellationToken ct)
    {
        if (!TryRowVersion(request.RowVersion, out var rowVersion))
            return BadRequest(RowVersionError);
        var context = session.Participant!;
        var result = await bus.InvokeAsync<Result<FormDraftSaveOutcome>>(
            new SavePortalFormDraftCommand(
                context,
                context.BoundRequestId!.Value,
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

    [HttpPost("sensitive-answers")]
    [EnableRateLimiting("public-forms")]
    public async Task<IActionResult> CaptureSensitive(CapturePortalSensitiveAnswerRequest request, CancellationToken ct)
    {
        var context = session.Participant!;
        var result = await bus.InvokeAsync<Result<CapturedSensitiveAnswerResponse>>(
            new CapturePortalSensitiveAnswerCommand(
                context,
                context.BoundRequestId!.Value,
                request.ComponentId,
                request.RowId,
                request.Kind,
                request.Value,
                DateTime.UtcNow
            ),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("submit")]
    [EnableRateLimiting("public-forms")]
    public async Task<IActionResult> Submit(SubmitPortalFormRequestRequest request, CancellationToken ct)
    {
        if (!TryRowVersion(request.RowVersion, out var rowVersion))
            return BadRequest(RowVersionError);
        var context = session.Participant!;
        var result = await bus.InvokeAsync<Result<FormSubmitOutcome>>(
            new SubmitPortalFormRequestCommand(context, context.BoundRequestId!.Value, rowVersion, DateTime.UtcNow),
            ct
        );
        if (result.IsFailure)
            return StatusCode(result.Error.ToHttpStatusCode(), result.Error);
        return result.Value.Submitted ? Ok(result.Value) : UnprocessableEntity(result.Value.Issues);
    }

    [HttpPost("attachments/uploads")]
    [EnableRateLimiting("public-forms-upload")]
    public async Task<IActionResult> InitiateAttachment(
        InitiatePublicFormAttachmentRequest request,
        CancellationToken ct
    )
    {
        var result = await bus.InvokeAsync<Result<ExternalFormAttachmentUploadResponse>>(
            new InitiateExternalFormAttachmentUploadCommand(
                session.Participant!,
                request.ComponentId,
                request.RowId,
                request.OriginalName,
                request.ContentType,
                request.SizeBytes,
                DateTime.UtcNow
            ),
            ct
        );
        return result.IsSuccess
            ? Created(string.Empty, result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("attachments/{fileId:guid}/complete")]
    [EnableRateLimiting("public-forms-upload")]
    public async Task<IActionResult> CompleteAttachment(Guid fileId, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result>(
            new CompleteExternalFormAttachmentUploadCommand(session.Participant!, fileId),
            ct
        );
        return result.IsSuccess
            ? Accepted(new { fileId, status = "PendingScan" })
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpDelete("attachments/{attachmentId:guid}")]
    [EnableRateLimiting("public-forms-upload")]
    public async Task<IActionResult> DetachAttachment(Guid attachmentId, CancellationToken ct)
    {
        var context = session.Participant!;
        var result = await bus.InvokeAsync<Result<FormRequestRowVersionResponse>>(
            new DetachPortalFormAttachmentCommand(
                context,
                context.BoundRequestId!.Value,
                attachmentId,
                DateTime.UtcNow
            ),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("logout")]
    [EnableRateLimiting("public-forms")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        await bus.InvokeAsync<Result>(
            new RevokePublicFormSessionCommand(session.SessionToken, session.RequestHost, DateTime.UtcNow),
            ct
        );
        return NoContent();
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

    private string ClientIp() => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private string UserAgent()
    {
        var value = Request.Headers.UserAgent.ToString();
        if (string.IsNullOrWhiteSpace(value))
            return "unknown";
        return value.Length <= 512 ? value : value[..512];
    }
}
