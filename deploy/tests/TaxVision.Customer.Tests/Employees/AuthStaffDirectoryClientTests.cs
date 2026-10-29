using System.Net;
using System.Text;
using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Customer.Infrastructure.Employees;

namespace TaxVision.Customer.Tests.Employees;

/// <summary>
/// El endpoint de staff de Auth solo acepta un token de servicio de PlatformTenant. El cliente nunca
/// lanza: sin token, con un error HTTP o sin red devuelve null para que la pasada se reintente después.
/// </summary>
public sealed class AuthStaffDirectoryClientTests
{
    private sealed class FakeTokens(string? token) : IServiceTokenAcquirer
    {
        public List<Guid> RequestedTenants { get; } = [];

        public Task<string?> GetTokenAsync(Guid tenantId, CancellationToken ct = default)
        {
            RequestedTenants.Add(tenantId);
            return Task.FromResult(token);
        }
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request;
            return Task.FromResult(respond(request));
        }
    }

    private static AuthStaffDirectoryClient Client(StubHandler handler, IServiceTokenAcquirer tokens) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("http://auth.test/") },
            tokens,
            NullLogger<AuthStaffDirectoryClient>.Instance
        );

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Reads_a_page_with_a_platform_service_token()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var handler = new StubHandler(_ =>
            Json(
                $$"""
                {"items":[{"tenantId":"{{tenantId}}","userId":"{{userId}}","actorType":"TenantAdmin","isActive":true,"isOffboarded":false}],
                 "page":1,"size":1,"totalCount":2}
                """
            )
        );
        var tokens = new FakeTokens("service-token");

        var page = await Client(handler, tokens).ListStaffAsync(1, 1);

        Assert.Equal([PlatformTenant.Id], tokens.RequestedTenants);
        Assert.Equal("Bearer", handler.LastRequest!.Headers.Authorization!.Scheme);
        Assert.Equal("/internal/users/staff/reconciliation", handler.LastRequest.RequestUri!.AbsolutePath);
        var user = Assert.Single(page!.Items);
        Assert.Equal(
            (tenantId, userId, "TenantAdmin", true, false),
            (user.TenantId, user.UserId, user.ActorType, user.IsActive, user.IsOffboarded)
        );
        Assert.True(page.HasMore);
    }

    [Fact]
    public async Task Returns_null_without_a_service_token_and_never_calls_Auth()
    {
        var handler = new StubHandler(_ => Json("{}"));

        Assert.Null(await Client(handler, new FakeTokens(null)).ListStaffAsync(1, 200));
        Assert.Null(handler.LastRequest);
    }

    [Fact]
    public async Task Returns_null_when_Auth_rejects_the_call()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));

        Assert.Null(await Client(handler, new FakeTokens("service-token")).ListStaffAsync(1, 200));
    }

    [Fact]
    public async Task Returns_null_when_Auth_is_unreachable()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("connection refused"));

        Assert.Null(await Client(handler, new FakeTokens("service-token")).ListStaffAsync(1, 200));
    }
}
