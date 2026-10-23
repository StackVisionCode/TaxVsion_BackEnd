using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Tasks.Api.Filters;
using TaxVision.Tasks.Api.Requests.ClientForms;
using TaxVision.Tasks.ClientForms.Application.Participants.Commands;
using Wolverine;

namespace TaxVision.Tasks.Api.Controllers.ClientForms;

[ApiController]
[ClientFormsEndpoint]
[Route("tasks/forms/intake-links")]
[AllowActorTypes(ActorType.TenantAdmin, ActorType.PlatformAdmin)]
[HasPermission(TasksPermissions.FormIntakeLinksManage)]
public sealed class FormIntakeLinksController(IMessageBus bus) : ControllerBase
{
    [HttpGet]
    [RateLimit("task.f.form_read")]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Forbid();
        return ToActionResult(
            await bus.InvokeAsync<Result<IReadOnlyList<FormIntakeLinkResponse>>>(
                new ListFormIntakeLinksQuery(tenantId),
                ct
            )
        );
    }

    [HttpGet("{id:guid}")]
    [RateLimit("task.f.form_read")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Forbid();
        return ToActionResult(
            await bus.InvokeAsync<Result<FormIntakeLinkResponse>>(new GetFormIntakeLinkQuery(tenantId, id), ct)
        );
    }

    [HttpGet("{id:guid}/token")]
    [RateLimit("task.h.form_requests_write")]
    public async Task<IActionResult> GetToken(Guid id, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Forbid();
        return ToActionResult(
            await bus.InvokeAsync<Result<FormIntakeLinkTokenResponse>>(
                new GetFormIntakeLinkTokenQuery(tenantId, userId, id),
                ct
            )
        );
    }

    [HttpPost]
    [RateLimit("task.h.form_requests_write")]
    public async Task<IActionResult> Create(CreateFormIntakeLinkRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Forbid();
        var result = await bus.InvokeAsync<Result<FormIntakeLinkTokenResponse>>(
            new CreateFormIntakeLinkCommand(
                tenantId,
                userId,
                request.Name,
                request.FormTemplateId,
                request.FormTemplateVersionId,
                request.EntryNoticeKey,
                request.ExpiresAtUtc,
                request.MaxSubmissions,
                request.ResponsibleUserId,
                DateTime.UtcNow
            ),
            ct
        );
        return result.IsFailure
            ? StatusCode(result.Error.ToHttpStatusCode(), result.Error)
            : CreatedAtAction(nameof(Get), new { id = result.Value.Link.Id }, result.Value);
    }

    // Apagar un enlace se permite aunque el módulo esté cerrado para el tenant.
    [HttpPost("{id:guid}/pause")]
    [RateLimit("task.h.form_requests_write")]
    [SkipClientFormsAccess]
    public Task<IActionResult> Pause(Guid id, ChangeFormIntakeLinkRequest request, CancellationToken ct) =>
        Change(id, FormIntakeLinkAction.Pause, null, request.RowVersion, ct);

    [HttpPost("{id:guid}/resume")]
    [RateLimit("task.h.form_requests_write")]
    public Task<IActionResult> Resume(Guid id, ChangeFormIntakeLinkRequest request, CancellationToken ct) =>
        Change(id, FormIntakeLinkAction.Resume, null, request.RowVersion, ct);

    [HttpPost("{id:guid}/revoke")]
    [RateLimit("task.h.form_requests_write")]
    [SkipClientFormsAccess]
    public Task<IActionResult> Revoke(Guid id, ChangeFormIntakeLinkRequest request, CancellationToken ct) =>
        Change(id, FormIntakeLinkAction.Revoke, null, request.RowVersion, ct);

    [HttpPost("{id:guid}/regenerate-token")]
    [RateLimit("task.h.form_requests_write")]
    public Task<IActionResult> RegenerateToken(Guid id, ChangeFormIntakeLinkRequest request, CancellationToken ct) =>
        Change(id, FormIntakeLinkAction.RegenerateToken, null, request.RowVersion, ct);

    [HttpPost("{id:guid}/pin-version")]
    [RateLimit("task.h.form_requests_write")]
    public Task<IActionResult> PinVersion(Guid id, PinFormIntakeLinkVersionRequest request, CancellationToken ct) =>
        Change(id, FormIntakeLinkAction.PinVersion, request.FormTemplateVersionId, request.RowVersion, ct);

    private async Task<IActionResult> Change(
        Guid id,
        FormIntakeLinkAction action,
        Guid? versionId,
        string rowVersion,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Forbid();
        if (!TryDecode(rowVersion, out var decoded))
            return BadRequest(new Error("FormIntakeLink.InvalidRowVersion", "The row version is invalid."));
        return ToActionResult(
            await bus.InvokeAsync<Result<FormIntakeLinkTokenResponse>>(
                new UpdateFormIntakeLinkCommand(tenantId, userId, id, action, versionId, decoded, DateTime.UtcNow),
                ct
            )
        );
    }

    private static bool TryDecode(string value, out byte[] decoded)
    {
        try
        {
            decoded = Convert.FromBase64String(value);
            return decoded.Length > 0;
        }
        catch (FormatException)
        {
            decoded = [];
            return false;
        }
    }

    private IActionResult ToActionResult<T>(Result<T> result) =>
        result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
}
