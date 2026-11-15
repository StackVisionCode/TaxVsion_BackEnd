using System.Globalization;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Campaigns.Application.Settings.SmsWindow;
using Wolverine;

namespace TaxVision.Campaigns.Api.Controllers;

public sealed record SetCampaignSmsWindowRequest(string? Opens, string? Closes);

/// <summary>
/// P32/P33: horario de envío de SMS de campañas de la oficina. Verlo pide <c>campaigns.view</c>; cambiarlo,
/// <c>campaigns.manage</c>. TenantId siempre del JWT.
/// </summary>
[ApiController]
[Route("campaign-settings/sms-window")]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class CampaignSettingsController(IMessageBus bus) : ControllerBase
{
    public static readonly Error TimeInvalid = new(
        "Campaigns.SmsWindow.TimeInvalid",
        "Times must use the HH:mm format, like 09:00."
    );

    [HttpGet]
    [HasPermission(CampaignsPermissions.View)]
    [RateLimit("campaigns.f.get")]
    [ProducesResponseType<CampaignSmsWindowResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();
        return Ok(await bus.InvokeAsync<CampaignSmsWindowResponse>(new GetCampaignSmsWindowQuery(tenantId), ct));
    }

    [HttpPut]
    [HasPermission(CampaignsPermissions.Manage)]
    [RateLimit("campaigns.g.create")]
    [ProducesResponseType<CampaignSmsWindowResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Set(SetCampaignSmsWindowRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();
        if (!TryTime(request.Opens, out var opens) || !TryTime(request.Closes, out var closes))
            return BadRequest(TimeInvalid);

        var result = await bus.InvokeAsync<Result<CampaignSmsWindowResponse>>(
            new SetCampaignSmsWindowCommand(tenantId, userId, opens, closes, DateTime.UtcNow),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpDelete]
    [HasPermission(CampaignsPermissions.Manage)]
    [RateLimit("campaigns.g.create")]
    [ProducesResponseType<CampaignSmsWindowResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Reset(CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();
        return Ok(await bus.InvokeAsync<CampaignSmsWindowResponse>(new ResetCampaignSmsWindowCommand(tenantId), ct));
    }

    private static bool TryTime(string? value, out TimeOnly time) =>
        TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
}
