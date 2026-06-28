using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Customer.Api.Requests;
using TaxVision.Customer.Application.Customers;
using TaxVision.Customer.Application.Customers.Commands.AddAddress;
using TaxVision.Customer.Application.Customers.Commands.AddContactPoint;
using TaxVision.Customer.Application.Customers.Commands.AddRelation;
using TaxVision.Customer.Application.Customers.Commands.Archive;
using TaxVision.Customer.Application.Customers.Commands.Create;
using TaxVision.Customer.Application.Customers.Commands.RequestPortalInvitation;
using TaxVision.Customer.Application.Customers.Commands.SetCustomerFiscalProfile;
using TaxVision.Customer.Application.Customers.Commands.SetRelationFiscalProfile;
using TaxVision.Customer.Application.Customers.Commands.Update;
using TaxVision.Customer.Application.Customers.FiscalProfiles;
using TaxVision.Customer.Application.Customers.Queries.GetById;
using TaxVision.Customer.Application.Customers.Queries.Search;
using Wolverine;

namespace TaxVision.Customer.Api.Controllers;

[ApiController]
[Route("customers")]
[Authorize]
public sealed class CustomerController(IMessageBus bus) : ControllerBase
{
    // ---------- POST /customers ----------
    [HttpPost]
    [Authorize(Roles = "TenantEmploye,TenantAdmin")]
    [ProducesResponseType<CustomerResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateCustomerRequest body, CancellationToken ct)
    {
        if (!TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var cmd = new CreateCustomerCommand(
            tenantId,
            userId,
            body.Kind,
            body.FirstName,
            body.MiddleName,
            body.LastName,
            body.Prefix,
            body.Suffix,
            body.LegalName,
            body.BusinessStructure,
            body.Dba,
            body.FormationDate,
            body.PrincipalBusinessActivityId,
            body.DateOfBirth,
            body.OccupationId,
            body.PrimaryEmail,
            body.PrimaryPhone,
            body.Language,
            body.PreferredChannel
        );

        var result = await bus.InvokeAsync<Result<CustomerResponse>>(cmd, ct);

        return result.IsSuccess
            ? Created($"/customers/{result.Value.Id}", result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    // ---------- GET /customers ----------
    [HttpGet]
    [Authorize(Roles = "TenantEmployee,TenantAdmin")]
    [ProducesResponseType<IReadOnlyList<CustomerSummaryResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CustomerSummaryResponse>>> Search(
        [FromQuery] string? term = null,
        [FromQuery] int page = 1,
        [FromQuery] int size = 20,
        CancellationToken ct = default
    )
    {
        var list = await bus.InvokeAsync<IReadOnlyList<CustomerSummaryResponse>>(
            new SearchCustomersQuery(term, page, size),
            ct
        );
        return Ok(list);
    }

    // ---------- GET /customers/{id} ----------
    [HttpGet("{id:guid}")]
    [Authorize(Roles = "TenantEmployee,TenantAdmin")]
    [ProducesResponseType<CustomerResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<CustomerResponse>>(new GetCustomerByIdQuery(id), ct);

        if (result.IsSuccess)
            return Ok(result.Value);
        if (result.Error.Code == "Customer.NotFound")
            return NotFound(result.Error);
        return StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    // ---------- PATCH /customers/{id} ----------
    [HttpPatch("{id:guid}")]
    [Authorize(Roles = "TenantEmployee,TenantAdmin")]
    [ProducesResponseType<CustomerResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateCustomerRequest body, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized();

        var cmd = new UpdateCustomerCommand(
            id,
            userId,
            body.Language,
            body.PreferredChannel,
            body.OccupationId,
            body.ProfilePictureFileId,
            body.PrimaryEmail,
            body.PrimaryPhone
        );

        var result = await bus.InvokeAsync<Result<CustomerResponse>>(cmd, ct);

        if (result.IsSuccess)
            return Ok(result.Value);
        if (result.Error.Code == "Customer.NotFound")
            return NotFound(result.Error);
        return StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    // ---------- POST /customers/{id}/addresses ----------
    [HttpPost("{id:guid}/addresses")]
    [Authorize(Roles = "TenantEmployee,TenantAdmin")]
    [ProducesResponseType<AddressResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> AddAddress(Guid id, [FromBody] AddAddressRequest body, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized();

        var cmd = new AddAddressCommand(
            id,
            userId,
            body.Kind,
            body.Line1,
            body.Line2,
            body.City,
            body.Region,
            body.PostalCode,
            body.CountryCode,
            body.IsPrimary
        );

        var result = await bus.InvokeAsync<Result<AddressResponse>>(cmd, ct);

        return result.IsSuccess
            ? Created($"/customers/{id}/addresses/{result.Value.Id}", result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    // ---------- POST /customers/{id}/contact-points ----------
    [HttpPost("{id:guid}/contact-points")]
    [Authorize(Roles = "TenantEmployee,TenantAdmin")]
    [ProducesResponseType<ContactPointResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> AddContactPoint(
        Guid id,
        [FromBody] AddContactPointRequest body,
        CancellationToken ct
    )
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized();

        var cmd = new AddContactPointCommand(id, userId, body.Type, body.Value, body.Label, body.IsPrimary);

        var result = await bus.InvokeAsync<Result<ContactPointResponse>>(cmd, ct);

        return result.IsSuccess
            ? Created($"/customers/{id}/contact-points/{result.Value.Id}", result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    // ---------- POST /customers/{id}/relations ----------
    [HttpPost("{id:guid}/relations")]
    [Authorize(Roles = "TenantEmployee,TenantAdmin")]
    [ProducesResponseType<RelationResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> AddRelation(Guid id, [FromBody] AddRelationRequest body, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized();

        var cmd = new AddRelationCommand(
            id,
            userId,
            body.RelationshipKind,
            body.Purposes,
            body.FirstName,
            body.LastName,
            body.MiddleName,
            body.Prefix,
            body.Suffix,
            body.PrimaryEmail,
            body.PrimaryPhone,
            body.DateOfBirth,
            body.AddressLine1,
            body.AddressLine2,
            body.AddressCity,
            body.AddressRegion,
            body.AddressPostalCode,
            body.AddressCountryCode
        );

        var result = await bus.InvokeAsync<Result<RelationResponse>>(cmd, ct);

        return result.IsSuccess
            ? Created($"/customers/{id}/relations/{result.Value.Id}", result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    // ---------- POST /customers/{id}/archive ----------
    [HttpPost("{id:guid}/archive")]
    [Authorize(Roles = "TenantAdmin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Archive(Guid id, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(new ArchiveCustomerCommand(id, userId), ct);

        if (result.IsSuccess)
            return NoContent();
        if (result.Error.Code == "Customer.NotFound")
            return NotFound(result.Error);
        return StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    // ---------- POST /customers/{id}/portal-invitations ----------
    [HttpPost("{id:guid}/portal-invitations")]
    [Authorize(Roles = "TenantAdmin")]
    [ProducesResponseType<RequestPortalInvitationResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<Error>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RequestPortalInvitation(Guid id, CancellationToken ct)
    {
        if (!TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<RequestPortalInvitationResponse>>(
            new RequestPortalInvitationCommand(tenantId, id, userId),
            ct
        );

        if (result.IsSuccess)
            return Accepted($"/customers/{id}/portal-invitations", result.Value);
        if (result.Error.Code == "Customer.NotFound")
            return NotFound(result.Error);
        return StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    // ---------- PUT /customers/{id}/fiscal-profile ----------
    [HttpPut("{id:guid}/fiscal-profile")]
    [Authorize(Roles = "TenantAdmin")]
    [ProducesResponseType<CustomerFiscalProfileResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SetFiscalProfile(
        Guid id,
        [FromBody] SetCustomerFiscalProfileRequest body,
        CancellationToken ct
    )
    {
        if (!TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var cmd = new SetCustomerFiscalProfileCommand(
            tenantId,
            id,
            userId,
            body.SubjectKind,
            body.TaxIdentifier,
            body.FilingStatus,
            body.PriorYearAgi,
            body.IsReturningCustomer,
            body.RefundBankAccount,
            body.RefundBankRouting
        );

        var result = await bus.InvokeAsync<Result<CustomerFiscalProfileResponse>>(cmd, ct);
        if (result.IsSuccess)
            return Ok(result.Value);
        if (result.Error.Code == "Customer.NotFound")
            return NotFound(result.Error);
        return StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    // ---------- PUT /customers/{id}/relations/{relationId}/fiscal-profile ----------
    [HttpPut("{id:guid}/relations/{relationId:guid}/fiscal-profile")]
    [Authorize(Roles = "TenantAdmin")]
    [ProducesResponseType<RelationFiscalProfileResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SetRelationFiscalProfile(
        Guid id,
        Guid relationId,
        [FromBody] SetRelationFiscalProfileRequest body,
        CancellationToken ct
    )
    {
        if (!TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var cmd = new SetRelationFiscalProfileCommand(
            tenantId,
            id,
            relationId,
            userId,
            body.Role,
            body.TaxIdentifier,
            body.TaxYear,
            body.QualifiesAsDependent,
            body.LivedWithTaxpayer
        );

        var result = await bus.InvokeAsync<Result<RelationFiscalProfileResponse>>(cmd, ct);
        if (result.IsSuccess)
            return Ok(result.Value);
        if (result.Error.Code is "Customer.NotFound" or "Relation.NotFound")
            return NotFound(result.Error);
        return StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    // ---------- Helpers ----------
    private bool TryGetUserId(out Guid userId)
    {
        var raw =
            User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(raw, out userId);
    }

    private bool TryGetTenantAndUser(out Guid tenantId, out Guid userId)
    {
        tenantId = Guid.Empty;
        var tenantRaw = User.FindFirst("tenant_id")?.Value;
        if (!Guid.TryParse(tenantRaw, out tenantId))
        {
            userId = Guid.Empty;
            return false;
        }
        return TryGetUserId(out userId);
    }
}
