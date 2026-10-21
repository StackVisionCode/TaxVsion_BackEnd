using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Infrastructure.Storage;

namespace TaxVision.CloudStorage.Api.Controllers;

[ApiController]
[Route("storage/providers/dropbox")]
[Authorize]
[AllowActorTypes(ActorType.TenantAdmin, ActorType.PlatformAdmin)]
[HasPermission(CloudStoragePermissions.SettingsManage)]
public sealed class DropboxConnectionController(IDropboxConnectionService connections) : ControllerBase
{
    public sealed record ConnectionStatusResponse(bool Connected, string? Email);
    public sealed record AuthorizationUrlResponse(string AuthorizationUrl);

    [HttpGet]
    [RateLimit("cloudstorage.f.admin_read")]
    public async Task<IActionResult> GetStatus(CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId)) return Unauthorized();
        var status = await connections.GetStatusAsync(tenantId, ct);
        return Ok(new ConnectionStatusResponse(status.Connected, status.Email));
    }

    [HttpPost("connect")]
    [RateLimit("cloudstorage.g.settings_manage")]
    public async Task<IActionResult> Connect(CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId) || !User.TryGetUserId(out var userId)) return Unauthorized();
        try { return Ok(new AuthorizationUrlResponse(await connections.StartAsync(tenantId, userId, ct))); }
        catch (InvalidOperationException ex) { return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: ex.Message); }
    }

    [HttpDelete]
    [RateLimit("cloudstorage.g.settings_manage")]
    public async Task<IActionResult> Disconnect(CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId)) return Unauthorized();
        await connections.DisconnectAsync(tenantId, ct);
        return NoContent();
    }
}

[ApiController]
[Route("storage/providers/dropbox/callback")]
[AllowAnonymous]
public sealed class DropboxOAuthCallbackController(
    IDropboxConnectionService connections,
    IOptions<DropboxOptions> options,
    ILogger<DropboxOAuthCallbackController> logger
) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Callback(
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error,
        CancellationToken ct)
    {
        var destination = options.Value.FrontendReturnUri;
        if (!Uri.TryCreate(destination, UriKind.Absolute, out var returnUri)
            || (returnUri.Scheme != Uri.UriSchemeHttps && !returnUri.IsLoopback))
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Dropbox return URL is not configured.");
        if (!string.IsNullOrWhiteSpace(error) || string.IsNullOrWhiteSpace(code) || !Guid.TryParseExact(state, "N", out var stateId))
            return Redirect(QueryHelpers.AddQueryString(destination, "dropbox", "error"));
        try
        {
            await connections.CompleteAsync(stateId, code, ct);
            return Redirect(QueryHelpers.AddQueryString(destination, "dropbox", "connected"));
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or Microsoft.EntityFrameworkCore.DbUpdateException)
        {
            logger.LogWarning("Dropbox OAuth callback failed: {FailureType}", ex.GetType().Name);
            return Redirect(QueryHelpers.AddQueryString(destination, "dropbox", "error"));
        }
    }
}
