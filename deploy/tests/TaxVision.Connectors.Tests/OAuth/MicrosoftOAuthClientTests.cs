using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Connectors.Application.OAuth;
using TaxVision.Connectors.Infrastructure.Providers.OAuth;
using TaxVision.Connectors.Tests.Providers;

namespace TaxVision.Connectors.Tests.OAuth;

public class MicrosoftOAuthClientTests
{
    private static MicrosoftOAuthClient CreateClient(FakeHttpMessageHandler handler) =>
        new(
            new HttpClient(handler),
            Options.Create(
                new MicrosoftOAuthOptions
                {
                    ClientId = "client-id",
                    ClientSecret = "client-secret",
                    TenantId = "common",
                }
            ),
            NullLogger<MicrosoftOAuthClient>.Instance
        );

    [Fact]
    public async Task RefreshAccessTokenAsync_ParsesTokenResponse()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """{ "access_token": "at", "refresh_token": "rt", "expires_in": 3600 }""");

        var grant = await CreateClient(handler).RefreshAccessTokenAsync("refresh-token");

        Assert.Equal("at", grant.AccessToken);
        Assert.Equal("rt", grant.RefreshToken);
    }

    [Fact]
    public async Task ExchangeAuthorizationCodeAsync_SendsAuthorizationCodeGrant()
    {
        var handler = new FakeHttpMessageHandler();
        string? capturedBody = null;
        handler.Enqueue(request =>
        {
            capturedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{ "access_token": "at", "refresh_token": "rt", "expires_in": 3600 }"""),
            };
        });

        var grant = await CreateClient(handler)
            .ExchangeAuthorizationCodeAsync("auth-code", "https://api.example.com/connectors/oauth/callback/graph");

        Assert.Equal("at", grant.AccessToken);
        Assert.Contains("grant_type=authorization_code", capturedBody);
        Assert.Contains("code=auth-code", capturedBody);
    }

    [Fact]
    public async Task ExchangeAuthorizationCodeAsync_OnFailure_ThrowsOAuthProviderException()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.BadRequest, "{}");

        await Assert.ThrowsAsync<TaxVision.Connectors.Application.OAuth.OAuthProviderException>(() =>
            CreateClient(handler).ExchangeAuthorizationCodeAsync("bad-code", "https://redirect")
        );
    }

    [Theory]
    [InlineData(
        HttpStatusCode.BadRequest,
        "invalid_grant",
        "AADSTS700082: The refresh token has expired due to inactivity."
    )]
    [InlineData(
        HttpStatusCode.BadRequest,
        "invalid_grant",
        "AADSTS50173: The provided grant has expired due to it being revoked."
    )]
    [InlineData(
        HttpStatusCode.BadRequest,
        "interaction_required",
        "AADSTS50076: Due to a configuration change made by your administrator, you must use multi-factor authentication."
    )]
    [InlineData(
        HttpStatusCode.BadRequest,
        "consent_required",
        "AADSTS65001: The user or administrator has not consented to use the application."
    )]
    public async Task RefreshAccessTokenAsync_WhenEntraRejectsTheUserGrant_RequiresReconnect(
        HttpStatusCode status,
        string error,
        string description
    )
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(status, $$"""{ "error": "{{error}}", "error_description": "{{description}}" }""");

        var ex = await Assert.ThrowsAsync<OAuthProviderException>(() =>
            CreateClient(handler).RefreshAccessTokenAsync("refresh-token")
        );

        Assert.True(ex.RequiresReconnect);
        Assert.Contains(error, ex.Message);
        Assert.Contains(description.Split(':')[0], ex.Message);
    }

    [Theory]
    // Client secret vencido en Entra: es de la app, no de la cuenta. Reconectar no lo arregla.
    [InlineData(
        HttpStatusCode.Unauthorized,
        """{ "error": "invalid_client", "error_description": "AADSTS7000222: The provided client secret keys are expired." }"""
    )]
    [InlineData(
        HttpStatusCode.BadRequest,
        """{ "error": "unauthorized_client", "error_description": "AADSTS700016: Application not found in the directory." }"""
    )]
    [InlineData(HttpStatusCode.TooManyRequests, """{ "error": "temporarily_unavailable" }""")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "")]
    [InlineData(HttpStatusCode.BadRequest, "<html>gateway error</html>")]
    public async Task RefreshAccessTokenAsync_WhenTheFailureIsNotTheAccounts_DoesNotRequireReconnect(
        HttpStatusCode status,
        string body
    )
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(status, body);

        var ex = await Assert.ThrowsAsync<OAuthProviderException>(() =>
            CreateClient(handler).RefreshAccessTokenAsync("refresh-token")
        );

        Assert.False(ex.RequiresReconnect);
    }
}
