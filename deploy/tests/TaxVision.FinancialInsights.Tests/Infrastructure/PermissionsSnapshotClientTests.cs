using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.FinancialInsights.Infrastructure.Permissions;
using Xunit;

namespace TaxVision.FinancialInsights.Tests;

public sealed class PermissionsSnapshotClientTests
{
    [Fact]
    public async Task Pulls_existing_users_effective_codes_from_auth_with_service_token()
    {
        var tenant = Guid.NewGuid();
        var user = Guid.NewGuid();
        var role = Guid.NewGuid();
        var calls = new List<string>();
        using var http = new HttpClient(
            new Handler(request =>
            {
                calls.Add(request.RequestUri!.AbsolutePath);
                if (request.RequestUri.AbsolutePath == "/auth/service-token")
                    return Json("""{"accessToken":"service-token","expiresInSeconds":300}""");
                Assert.Equal(
                    $"/internal/tenants/{tenant:D}/users/{user:D}/permissions-snapshot",
                    request.RequestUri.AbsolutePath
                );
                Assert.Equal("service-token", request.Headers.Authorization?.Parameter);
                return Json(
                    $$"""{"permissionsVersion":4,"permissionCodes":["financial.import"],"roleIds":["{{role}}"]}"""
                );
            })
        )
        {
            BaseAddress = new Uri("https://auth.test/"),
        };
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ServiceAuthClient:ClientId"] = "financial-insights-worker",
                    ["ServiceAuthClient:ClientSecret"] = "test-only",
                }
            )
            .Build();
        var client = new FinancialPermissionsSnapshotClient(
            http,
            new FinancialServiceTokenAcquirer(http, config, NullLogger<FinancialServiceTokenAcquirer>.Instance)
        );
        var snapshot = await client.FetchSnapshotAsync(tenant, user);
        Assert.Equal(4, snapshot?.PermissionsVersion);
        Assert.Contains("financial.import", snapshot!.PermissionCodes);
        Assert.Equal(role, Assert.Single(snapshot.RoleIds));
        Assert.Equal(2, calls.Count);

        // El segundo snapshot del mismo tenant reutiliza el token: solo va a Auth por el snapshot.
        await client.FetchSnapshotAsync(tenant, user);
        Assert.Equal(1, calls.Count(path => path == "/auth/service-token"));
    }

    [Fact]
    public async Task Without_a_service_token_there_is_no_snapshot()
    {
        using var http = new HttpClient(
            new Handler(_ => throw new InvalidOperationException("Auth must not be called"))
        )
        {
            BaseAddress = new Uri("https://auth.test/"),
        };
        var noCredentials = new ConfigurationBuilder().Build();
        var client = new FinancialPermissionsSnapshotClient(
            http,
            new FinancialServiceTokenAcquirer(http, noCredentials, NullLogger<FinancialServiceTokenAcquirer>.Instance)
        );

        Assert.Null(await client.FetchSnapshotAsync(Guid.NewGuid(), Guid.NewGuid()));
    }

    private static HttpResponseMessage Json(string content) =>
        new(HttpStatusCode.OK) { Content = new StringContent(content, System.Text.Encoding.UTF8, "application/json") };

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => Task.FromResult(reply(request));
    }
}
