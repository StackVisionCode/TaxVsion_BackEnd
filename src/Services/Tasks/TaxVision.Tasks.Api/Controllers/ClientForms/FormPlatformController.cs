using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Tasks.Api.Filters;
using TaxVision.Tasks.Api.Requests.ClientForms;
using TaxVision.Tasks.ClientForms.Application.Settings;
using Wolverine;

namespace TaxVision.Tasks.Api.Controllers.ClientForms;

[ApiController]
[ClientFormsEndpoint]
[Route("tasks/forms/platform/tenants")]
[AllowActorTypes(ActorType.PlatformAdmin)]
[HasPermission(TasksPermissions.FormsPlatformManage)]
[SkipClientFormsAccess]
public sealed class FormPlatformController(IMessageBus bus) : ControllerBase
{
    [HttpGet]
    [RateLimit("task.f.form_read")]
    public async Task<IActionResult> List(CancellationToken ct) =>
        Ok(await bus.InvokeAsync<IReadOnlyList<FormModuleSettingsResponse>>(new ListFormModuleSettingsQuery(), ct));

    [HttpPut("{tenantId:guid}")]
    [RateLimit("task.h.form_templates_write")]
    public async Task<IActionResult> Set(Guid tenantId, SetFormModuleAvailabilityRequest request, CancellationToken ct)
    {
        if (!User.TryGetUserId(out var userId))
            return Forbid();
        var result = await bus.InvokeAsync<Result<FormModuleSettingsResponse>>(
            new SetFormModuleAvailabilityCommand(
                tenantId,
                userId,
                request.Enabled,
                request.Note,
                request.ExternalParticipantsEnabled,
                request.IntakeLinksEnabled,
                request.AutoCustomerCreationEnabled,
                request.SignatureStepEnabled,
                request.PushNotificationsEnabled
            ),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
