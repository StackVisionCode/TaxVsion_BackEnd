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
using TaxVision.Campaigns.Application.Templates;
using TaxVision.Campaigns.Application.Templates.Commands;
using TaxVision.Campaigns.Application.Templates.Queries;
using Wolverine;

namespace TaxVision.Campaigns.Api.Controllers;

/// <summary>
/// Plantillas de campaña reutilizables (multicanal) — staff únicamente. TenantId SIEMPRE del JWT.
/// Permiso <c>campaigns.manage</c> para escrituras, <c>campaigns.view</c> para lecturas.
/// </summary>
[ApiController]
[Route("campaign-templates")]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class CampaignTemplatesController(IMessageBus bus) : ControllerBase
{
    private const int DefaultSize = 20;
    private const int MaxSize = 100;

    private static IReadOnlyList<TemplateContentInput>? MapContents(IReadOnlyList<CampaignContentRequest>? contents) =>
        contents?.Select(c => new TemplateContentInput(c.Channel.ToString(), c.Subject, c.Title, c.Body)).ToList();

    [HttpPost]
    [HasPermission(CampaignsPermissions.Manage)]
    [RateLimit("campaigns.g.create")]
    [ProducesResponseType<CampaignTemplateResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CampaignTemplateRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<CampaignTemplateResponse>>(
            new CreateCampaignTemplateCommand(
                tenantId,
                userId,
                request.Name,
                request.Description,
                request.ToChannelsFlag(),
                MapContents(request.Contents)
            ),
            ct
        );
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetById), new { id = result.Value.Id }, result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet]
    [HasPermission(CampaignsPermissions.View)]
    [RateLimit("campaigns.f.list")]
    [ProducesResponseType<PagedResult<CampaignTemplateResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] int page, [FromQuery] int size, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<PagedResult<CampaignTemplateResponse>>(
            new ListCampaignTemplatesQuery(tenantId, NormalizePage(page), NormalizeSize(size)),
            ct
        );
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [HasPermission(CampaignsPermissions.View)]
    [RateLimit("campaigns.f.get")]
    [ProducesResponseType<CampaignTemplateResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<CampaignTemplateResponse>>(
            new GetCampaignTemplateQuery(tenantId, id),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPut("{id:guid}")]
    [HasPermission(CampaignsPermissions.Manage)]
    [RateLimit("campaigns.g.create")]
    [ProducesResponseType<CampaignTemplateResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, CampaignTemplateRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<CampaignTemplateResponse>>(
            new UpdateCampaignTemplateCommand(
                tenantId,
                id,
                request.Name,
                request.Description,
                request.ToChannelsFlag(),
                MapContents(request.Contents)
            ),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpDelete("{id:guid}")]
    [HasPermission(CampaignsPermissions.Manage)]
    [RateLimit("campaigns.g.create")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(new DeleteCampaignTemplateCommand(tenantId, id), ct);
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    private static int NormalizePage(int page) => page < 1 ? 1 : page;

    private static int NormalizeSize(int size) => size < 1 ? DefaultSize : Math.Min(size, MaxSize);
}
