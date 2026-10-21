using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Tasks.Api.Filters;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;

namespace TaxVision.Tasks.Api.Controllers.ClientForms;

[ApiController]
[Route("tasks/forms/availability")]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin)]
[SkipClientFormsAccess]
public sealed class FormAvailabilityController(IClientFormsAvailabilityEvaluator availability) : ControllerBase
{
    [HttpGet]
    [HasPermission(TasksPermissions.FormTemplatesRead)]
    [RateLimit("task.f.form_read")]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Forbid();
        return Ok(await availability.EvaluateAsync(tenantId, ct));
    }
}
