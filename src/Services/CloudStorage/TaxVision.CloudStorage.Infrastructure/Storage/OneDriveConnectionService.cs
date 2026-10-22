using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using BuildingBlocks.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Domain.Providers;
using TaxVision.CloudStorage.Infrastructure.Persistence;

namespace TaxVision.CloudStorage.Infrastructure.Storage;

public sealed class OneDriveOptions
{
    public const string SectionName = "CloudStorage:OneDrive";
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = string.Empty;
    public string FrontendReturnUri { get; set; } = string.Empty;
}

public interface IOneDriveConnectionService
{
    Task<string> StartAsync(Guid tenantId, Guid userId, string? returnOrigin, CancellationToken ct);
    Task CompleteAsync(Guid stateId, string code, CancellationToken ct);
    Task<(bool Connected, string? Email)> GetStatusAsync(Guid tenantId, CancellationToken ct);
    Task DisconnectAsync(Guid tenantId, CancellationToken ct);
}

/// <summary>Microsoft identity OAuth authorization-code flow for tenant OneDrive accounts.</summary>
public sealed class OneDriveConnectionService(
    CloudStorageDbContext db,
    IHttpClientFactory clients,
    IServiceProvider services,
    CloudProviderOAuthStateStore states,
    IOptions<OneDriveOptions> options
) : IOneDriveConnectionService
{
    public const string ProviderId = "onedrive";

    // Files.ReadWrite is required for OneDrive for Business upload sessions. The integration itself
    // only addresses the app folder; Microsoft's delegated business permission is broader than that.
    public const string Scopes = "openid profile email offline_access User.Read Files.ReadWrite";
    private const string Authority = "https://login.microsoftonline.com/common/oauth2/v2.0";

    public async Task<string> StartAsync(Guid tenantId, Guid userId, string? returnOrigin, CancellationToken ct)
    {
        var config = options.Value;
        EnsureConfigured(config);
        // Sin Encryption:MasterKey el token no se puede guardar: fallar aquí, antes del consentimiento.
        _ = services.GetRequiredService<ISecretProtector>();
        var state = await states.IssueAsync(tenantId, userId, ProviderId, returnOrigin, ct);

        var values = new Dictionary<string, string>
        {
            ["client_id"] = config.ClientId,
            ["response_type"] = "code",
            ["redirect_uri"] = config.RedirectUri,
            ["response_mode"] = "query",
            ["scope"] = Scopes,
            ["state"] = state.Id.ToString("N"),
            ["prompt"] = "select_account",
        };
        return Authority
            + "/authorize?"
            + string.Join(
                "&",
                values.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}")
            );
    }

    public async Task CompleteAsync(Guid stateId, string code, CancellationToken ct)
    {
        var state =
            await states.ConsumeAsync(stateId, ProviderId, ct)
            ?? throw new InvalidOperationException("The OneDrive authorization request expired. Please reconnect.");

        var config = options.Value;
        EnsureConfigured(config);
        using var tokenResponse = await clients
            .CreateClient("onedrive-oauth")
            .PostAsync(
                Authority + "/token",
                new FormUrlEncodedContent(
                    new Dictionary<string, string>
                    {
                        ["client_id"] = config.ClientId,
                        ["client_secret"] = config.ClientSecret,
                        ["code"] = code,
                        ["redirect_uri"] = config.RedirectUri,
                        ["grant_type"] = "authorization_code",
                        ["scope"] = Scopes,
                    }
                ),
                ct
            );
        tokenResponse.EnsureSuccessStatusCode();
        var token =
            await tokenResponse.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Microsoft did not return an OAuth token.");
        if (string.IsNullOrWhiteSpace(token.RefreshToken))
            throw new InvalidOperationException(
                "Microsoft did not return offline access. Reconnect and accept the requested permissions."
            );

        using var profileRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "https://graph.microsoft.com/v1.0/me?$select=mail,userPrincipalName"
        );
        profileRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        using var profileResponse = await clients.CreateClient("onedrive-graph").SendAsync(profileRequest, ct);
        profileResponse.EnsureSuccessStatusCode();
        var profile = await profileResponse.Content.ReadFromJsonAsync<ProfileResponse>(cancellationToken: ct);
        var email = profile?.Mail ?? profile?.UserPrincipalName;
        if (string.IsNullOrWhiteSpace(email))
            throw new InvalidOperationException("Microsoft Graph did not return the OneDrive account address.");

        var old = await db
            .CloudConnections.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.TenantId == state.TenantId && x.Provider == ProviderId, ct);
        if (old is not null)
            db.CloudConnections.Remove(old);
        var protector = services.GetRequiredService<ISecretProtector>();
        db.CloudConnections.Add(
            TenantCloudConnection.Connect(state.TenantId, ProviderId, email, protector.Protect(token.RefreshToken))
        );
        await db.SaveChangesAsync(ct);
    }

    public async Task<(bool Connected, string? Email)> GetStatusAsync(Guid tenantId, CancellationToken ct)
    {
        var connection = await db
            .CloudConnections.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Provider == ProviderId, ct);
        return (connection is not null, connection?.AccountEmail);
    }

    // Microsoft does not expose a consumer OAuth token-revocation endpoint. Disconnect removes
    // the encrypted token from TaxVision; the tenant can separately revoke the app grant in Entra.
    public async Task DisconnectAsync(Guid tenantId, CancellationToken ct)
    {
        var connection = await db.CloudConnections.SingleOrDefaultAsync(
            x => x.TenantId == tenantId && x.Provider == ProviderId,
            ct
        );
        if (connection is null)
            return;
        db.CloudConnections.Remove(connection);
        await db.SaveChangesAsync(ct);
    }

    private static void EnsureConfigured(OneDriveOptions config)
    {
        if (
            string.IsNullOrWhiteSpace(config.ClientId)
            || string.IsNullOrWhiteSpace(config.ClientSecret)
            || string.IsNullOrWhiteSpace(config.RedirectUri)
            || string.IsNullOrWhiteSpace(config.FrontendReturnUri)
        )
            throw new InvalidOperationException("OneDrive OAuth is not configured for this environment.");
        if (
            !Uri.TryCreate(config.RedirectUri, UriKind.Absolute, out var redirect)
            || (redirect.Scheme != Uri.UriSchemeHttps && !redirect.IsLoopback)
        )
            throw new InvalidOperationException(
                "OneDrive OAuth redirect URL must use HTTPS, except for localhost development."
            );
        if (
            !Uri.TryCreate(config.FrontendReturnUri, UriKind.Absolute, out var frontend)
            || (frontend.Scheme != Uri.UriSchemeHttps && !frontend.IsLoopback)
        )
            throw new InvalidOperationException(
                "OneDrive frontend return URL must use HTTPS, except for localhost development."
            );
    }

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken
    );

    private sealed record ProfileResponse(
        [property: JsonPropertyName("mail")] string? Mail,
        [property: JsonPropertyName("userPrincipalName")] string? UserPrincipalName
    );
}
