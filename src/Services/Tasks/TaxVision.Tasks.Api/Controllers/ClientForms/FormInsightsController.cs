using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.Permissions;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Tasks.Api.Filters;
using TaxVision.Tasks.ClientForms.Application.Insights;
using Wolverine;

namespace TaxVision.Tasks.Api.Controllers.ClientForms;

// Insights de la oficina: lectura agregada con el mismo permiso y ABAC que la bandeja de requests.
[ApiController]
[ClientFormsEndpoint]
[Route("tasks/forms/insights")]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
[HasPermission(TasksPermissions.FormRequestsRead)]
public sealed class FormInsightsController(IMessageBus bus, IUserPermissionsSource permissions) : ControllerBase
{
    [HttpGet("summary")]
    [RateLimit("task.f.form_read")]
    public async Task<IActionResult> Summary(
        [FromQuery] Guid? templateId,
        [FromQuery] int period = FormInsightsWindow.DefaultDays,
        [FromQuery] int utcOffsetMinutes = 0,
        CancellationToken ct = default
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Forbid();
        if (!FormInsightsWindow.IsAllowed(period))
            return BadRequest(InvalidPeriod);
        var result = await bus.InvokeAsync<FormInsightsSummaryResponse>(
            new FormInsightsSummaryQuery(
                tenantId,
                userId,
                await CanViewAllAsync(ct),
                period,
                templateId,
                utcOffsetMinutes
            ),
            ct
        );
        return Ok(result);
    }

    [HttpGet("templates")]
    [RateLimit("task.f.form_read")]
    public async Task<IActionResult> Templates(
        [FromQuery] int period = FormInsightsWindow.DefaultDays,
        [FromQuery] int utcOffsetMinutes = 0,
        [FromQuery] int page = 1,
        [FromQuery] int size = FormInsightsTemplatesHandler.DefaultSize,
        CancellationToken ct = default
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Forbid();
        if (!FormInsightsWindow.IsAllowed(period))
            return BadRequest(InvalidPeriod);
        var result = await bus.InvokeAsync<PagedResult<FormInsightsTemplateRowResponse>>(
            new FormInsightsTemplatesQuery(
                tenantId,
                userId,
                await CanViewAllAsync(ct),
                period,
                utcOffsetMinutes,
                page,
                size
            ),
            ct
        );
        return Ok(result);
    }

    private Task<bool> CanViewAllAsync(CancellationToken ct) =>
        permissions.HasPermissionAsync(User, CustomersPermissions.ViewAll, ct);

    private static readonly Error InvalidPeriod = new(
        "FormInsights.InvalidPeriod",
        "The period must be 7, 30 or 90 days."
    );
}
