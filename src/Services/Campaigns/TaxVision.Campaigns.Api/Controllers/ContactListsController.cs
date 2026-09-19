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
/// Listas de contactos (agrupación + import CSV) — staff únicamente. TenantId SIEMPRE del JWT. Permiso
/// <c>campaigns.manage</c>. Una campaña referencia listas por id; la audiencia se resuelve por run.
/// </summary>
[ApiController]
[Route("contact-lists")]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class ContactListsController(IMessageBus bus) : ControllerBase
{
    private const int DefaultSize = 20;

    [HttpPost]
    [HasPermission(CampaignsPermissions.Manage)]
    [RateLimit("campaigns.g.create")]
    [ProducesResponseType<ContactListResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateContactListRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<ContactListResponse>>(
            new CreateContactListCommand(tenantId, request.Name, request.Description),
            ct
        );
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetById), new { id = result.Value.Id }, result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet]
    [HasPermission(CampaignsPermissions.Manage)]
    [RateLimit("campaigns.f.list")]
    [ProducesResponseType<PagedResult<ContactListResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] int page, [FromQuery] int size, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<PagedResult<ContactListResponse>>(
            new ListContactListsQuery(tenantId, NormalizePage(page), NormalizeSize(size)),
            ct
        );
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [HasPermission(CampaignsPermissions.Manage)]
    [RateLimit("campaigns.f.get")]
    [ProducesResponseType<ContactListResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<ContactListResponse>>(new GetContactListQuery(tenantId, id), ct);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPut("{id:guid}")]
    [HasPermission(CampaignsPermissions.Manage)]
    [RateLimit("campaigns.g.create")]
    [ProducesResponseType<ContactListResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, UpdateContactListRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<ContactListResponse>>(
            new UpdateContactListCommand(tenantId, id, request.Name, request.Description),
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

        var result = await bus.InvokeAsync<Result>(new DeleteContactListCommand(tenantId, id), ct);
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("{id:guid}/members")]
    [HasPermission(CampaignsPermissions.Manage)]
    [RateLimit("campaigns.g.create")]
    [ProducesResponseType<ContactListResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> AddMember(Guid id, AddListMemberRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<ContactListResponse>>(
            new AddContactToListCommand(tenantId, id, request.ContactId),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpDelete("{id:guid}/members/{contactId:guid}")]
    [HasPermission(CampaignsPermissions.Manage)]
    [RateLimit("campaigns.g.create")]
    [ProducesResponseType<ContactListResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RemoveMember(Guid id, Guid contactId, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<ContactListResponse>>(
            new RemoveContactFromListCommand(tenantId, id, contactId),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("{id:guid}/import")]
    [HasPermission(CampaignsPermissions.Manage)]
    [RateLimit("campaigns.g.create")]
    [ProducesResponseType<ImportContactsResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Import(Guid id, ImportContactsRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<ImportContactsResponse>>(
            new ImportContactsCommand(tenantId, id, request.Csv ?? string.Empty),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    private static int NormalizePage(int page) => page < 1 ? 1 : page;

    private static int NormalizeSize(int size) => size is < 1 or > 100 ? DefaultSize : size;
}
