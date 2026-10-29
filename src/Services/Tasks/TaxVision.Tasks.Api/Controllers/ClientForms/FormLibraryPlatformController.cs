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
using TaxVision.Tasks.ClientForms.Application.Library.Commands;
using TaxVision.Tasks.ClientForms.Application.Library.Queries;
using TaxVision.Tasks.ClientForms.Application.Templates.Responses;
using Wolverine;

namespace TaxVision.Tasks.Api.Controllers.ClientForms;

// Gestión de la biblioteca por PlatformAdmin: registrar revisión, (des)listar y exportar. PlatformOnly.
[ApiController]
[ClientFormsEndpoint]
[Route("tasks/forms")]
[AllowActorTypes(ActorType.PlatformAdmin)]
[HasPermission(TasksPermissions.FormsLibraryManage)]
[SkipClientFormsAccess]
public sealed class FormLibraryPlatformController(IMessageBus bus) : ControllerBase
{
    [HttpPost("library/{templateId:guid}/reviews")]
    [RateLimit("task.h.form_templates_write")]
    public async Task<IActionResult> RecordReview(
        Guid templateId,
        RecordLibraryReviewRequest request,
        CancellationToken ct
    )
    {
        if (!User.TryGetUserId(out var userId))
            return Forbid();
        var result = await bus.InvokeAsync<Result<FormTemplateDetailResponse>>(
            new RecordLibraryReviewCommand(
                userId,
                templateId,
                request.ReviewedVersionNumber,
                request.ReviewerRole,
                request.Note
            ),
            ct
        );
        return result.IsFailure ? StatusCode(result.Error.ToHttpStatusCode(), result.Error) : Ok(result.Value);
    }

    [HttpPut("library/{templateId:guid}/listing")]
    [RateLimit("task.h.form_templates_write")]
    public async Task<IActionResult> SetListing(Guid templateId, SetLibraryListingRequest request, CancellationToken ct)
    {
        if (!User.TryGetUserId(out var userId))
            return Forbid();
        var result = await bus.InvokeAsync<Result<FormTemplateDetailResponse>>(
            new SetLibraryListingCommand(userId, templateId, request.LibraryKey, request.IsListed),
            ct
        );
        return result.IsFailure ? StatusCode(result.Error.ToHttpStatusCode(), result.Error) : Ok(result.Value);
    }

    [HttpGet("templates/{templateId:guid}/export")]
    [RateLimit("task.f.form_read")]
    public async Task<IActionResult> Export(Guid templateId, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<string>>(new ExportFormTemplateQuery(templateId), ct);
        return result.IsFailure
            ? StatusCode(result.Error.ToHttpStatusCode(), result.Error)
            : Content(result.Value, "application/json");
    }
}
