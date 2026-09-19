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
using TaxVision.Campaigns.Application.Contacts;
using TaxVision.Campaigns.Application.Contacts.Commands;
using TaxVision.Campaigns.Application.Contacts.Queries;
using Wolverine;

namespace TaxVision.Campaigns.Api.Controllers;

/// <summary>
/// Libreta de contactos de campañas — staff únicamente. TenantId SIEMPRE del JWT, nunca del body.
/// Permiso <c>campaigns.manage</c>. Contactos propios (pueden no ser clientes), con opt-out por canal.
/// </summary>
[ApiController]
[Route("contacts")]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class ContactsController(IMessageBus bus) : ControllerBase
{
    private const int DefaultSize = 20;

    [HttpPost]
    [HasPermission(CampaignsPermissions.Manage)]
    [RateLimit("campaigns.g.create")]
    [ProducesResponseType<ContactResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateContactRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<ContactResponse>>(
            new CreateContactCommand(tenantId, request.Name, request.Email, request.PhoneE164),
            ct
        );
        return result.IsSuccess
            ? CreatedAtAction(nameof(List), new { }, result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet]
    [HasPermission(CampaignsPermissions.Manage)]
    [RateLimit("campaigns.f.list")]
    [ProducesResponseType<PagedResult<ContactResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] int page, [FromQuery] int size, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<PagedResult<ContactResponse>>(
            new ListContactsQuery(tenantId, NormalizePage(page), NormalizeSize(size)),
            ct
        );
        return Ok(result);
    }

    [HttpPut("{id:guid}")]
    [HasPermission(CampaignsPermissions.Manage)]
    [RateLimit("campaigns.g.create")]
    [ProducesResponseType<ContactResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, UpdateContactRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<ContactResponse>>(
            new UpdateContactCommand(tenantId, id, request.Name, request.Email, request.PhoneE164),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("{id:guid}/opt-out")]
    [HasPermission(CampaignsPermissions.Manage)]
    [RateLimit("campaigns.g.create")]
    [ProducesResponseType<ContactResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SetOptOut(Guid id, SetContactOptOutRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<ContactResponse>>(
            new SetContactOptOutCommand(tenantId, id, request.ToFlag(), request.OptedOut),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    private static int NormalizePage(int page) => page < 1 ? 1 : page;

    private static int NormalizeSize(int size) => size is < 1 or > 100 ? DefaultSize : size;
}
