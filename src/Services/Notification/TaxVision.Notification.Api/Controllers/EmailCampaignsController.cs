using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Notification.Api.Common;
using TaxVision.Notification.Application.Email.Campaigns;
using TaxVision.Notification.Application.Email.Campaigns.Commands;
using TaxVision.Notification.Application.Email.Campaigns.Queries;
using TaxVision.Notification.Application.Email.Sending;
using TaxVision.Notification.Domain.Emailing.Campaigns;
using Wolverine;

namespace TaxVision.Notification.Api.Controllers;

/// <summary>
/// Campañas de correo de un tenant. Draft → programar (captura la plantilla) → el scheduler encola el
/// fan-out. Nunca mezcla clientes de tenants distintos; el envío masivo es por cola/evento.
/// </summary>
[ApiController]
[Route("notifications/email/campaigns")]
[Authorize]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class EmailCampaignsController(IMessageBus bus) : ControllerBase
{
    public sealed record CreateCampaignRequest(
        string Name,
        CampaignType Type,
        Guid TemplateId,
        IReadOnlyList<CampaignRecipientInput> Recipients
    );

    public sealed record ScheduleCampaignRequest(DateTime? ScheduledAtUtc = null);

    public sealed record SendTestRequest(string ToEmail, Dictionary<string, string?>? Variables = null);

    [HttpPost]
    [HasPermission(NotificationPermissions.CampaignManage)]
    [ProducesResponseType<EmailCampaignResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] CreateCampaignRequest request, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var command = new CreateEmailCampaignCommand(
            tenantId,
            User.TryGetUserId(out var userId) ? userId : null,
            request.Name,
            request.Type,
            request.TemplateId,
            request.Recipients
        );
        var result = await bus.InvokeAsync<Result<EmailCampaignResponse>>(command, ct);
        return result.IsSuccess
            ? Created($"/notifications/email/campaigns/{result.Value.Id}", result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet]
    [HasPermission(NotificationPermissions.CampaignView)]
    [ProducesResponseType<PagedResult<EmailCampaignResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] CampaignStatus? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int size = 20,
        CancellationToken ct = default
    )
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<PagedResult<EmailCampaignResponse>>>(
            new GetEmailCampaignsQuery(tenantId, status, page, size),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet("{id:guid}")]
    [HasPermission(NotificationPermissions.CampaignView)]
    [ProducesResponseType<EmailCampaignResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<EmailCampaignResponse>>(
            new GetEmailCampaignByIdQuery(id, tenantId),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("{id:guid}/schedule")]
    [HasPermission(NotificationPermissions.CampaignManage)]
    [ProducesResponseType<EmailCampaignResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Schedule(Guid id, [FromBody] ScheduleCampaignRequest request, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<EmailCampaignResponse>>(
            new ScheduleEmailCampaignCommand(id, tenantId, request.ScheduledAtUtc),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("{id:guid}/send-test")]
    [HasPermission(NotificationPermissions.CampaignManage)]
    [ProducesResponseType<OutboundEmailResponse>(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> SendTest(Guid id, [FromBody] SendTestRequest request, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<OutboundEmailResponse>>(
            new SendCampaignTestCommand(id, tenantId, request.ToEmail, request.Variables),
            ct
        );
        return result.IsSuccess ? Accepted(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("{id:guid}/cancel")]
    [HasPermission(NotificationPermissions.CampaignManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(new CancelEmailCampaignCommand(id, tenantId), ct);
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
