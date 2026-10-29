using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Tasks.Api.Authorization;
using TaxVision.Tasks.Api.Filters;
using TaxVision.Tasks.ClientForms.Application.Mapping.Queries;
using TaxVision.Tasks.ClientForms.Application.Mapping.Responses;
using Wolverine;

namespace TaxVision.Tasks.Api.Controllers.ClientForms;

[ApiController]
[ClientFormsEndpoint]
[Route("tasks/forms/mapping-catalog")]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class FormMappingCatalogController(IMessageBus bus, FormPublisherCapabilitiesResolver capabilities)
    : ControllerBase
{
    [HttpGet]
    [HasPermission(TasksPermissions.FormTemplatesConfigureActions)]
    [RateLimit("task.f.form_read")]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out _, out _))
            return Forbid();
        var result = await bus.InvokeAsync<FormMappingCatalogResponse>(
            new GetFormMappingCatalogQuery(await capabilities.ResolveAsync(User, ct)),
            ct
        );
        return Ok(result);
    }
}
