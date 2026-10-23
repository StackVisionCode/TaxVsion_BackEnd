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
using TaxVision.Tasks.ClientForms.Application.Consents;
using Wolverine;

namespace TaxVision.Tasks.Api.Controllers.ClientForms;

[ApiController]
[ClientFormsEndpoint]
[Route("tasks/forms/consent-notices")]
[AllowActorTypes(ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class ConsentNoticesController(IMessageBus bus) : ControllerBase
{
    [HttpGet]
    // Lectura para el selector del builder: quien arma plantillas lista avisos; gestionarlos sigue siendo admin.
    [AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
    [HasAnyPermission(TasksPermissions.FormConsentsManage, TasksPermissions.FormTemplatesManage)]
    [RateLimit("task.f.form_read")]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Forbid();
        return Ok(
            await bus.InvokeAsync<IReadOnlyList<ConsentNoticeResponse>>(new ListConsentNoticesQuery(tenantId), ct)
        );
    }

    [HttpGet("{id:guid}")]
    [AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
    [HasAnyPermission(TasksPermissions.FormConsentsManage, TasksPermissions.FormTemplatesManage)]
    [RateLimit("task.f.form_read")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Forbid();
        return ToActionResult(
            await bus.InvokeAsync<Result<ConsentNoticeResponse>>(new GetConsentNoticeQuery(tenantId, id), ct)
        );
    }

    [HttpPost]
    [HasPermission(TasksPermissions.FormConsentsManage)]
    [RateLimit("task.h.form_templates_write")]
    public async Task<IActionResult> Create(CreateConsentNoticeRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Forbid();
        var result = await bus.InvokeAsync<Result<ConsentNoticeResponse>>(
            new CreateConsentNoticeCommand(
                tenantId,
                userId,
                request.Key,
                request.Title,
                request.Purpose,
                DateTime.UtcNow
            ),
            ct
        );
        return result.IsFailure
            ? StatusCode(result.Error.ToHttpStatusCode(), result.Error)
            : CreatedAtAction(nameof(Get), new { id = result.Value.Id }, result.Value);
    }

    [HttpPut("{id:guid}")]
    [HasPermission(TasksPermissions.FormConsentsManage)]
    [RateLimit("task.h.form_templates_write")]
    public async Task<IActionResult> Update(Guid id, UpdateConsentNoticeRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Forbid();
        if (!TryDecode(request.RowVersion, out var rowVersion))
            return BadRequest(new Error("ConsentNotice.InvalidRowVersion", "The row version is invalid."));
        return ToActionResult(
            await bus.InvokeAsync<Result<ConsentNoticeResponse>>(
                new UpdateConsentNoticeCommand(tenantId, userId, id, request.Title, rowVersion),
                ct
            )
        );
    }

    [HttpPost("{id:guid}/versions")]
    [HasPermission(TasksPermissions.FormConsentsManage)]
    [RateLimit("task.h.form_templates_write")]
    public async Task<IActionResult> Publish(Guid id, PublishConsentNoticeVersionRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Forbid();
        if (!TryDecode(request.RowVersion, out var rowVersion))
            return BadRequest(new Error("ConsentNotice.InvalidRowVersion", "The row version is invalid."));
        return ToActionResult(
            await bus.InvokeAsync<Result<ConsentNoticeResponse>>(
                new PublishConsentNoticeVersionCommand(
                    tenantId,
                    userId,
                    id,
                    request.Language,
                    request.Body,
                    rowVersion,
                    DateTime.UtcNow
                ),
                ct
            )
        );
    }

    [HttpPost("{id:guid}/archive")]
    [HasPermission(TasksPermissions.FormConsentsManage)]
    [RateLimit("task.h.form_templates_write")]
    public async Task<IActionResult> Archive(Guid id, ArchiveConsentNoticeRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Forbid();
        if (!TryDecode(request.RowVersion, out var rowVersion))
            return BadRequest(new Error("ConsentNotice.InvalidRowVersion", "The row version is invalid."));
        var result = await bus.InvokeAsync<Result>(
            new ArchiveConsentNoticeCommand(tenantId, userId, id, rowVersion, DateTime.UtcNow),
            ct
        );
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
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
