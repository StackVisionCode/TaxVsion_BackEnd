using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Imaging;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Auth.Application.Users.Avatars;
using Wolverine;

namespace TaxVision.Auth.Api.Controllers;

/// <summary>
/// Foto de perfil propia del staff. La del cliente del portal vive en su ficha de Customer
/// (<c>/customers/me/avatar</c>), por eso CustomerPortal no entra acá. Usuario y oficina salen del token.
/// La imagen se sirve sin token en <c>/storage/avatars/{fileId}</c> (CloudStorage).
/// </summary>
[ApiController]
[Route("auth/me/avatar")]
[Authorize]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class MeAvatarController(IMessageBus bus) : ControllerBase
{
    /// <summary>Tope del cuerpo: los 5 MB de la foto más el sobre multipart.</summary>
    public const long MaxRequestBytes = 5_767_168;

    /// <summary>Sube (o reemplaza) la foto. Responde 202: se confirma cuando termina el antivirus.</summary>
    [HttpPut]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes)]
    [RateLimit("auth.g.user_profile_manage")]
    [ProducesResponseType<UploadMyAvatarResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<Error>(StatusCodes.Status413PayloadTooLarge)]
    public async Task<IActionResult> Upload(IFormFile? file, CancellationToken ct)
    {
        if (!User.TryGetUserId(out var userId) || !User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        if (file is null || file.Length == 0)
            return Failure(AvatarImageErrors.Empty);
        // Se corta antes de leer el archivo; el límite real es el del normalizador.
        if (file.Length > AvatarImageOptions.DefaultMaxInputBytes)
            return Failure(AvatarImageErrors.TooLarge);

        using var buffer = new MemoryStream((int)file.Length);
        await file.CopyToAsync(buffer, ct);

        var result = await bus.InvokeAsync<Result<UploadMyAvatarResponse>>(
            new UploadMyAvatarCommand(tenantId, userId, buffer.ToArray()),
            ct
        );

        return result.IsSuccess ? Accepted(result.Value) : Failure(result.Error);
    }

    /// <summary>Quita la foto (y la que estuviera pendiente). Idempotente.</summary>
    [HttpDelete]
    [RateLimit("auth.g.user_profile_manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Remove(CancellationToken ct)
    {
        if (!User.TryGetUserId(out var userId) || !User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(new RemoveMyAvatarCommand(tenantId, userId), ct);

        return result.IsSuccess ? NoContent() : Failure(result.Error);
    }

    private ObjectResult Failure(Error error) => StatusCode(error.ToHttpStatusCode(), error);
}
