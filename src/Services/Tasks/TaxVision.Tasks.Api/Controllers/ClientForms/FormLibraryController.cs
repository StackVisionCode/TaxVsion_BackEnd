using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Tasks.Api.Authorization;
using TaxVision.Tasks.Api.Filters;
using TaxVision.Tasks.ClientForms.Application.Library;
using TaxVision.Tasks.ClientForms.Application.Library.Commands;
using TaxVision.Tasks.ClientForms.Application.Library.Queries;
using TaxVision.Tasks.ClientForms.Application.Templates.Responses;
using Wolverine;

namespace TaxVision.Tasks.Api.Controllers.ClientForms;

// Biblioteca TaxVision vista por los tenants: explorar, previsualizar y clonar (D-17).
[ApiController]
[ClientFormsEndpoint]
[Route("tasks/forms/library")]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class FormLibraryController(IMessageBus bus, FormPublisherCapabilitiesResolver capabilities)
    : ControllerBase
{
    [HttpGet]
    [HasPermission(TasksPermissions.FormTemplatesRead)]
    [RateLimit("task.f.form_read")]
    public async Task<IActionResult> List(CancellationToken ct) =>
        Ok(await bus.InvokeAsync<IReadOnlyList<LibraryTemplateSummaryResponse>>(new ListLibraryTemplatesQuery(), ct));

    [HttpGet("{key}")]
    [HasPermission(TasksPermissions.FormTemplatesRead)]
    [RateLimit("task.f.form_read")]
    public async Task<IActionResult> Preview(string key, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<LibraryTemplatePreviewResponse>>(
            new GetLibraryTemplateQuery(key),
            ct
        );
        return result.IsFailure ? StatusCode(result.Error.ToHttpStatusCode(), result.Error) : Ok(result.Value);
    }

    [HttpPost("{key}/clone")]
    [HasPermission(TasksPermissions.FormTemplatesManage)]
    [RateLimit("task.h.form_templates_write")]
    public async Task<IActionResult> Clone(string key, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Forbid();
        var result = await bus.InvokeAsync<Result<FormTemplateDetailResponse>>(
            new CloneLibraryTemplateCommand(
                tenantId,
                userId,
                key,
                await capabilities.CanConfigureActionsAsync(User, ct)
            ),
            ct
        );
        return result.IsFailure
            ? StatusCode(result.Error.ToHttpStatusCode(), result.Error)
            : CreatedAtAction("Get", "FormTemplates", new { id = result.Value.Id }, result.Value);
    }
}
