using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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
}
