using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Campaigns.Api.Requests;
using TaxVision.Campaigns.Application.Campaigns;
using TaxVision.Campaigns.Application.Senders;
using TaxVision.Campaigns.Application.Senders.Commands;
using TaxVision.Campaigns.Application.Senders.Queries;
using TaxVision.Campaigns.Domain.Campaigns;
using Wolverine;

namespace TaxVision.Campaigns.Api.Controllers;

/// <summary>
/// Remitentes por canal ("quién envía") — staff únicamente. TenantId SIEMPRE del JWT. Permiso
/// <c>campaigns.manage</c>. Guarda solo la referencia opaca del remitente; los secretos de proveedor
/// viven en el ejecutor, nunca acá.
/// </summary>
[ApiController]
[Route("sender-profiles")]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class SenderProfilesController(IMessageBus bus) : ControllerBase
{
    private const int DefaultSize = 20;

    [HttpPost]
    [HasPermission(CampaignsPermissions.Manage)]
    [RateLimit("campaigns.g.create")]
    [ProducesResponseType<SenderProfileResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateSenderProfileRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<SenderProfileResponse>>(
            new CreateSenderProfileCommand(tenantId, request.Channel, request.Name, request.SenderRef),
            ct
        );
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetById), new { id = result.Value.Id }, result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet]
    [HasPermission(CampaignsPermissions.Manage)]
    [RateLimit("campaigns.f.list")]
    [ProducesResponseType<PagedResult<SenderProfileResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] CampaignChannel? channel,
        [FromQuery] int page,
        [FromQuery] int size,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<PagedResult<SenderProfileResponse>>(
            new ListSenderProfilesQuery(tenantId, channel, NormalizePage(page), NormalizeSize(size)),
            ct
        );
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [HasPermission(CampaignsPermissions.Manage)]
    [RateLimit("campaigns.f.get")]
    [ProducesResponseType<SenderProfileResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<SenderProfileResponse>>(new GetSenderProfileQuery(tenantId, id), ct);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPut("{id:guid}")]
    [HasPermission(CampaignsPermissions.Manage)]
    [RateLimit("campaigns.g.create")]
    [ProducesResponseType<SenderProfileResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, UpdateSenderProfileRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<SenderProfileResponse>>(
            new UpdateSenderProfileCommand(tenantId, id, request.Name, request.SenderRef),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("{id:guid}/status")]
    [HasPermission(CampaignsPermissions.Manage)]
    [RateLimit("campaigns.g.create")]
    [ProducesResponseType<SenderProfileResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SetStatus(Guid id, SetSenderProfileStatusRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<SenderProfileResponse>>(
            new SetSenderProfileStatusCommand(tenantId, id, request.Active),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    private static int NormalizePage(int page) => page < 1 ? 1 : page;

    private static int NormalizeSize(int size) => size is < 1 or > 100 ? DefaultSize : size;
}
