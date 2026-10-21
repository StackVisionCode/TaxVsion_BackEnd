using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Permissions;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Tasks.Api.Requests.ClientForms;
using TaxVision.Tasks.ClientForms.Application.Requests;
using TaxVision.Tasks.ClientForms.Application.Requests.Commands;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using Wolverine;

namespace TaxVision.Tasks.Api.Controllers.ClientForms;

[ApiController]
[Route("tasks/forms/requests")]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class FormRequestsController(IMessageBus bus, IUserPermissionsSource permissions) : ControllerBase
{
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
            : CreatedAtAction(nameof(Assign), new { id = result.Value.Id }, result.Value);
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
        return ToActionResult(await bus.InvokeAsync<Result<FormRequestResponse>>(
            new ReturnFormRequestCommand(tenantId, userId, await CanViewAllAsync(ct), id, request.Message, comments, rowVersion, DateTime.UtcNow),
            ct
        ));
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
        return ToActionResult(await bus.InvokeAsync<Result<FormRequestResponse>>(
            new CompleteFormRequestCommand(tenantId, userId, await CanViewAllAsync(ct), id, request.InternalNote, rowVersion, DateTime.UtcNow),
            ct
        ));
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
        return ToActionResult(await bus.InvokeAsync<Result<FormRequestResponse>>(
            new CancelFormRequestCommand(tenantId, userId, await CanViewAllAsync(ct), id, request.Note, rowVersion, DateTime.UtcNow),
            ct
        ));
    }

    [HttpPut("{id:guid}/due-date")]
    [HasPermission(TasksPermissions.FormRequestsManage)]
    [RateLimit("task.h.form_requests_write")]
    public async Task<IActionResult> ChangeDueDate(Guid id, ChangeFormRequestDueDateRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Forbid();
        if (!TryRowVersion(request.RowVersion, out var rowVersion))
            return BadRequest(RowVersionError);
        return ToActionResult(await bus.InvokeAsync<Result<FormRequestResponse>>(
            new ChangeFormRequestDueDateCommand(tenantId, userId, await CanViewAllAsync(ct), id, request.DueAtUtc, rowVersion, DateTime.UtcNow),
            ct
        ));
    }

    private Task<bool> CanViewAllAsync(CancellationToken ct) =>
        permissions.HasPermissionAsync(User, CustomersPermissions.ViewAll, ct);

    private IActionResult ToActionResult(Result<FormRequestResponse> result) =>
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
