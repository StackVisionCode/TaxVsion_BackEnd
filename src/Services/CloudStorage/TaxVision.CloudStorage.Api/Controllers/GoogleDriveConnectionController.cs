using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Platform;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Infrastructure.Storage;

namespace TaxVision.CloudStorage.Api.Controllers;

[ApiController]
[Route("storage/providers/google-drive")]
[Authorize]
[AllowActorTypes(ActorType.TenantAdmin, ActorType.PlatformAdmin)]
[HasPermission(CloudStoragePermissions.SettingsManage)]
public sealed class GoogleDriveConnectionController(IGoogleDriveConnectionService connections) : ControllerBase
{
    public sealed record ConnectionStatusResponse(bool Connected, string? Email);

    public sealed record AuthorizationUrlResponse(string AuthorizationUrl);

    [HttpGet]
    [RateLimit("cloudstorage.f.admin_read")]
    public async Task<IActionResult> GetStatus(CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();
        var status = await connections.GetStatusAsync(tenantId, ct);
        return Ok(new ConnectionStatusResponse(status.Connected, status.Email));
    }

    [HttpPost("connect")]
    [RateLimit("cloudstorage.g.settings_manage")]
    public async Task<IActionResult> Connect(CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId) || !User.TryGetUserId(out var userId))
            return Unauthorized();
        try
        {
            return Ok(
                new AuthorizationUrlResponse(
                    await connections.StartAsync(tenantId, userId, Request.Headers.Origin.ToString(), ct)
                )
            );
        }
        catch (InvalidOperationException ex)
        {
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: ex.Message);
        }
    }

    [HttpDelete]
    [RateLimit("cloudstorage.g.settings_manage")]
    public async Task<IActionResult> Disconnect(CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();
        await connections.DisconnectAsync(tenantId, ct);
        return NoContent();
    }
}

[ApiController]
[Route("storage/providers/google-drive/callback")]
[AllowAnonymous]
public sealed class GoogleDriveOAuthCallbackController(
    IGoogleDriveConnectionService connections,
    CloudProviderOAuthStateStore states,
    IPlatformSettings platform,
    IOptions<GoogleDriveOptions> options,
    ILogger<GoogleDriveOAuthCallbackController> logger
) : ControllerBase
{
    [HttpGet]
    [EnableRateLimiting("provider-oauth-callback")]
    [RateLimitExempt(
        "Endpoint anonimo (redirect del navegador tras el consentimiento) — [RateLimit] particiona por "
            + "Tenant/User y fallaria abierto aqui. Lo protege el limiter nativo provider-oauth-callback (IP+ruta)."
    )]
    public async Task<IActionResult> Callback(
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error,
        CancellationToken ct
    )
    {
        var destination = options.Value.FrontendReturnUri;
        if (
            !Uri.TryCreate(destination, UriKind.Absolute, out var returnUri)
            || returnUri.Scheme != Uri.UriSchemeHttps && !returnUri.IsLoopback
        )
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Google Drive return URL is not configured."
            );
        destination = OAuthReturnRedirectPolicy.ResolveUrl(
            await states.FindReturnOriginAsync(state, ct),
            destination,
            platform.Current.BaseDomain
        );

        if (
            !string.IsNullOrEmpty(error)
            || string.IsNullOrWhiteSpace(code)
            || !Guid.TryParseExact(state, "N", out var stateId)
        )
            return Redirect(QueryHelpers.AddQueryString(destination, "googleDrive", "error"));

        try
        {
            await connections.CompleteAsync(stateId, code, ct);
            return Redirect(QueryHelpers.AddQueryString(destination, "googleDrive", "connected"));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning("Google Drive OAuth callback failed: {FailureType}", ex.GetType().Name);
            return Redirect(QueryHelpers.AddQueryString(destination, "googleDrive", "error"));
        }
    }
}
