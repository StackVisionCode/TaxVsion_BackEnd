using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.FinancialInsights.Infrastructure.Permissions;
using Xunit;

namespace TaxVision.FinancialInsights.Tests;

/// <summary>
/// El token M2M dura minutos: se reutiliza por tenant hasta poco antes de vencer, como en el resto de servicios.
/// Sin caché, cada llamada a otro servicio era también un POST a Auth.
/// </summary>
public sealed class FinancialServiceTokenAcquirerTests
{
    [Fact]
    public async Task The_same_tenant_reuses_its_token_instead_of_asking_auth_again()
    {
        var auth = new CountingAuth(expiresInSeconds: 600);
        var acquirer = Acquirer(auth);
        var tenant = Guid.NewGuid();

        var first = await acquirer.GetTokenAsync(tenant);
        var second = await acquirer.GetTokenAsync(tenant);

        Assert.Equal(first, second);
        Assert.Equal(1, auth.Requests);
    }

    [Fact]
    public async Task Each_tenant_gets_its_own_token()
    {
        var auth = new CountingAuth(expiresInSeconds: 600);
        var acquirer = Acquirer(auth);

        var a = await acquirer.GetTokenAsync(Guid.NewGuid());
        var b = await acquirer.GetTokenAsync(Guid.NewGuid());

        Assert.NotEqual(a, b);
        Assert.Equal(2, auth.Requests);
    }

    [Fact]
    public async Task A_token_about_to_expire_is_renewed()
    {
        var auth = new CountingAuth(expiresInSeconds: 5);
        var acquirer = Acquirer(auth);
        var tenant = Guid.NewGuid();

        await acquirer.GetTokenAsync(tenant);
        await acquirer.GetTokenAsync(tenant);

        Assert.Equal(2, auth.Requests);
    }

    private static FinancialServiceTokenAcquirer Acquirer(CountingAuth auth) =>
        new(
            new HttpClient(auth) { BaseAddress = new Uri("https://auth.test/") },
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["ServiceAuthClient:ClientId"] = "financial-insights-worker",
                        ["ServiceAuthClient:ClientSecret"] = "test-only",
                    }
                )
                .Build(),
            NullLogger<FinancialServiceTokenAcquirer>.Instance
        );

    private sealed class CountingAuth(int expiresInSeconds) : HttpMessageHandler
    {
        private int _requests;

        public int Requests => _requests;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal("/auth/service-token", request.RequestUri!.AbsolutePath);
            var n = Interlocked.Increment(ref _requests);
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        $$"""{"accessToken":"token-{{n}}","expiresInSeconds":{{expiresInSeconds}}}""",
                        System.Text.Encoding.UTF8,
                        "application/json"
                    ),
                }
            );
        }
    }
}
