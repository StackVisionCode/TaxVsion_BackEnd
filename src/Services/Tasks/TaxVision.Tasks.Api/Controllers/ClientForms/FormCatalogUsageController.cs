using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Tasks.Api.Filters;
using TaxVision.Tasks.ClientForms.Application.Mapping.Queries;
using TaxVision.Tasks.ClientForms.Application.Mapping.Responses;
using Wolverine;

namespace TaxVision.Tasks.Api.Controllers.ClientForms;

[ApiController]
[ClientFormsEndpoint]
[Route("tasks/forms/platform/catalog-usage")]
[AllowActorTypes(ActorType.PlatformAdmin)]
[HasPermission(TasksPermissions.FormsPlatformManage)]
[SkipClientFormsAccess]
public sealed class FormCatalogUsageController(IMessageBus bus) : ControllerBase
{
    [HttpGet]
    [RateLimit("task.f.form_read")]
    public async Task<IActionResult> Get([FromQuery] string? targetKey, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<FormCatalogUsageResponse>>(
            new GetFormCatalogUsageQuery(targetKey),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
