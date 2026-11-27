using System.Net;
using System.Security.Claims;
using System.Text;
using BuildingBlocks.Infrastructure.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Infrastructure.Portal;
using Xunit;

namespace TaxVision.Customer.Tests.Integration;

/// <summary>
/// SR-03 con el cableado real del host: el cliente M2M hacia Auth toma el bearer de la petición entrante y lo manda
/// en <c>X-Actor-Authorization</c>, además del token de servicio. Auth se sustituye por un manejador que solo mira.
/// </summary>
public sealed class CustomerPortalActorTokenWiringTests : IClassFixture<CustomerApiFactory>
{
    private readonly CapturingHandler _auth = new();
    private readonly WebApplicationFactory<Program> _factory;

    public CustomerPortalActorTokenWiringTests(CustomerApiFactory baseFactory) =>
        _factory = baseFactory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IServiceTokenAcquirer>(new StaticServiceToken());
                services
                    .AddHttpClient<ICustomerPortalAccessClient, CustomerPortalAccessClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => _auth);
            })
        );

    [Fact]
    public async Task The_credentials_client_forwards_the_bearer_of_the_incoming_request()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        var incoming = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", Guid.NewGuid().ToString())], "Bearer")),
        };
        incoming.Request.Headers.Authorization = "Bearer user-access-token";
        accessor.HttpContext = incoming;
        try
        {
            var credentials = scope.ServiceProvider.GetRequiredService<ICustomerPortalCredentialsClient>();
            var mfa = scope.ServiceProvider.GetRequiredService<ICustomerPortalAccessClient>();
            var request = new PortalCredentialRequest(
                Guid.NewGuid(),
                DateTime.UtcNow,
                "203.0.113.7",
                "UA",
                "OfficePolicy"
            );

            await credentials.SecureAsync(Guid.NewGuid(), Guid.NewGuid(), request);
            await mfa.ResetMfaAsync(Guid.NewGuid(), Guid.NewGuid(), request);
        }
        finally
        {
            accessor.HttpContext = null;
        }

        Assert.Equal(2, _auth.Seen.Count);
        Assert.All(
            _auth.Seen,
            sent =>
            {
                Assert.Equal("Bearer service-token", sent.Authorization);
                Assert.Equal("Bearer user-access-token", sent.Actor);
            }
        );
    }

    private sealed class StaticServiceToken : IServiceTokenAcquirer
    {
        public Task<string?> GetTokenAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<string?>("service-token");
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public List<(string? Authorization, string? Actor)> Seen { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Seen.Add(
                (
                    request.Headers.Authorization?.ToString(),
                    request.Headers.TryGetValues("X-Actor-Authorization", out var values) ? values.Single() : null
                )
            );
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent(
                        """{"code":"Test.Stop","message":"stop"}""",
                        Encoding.UTF8,
                        "application/json"
                    ),
                }
            );
        }
    }
}
