using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Customer.Api.Requests;
using TaxVision.Customer.Application.Customers.Avatars;
using TaxVision.Customer.Application.Customers.Portal;
using Wolverine;

namespace TaxVision.Customer.Api.Controllers;

/// <summary>
/// Autoservicio del cliente del portal sobre su propia ficha, como los <c>/auth/me</c>: solo
/// <see cref="ActorType.CustomerPortal"/> y el cliente sale del token, nunca de la petición.
/// </summary>
[ApiController]
[Route("customers/me")]
[Authorize]
[AllowActorTypes(ActorType.CustomerPortal)]
public sealed class PortalCustomerController(IMessageBus bus) : ControllerBase
{
    public sealed record UpdateMyPhoneRequest(string? PhoneNumber);

    [HttpGet("contact")]
    [RateLimit("customer.f.get")]
    [ProducesResponseType<MyCustomerContactResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetContact(CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _) || !User.TryGetCustomerId(out var customerId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<MyCustomerContactResponse>>(
            new GetMyCustomerContactQuery(tenantId, customerId),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPut("phone")]
    [RateLimit("customer.g.write")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdatePhone(UpdateMyPhoneRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId) || !User.TryGetCustomerId(out var customerId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(
            new UpdateMyCustomerPhoneCommand(tenantId, customerId, userId, request.PhoneNumber),
            ct
        );
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    // Foto de perfil propia: 202 "processing"; se ve cuando el antivirus la aprueba (GET contact).
    [HttpPut("avatar")]
    [RateLimit("customer.g.write")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(AvatarUpload.MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = AvatarUpload.MaxRequestBytes)]
    [ProducesResponseType<UploadCustomerAvatarResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<Error>(StatusCodes.Status413PayloadTooLarge)]
    public async Task<IActionResult> UploadAvatar(IFormFile? file, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId) || !User.TryGetCustomerId(out var customerId))
            return Unauthorized();

        var content = await AvatarUpload.ReadAsync(file, ct);
        if (content.IsFailure)
            return StatusCode(content.Error.ToHttpStatusCode(), content.Error);

        var result = await bus.InvokeAsync<Result<UploadCustomerAvatarResponse>>(
            new UploadCustomerAvatarCommand(
                tenantId,
                customerId,
                userId,
                CustomerAvatarCaller.PortalSelf,
                CallerCanViewAllCustomers: false,
                content.Value
            ),
            ct
        );
        return result.IsSuccess ? Accepted(result.Value) : ToError(result.Error);
    }

    [HttpDelete("avatar")]
    [RateLimit("customer.g.write")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveAvatar(CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId) || !User.TryGetCustomerId(out var customerId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(
            new RemoveCustomerAvatarCommand(
                tenantId,
                customerId,
                userId,
                CustomerAvatarCaller.PortalSelf,
                CallerCanViewAllCustomers: false
            ),
            ct
        );
        return result.IsSuccess ? NoContent() : ToError(result.Error);
    }

    private ObjectResult ToError(Error error) =>
        error.Code == "Customer.NotFound" ? NotFound(error) : StatusCode(error.ToHttpStatusCode(), error);
}
