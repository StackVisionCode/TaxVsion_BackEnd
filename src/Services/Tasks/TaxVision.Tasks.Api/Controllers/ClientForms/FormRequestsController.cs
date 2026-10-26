using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Permissions;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.Middleware;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Tasks.Api.Filters;
using TaxVision.Tasks.Api.Requests.ClientForms;
using TaxVision.Tasks.ClientForms.Application.Actions;
using TaxVision.Tasks.ClientForms.Application.Actions.Commands;
using TaxVision.Tasks.ClientForms.Application.Participants.Commands;
using TaxVision.Tasks.ClientForms.Application.Requests;
using TaxVision.Tasks.ClientForms.Application.Requests.Commands;
using TaxVision.Tasks.ClientForms.Application.Requests.Queries;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using Wolverine;

namespace TaxVision.Tasks.Api.Controllers.ClientForms;

[ApiController]
[ClientFormsEndpoint]
[Route("tasks/forms/requests")]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class FormRequestsController(IMessageBus bus, IUserPermissionsSource permissions) : ControllerBase
{
    [HttpGet]
    [HasPermission(TasksPermissions.FormRequestsRead)]
    [RateLimit("task.f.form_read")]
    public async Task<IActionResult> List(
        [FromQuery] Guid? customerId,
        [FromQuery] FormParticipantKind? participantKind,
        [FromQuery] Guid? responsibleUserId,
        [FromQuery(Name = "status")] FormRequestStatus[]? statuses,
        [FromQuery] Guid? templateId,
        [FromQuery] bool? overdue,
        [FromQuery] string? search,
        [FromQuery] FormRequestInboxView view = FormRequestInboxView.NeedsAction,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken ct = default
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Forbid();
        var result = await bus.InvokeAsync<BuildingBlocks.Common.PagedResult<FormRequestListItemResponse>>(
            new ListFormRequestsQuery(
                tenantId,
                userId,
                await CanViewAllAsync(ct),
                customerId,
                participantKind,
                responsibleUserId,
                statuses,
                templateId,
                overdue,
                search,
                page,
                pageSize,
                view
            ),
            ct
        );
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [HasPermission(TasksPermissions.FormRequestsRead)]
    [RateLimit("task.f.form_read")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Forbid();
        return ToActionResult(
            await bus.InvokeAsync<Result<StaffFormRequestDetailResponse>>(
                new GetFormRequestQuery(tenantId, userId, await CanViewAllAsync(ct), id),
                ct
            )
        );
    }

    [HttpGet("{id:guid}/revisions/{revisionNumber:int}")]
    [HasPermission(TasksPermissions.FormRequestsRead)]
    [RateLimit("task.f.form_read")]
    public async Task<IActionResult> GetRevision(Guid id, int revisionNumber, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Forbid();
        return ToActionResult(
            await bus.InvokeAsync<Result<FormRequestRevisionResponse>>(
                new GetFormRequestRevisionQuery(tenantId, userId, await CanViewAllAsync(ct), id, revisionNumber),
                ct
            )
        );
    }

    [HttpPost]
    [HasPermission(TasksPermissions.FormRequestsManage)]
    [RateLimit("task.h.form_requests_write")]
    public async Task<IActionResult> Assign(AssignFormRequestRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Forbid();
        var result = await bus.InvokeAsync<Result<FormRequestResponse>>(
            new AssignFormRequestCommand(
                tenantId,
                userId,
                await CanViewAllAsync(ct),
                request.CustomerId,
                request.TemplateId,
                request.Title,
                request.MessageToClient,
                request.DueAtUtc,
                request.TaxYear,
                request.TaskId,
                request.PrefillFromRequestId,
                DateTime.UtcNow
            ),
            ct
        );
        return result.IsFailure
            ? StatusCode(result.Error.ToHttpStatusCode(), result.Error)
            : CreatedAtAction(nameof(Get), new { id = result.Value.Id }, result.Value);
    }

    [HttpPost("external")]
    [HasPermission(TasksPermissions.FormRequestsSendExternal)]
    [RateLimit("task.h.form_requests_write")]
    public async Task<IActionResult> AssignExternal(AssignExternalFormRequestRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Forbid();
        var result = await bus.InvokeAsync<Result<FormRequestResponse>>(
            new AssignExternalFormRequestCommand(
                tenantId,
                userId,
                request.Email,
                request.FullName,
                request.TemplateId,
                request.EntryNoticeKey,
                request.Title,
                request.MessageToClient,
                request.DueAtUtc,
                request.TaxYear,
                DateTime.UtcNow
            ),
            ct
        );
        return result.IsFailure
            ? StatusCode(result.Error.ToHttpStatusCode(), result.Error)
            : CreatedAtAction(nameof(Get), new { id = result.Value.Id }, result.Value);
    }

    [HttpPost("{id:guid}/external-access/resend")]
    [HasPermission(TasksPermissions.FormRequestsSendExternal)]
    [RateLimit("task.h.form_requests_write")]
    public async Task<IActionResult> ResendExternalAccess(Guid id, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Forbid();
        var result = await bus.InvokeAsync<Result>(
            new ResendExternalFormAccessCommand(tenantId, userId, await CanViewAllAsync(ct), id, DateTime.UtcNow),
            ct
        );
        return result.IsFailure ? StatusCode(result.Error.ToHttpStatusCode(), result.Error) : NoContent();
    }

    [HttpPost("{id:guid}/return")]
    [HasPermission(TasksPermissions.FormRequestsReview)]
    [RateLimit("task.h.form_requests_write")]
    public async Task<IActionResult> Return(Guid id, ReturnFormRequestRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Forbid();
        if (!TryRowVersion(request.RowVersion, out var rowVersion))
            return BadRequest(RowVersionError);
        var comments = new List<FormFieldCommentInput>();
        foreach (var item in request.FieldComments)
        {
            var address = FormAnswerAddress.Create(item.ComponentId, item.RowId);
            if (address.IsFailure)
                return BadRequest(address.Error);
            comments.Add(new FormFieldCommentInput(address.Value, item.Comment));
        }
        return ToActionResult(
            await bus.InvokeAsync<Result<FormRequestResponse>>(
                new ReturnFormRequestCommand(
                    tenantId,
                    userId,
                    await CanViewAllAsync(ct),
                    id,
                    request.Message,
                    comments,
                    rowVersion,
                    DateTime.UtcNow
                ),
                ct
            )
        );
    }

    [HttpPost("{id:guid}/complete")]
    [HasPermission(TasksPermissions.FormRequestsReview)]
    [RateLimit("task.h.form_requests_write")]
    public async Task<IActionResult> Complete(Guid id, CompleteFormRequestRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Forbid();
        if (!TryRowVersion(request.RowVersion, out var rowVersion))
            return BadRequest(RowVersionError);
        return ToActionResult(
            await bus.InvokeAsync<Result<FormRequestResponse>>(
                new CompleteFormRequestCommand(
                    tenantId,
                    userId,
                    await CanViewAllAsync(ct),
                    id,
                    request.InternalNote,
                    rowVersion,
                    DateTime.UtcNow
                ),
                ct
            )
        );
    }

    [HttpPost("{id:guid}/cancel")]
    [HasPermission(TasksPermissions.FormRequestsManage)]
    [RateLimit("task.h.form_requests_write")]
    public async Task<IActionResult> Cancel(Guid id, CancelFormRequestRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Forbid();
        if (!TryRowVersion(request.RowVersion, out var rowVersion))
            return BadRequest(RowVersionError);
        return ToActionResult(
            await bus.InvokeAsync<Result<FormRequestResponse>>(
                new CancelFormRequestCommand(
                    tenantId,
                    userId,
                    await CanViewAllAsync(ct),
                    id,
                    request.Note,
                    rowVersion,
                    DateTime.UtcNow
                ),
                ct
            )
        );
    }

    [HttpPost("{id:guid}/archive")]
    [HasPermission(TasksPermissions.FormRequestsManage)]
    [RateLimit("task.h.form_requests_write")]
    public async Task<IActionResult> Archive(Guid id, ArchiveFormRequestRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Forbid();
        if (!TryRowVersion(request.RowVersion, out var rowVersion))
            return BadRequest(RowVersionError);
        return ToActionResult(
            await bus.InvokeAsync<Result<FormRequestResponse>>(
                new ArchiveFormRequestCommand(
                    tenantId,
                    userId,
                    await CanViewAllAsync(ct),
                    id,
                    request.Note,
                    rowVersion,
                    DateTime.UtcNow
                ),
                ct
            )
        );
    }

    [HttpPost("{id:guid}/link-customer")]
    [HasPermission(TasksPermissions.FormRequestsReview)]
    [RateLimit("task.h.form_requests_write")]
    public async Task<IActionResult> LinkCustomer(Guid id, LinkFormRequestCustomerRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Forbid();
        if (!TryRowVersion(request.RowVersion, out var rowVersion))
            return BadRequest(RowVersionError);
        return ToActionResult(
            await bus.InvokeAsync<Result<FormRequestResponse>>(
                new LinkFormRequestCustomerCommand(
                    tenantId,
                    userId,
                    await CanViewAllAsync(ct),
                    id,
                    request.CustomerId,
                    rowVersion,
                    DateTime.UtcNow
                ),
                ct
            )
        );
    }

    [HttpPut("{id:guid}/responsible")]
    [HasPermission(TasksPermissions.FormRequestsManage)]
    [RateLimit("task.h.form_requests_write")]
    public async Task<IActionResult> ReassignResponsible(
        Guid id,
        ReassignFormRequestResponsibleRequest request,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Forbid();
        if (!TryRowVersion(request.RowVersion, out var rowVersion))
            return BadRequest(RowVersionError);
        return ToActionResult(
            await bus.InvokeAsync<Result<FormRequestResponse>>(
                new ReassignFormRequestResponsibleCommand(
                    tenantId,
                    userId,
                    await CanViewAllAsync(ct),
                    id,
                    request.UserId,
                    rowVersion,
                    DateTime.UtcNow
                ),
                ct
            )
        );
    }

    [HttpPut("{id:guid}/due-date")]
    [HasPermission(TasksPermissions.FormRequestsManage)]
    [RateLimit("task.h.form_requests_write")]
    public async Task<IActionResult> ChangeDueDate(
        Guid id,
        ChangeFormRequestDueDateRequest request,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Forbid();
        if (!TryRowVersion(request.RowVersion, out var rowVersion))
            return BadRequest(RowVersionError);
        return ToActionResult(
            await bus.InvokeAsync<Result<FormRequestResponse>>(
                new ChangeFormRequestDueDateCommand(
                    tenantId,
                    userId,
                    await CanViewAllAsync(ct),
                    id,
                    request.DueAtUtc,
                    rowVersion,
                    DateTime.UtcNow
                ),
                ct
            )
        );
    }

    [HttpPost("{id:guid}/sensitive-answers/{sensitiveAnswerId:guid}/reveal")]
    [HasPermission(TasksPermissions.FormSubmissionsReveal)]
    [RateLimit("task.n.form_sensitive_reveal")]
    public async Task<IActionResult> RevealSensitiveAnswer(Guid id, Guid sensitiveAnswerId, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Forbid();

        var result = await bus.InvokeAsync<Result<RevealedFormSensitiveAnswerResponse>>(
            new RevealFormSensitiveAnswerCommand(
                tenantId,
                userId,
                await CanViewAllAsync(ct),
                id,
                sensitiveAnswerId,
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                UserAgent(),
                Request.Headers[CorrelationIdMiddleware.Header].ToString()
            ),
            ct
        );
        if (result.IsFailure)
            return StatusCode(result.Error.ToHttpStatusCode(), result.Error);

        Response.Headers.CacheControl = "no-store";
        return Ok(result.Value);
    }

    // Arranque manual del alta del cliente; también reintenta el último intento si su resultado lo admite.
    [HttpPost("{id:guid}/actions/create-customer")]
    [HasPermission(TasksPermissions.FormRequestsReview)]
    [RateLimit("task.h.form_requests_write")]
    public async Task<IActionResult> StartCustomerCreation(Guid id, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Forbid();
        if (!await permissions.HasPermissionAsync(User, CustomersPermissions.Manage, ct))
            return Forbid();
        return ToAcceptedResult(
            await bus.InvokeAsync<Result<FormActionRunResponse>>(
                new StartFormCustomerCreationCommand(tenantId, userId, await CanViewAllAsync(ct), id, DateTime.UtcNow),
                ct
            )
        );
    }

    // Solo desde un resultado definitivo que garantiza que no se hizo nada; nunca desde Requested.
    [HttpPost("{id:guid}/actions/{runId:guid}/retry")]
    [HasPermission(TasksPermissions.FormRequestsReview)]
    [RateLimit("task.h.form_requests_write")]
    public async Task<IActionResult> RetryAction(Guid id, Guid runId, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Forbid();
        if (!await permissions.HasPermissionAsync(User, CustomersPermissions.Manage, ct))
            return Forbid();
        return ToAcceptedResult(
            await bus.InvokeAsync<Result<FormActionRunResponse>>(
                new RetryFormActionRunCommand(tenantId, userId, await CanViewAllAsync(ct), id, runId, DateTime.UtcNow),
                ct
            )
        );
    }

    // Reusa el cliente ya creado: nunca vuelve a llamar a Customer. Idempotente.
    [HttpPost("{id:guid}/customer-link/retry")]
    [HasPermission(TasksPermissions.FormRequestsReview)]
    [RateLimit("task.h.form_requests_write")]
    public async Task<IActionResult> RetryCustomerLink(Guid id, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Forbid();
        return ToActionResult(
            await bus.InvokeAsync<Result<FormRequestResponse>>(
                new RetryFormCustomerLinkCommand(tenantId, userId, await CanViewAllAsync(ct), id, DateTime.UtcNow),
                ct
            )
        );
    }

    private IActionResult ToAcceptedResult<T>(Result<T> result) =>
        result.IsFailure ? StatusCode(result.Error.ToHttpStatusCode(), result.Error) : Accepted(result.Value);

    private string? UserAgent()
    {
        var value = Request.Headers.UserAgent.ToString();
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return value.Length <= 512 ? value : value[..512];
    }

    private Task<bool> CanViewAllAsync(CancellationToken ct) =>
        permissions.HasPermissionAsync(User, CustomersPermissions.ViewAll, ct);

    private IActionResult ToActionResult<T>(Result<T> result) =>
        result.IsFailure ? StatusCode(result.Error.ToHttpStatusCode(), result.Error) : Ok(result.Value);

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
