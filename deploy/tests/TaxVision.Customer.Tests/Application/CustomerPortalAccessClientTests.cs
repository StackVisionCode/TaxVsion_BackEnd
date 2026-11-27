using System.Net;
using System.Text;
using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Web.Results;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Infrastructure.Portal;

namespace TaxVision.Customer.Tests.Application;

// Los rechazos de Auth vuelven al CRM tal cual, incluida la espera del 429 por objetivo (TargetRateLimited).
public sealed class CustomerPortalAccessClientTests
{
    [Fact]
    public async Task A_target_rate_limit_keeps_the_wait_auth_sent()
    {
        var client = new CustomerPortalAccessClient(
            new HttpClient(
                new StubHandler(
                    HttpStatusCode.TooManyRequests,
                    """{"code":"Auth.CredentialAdmin.TargetRateLimited","message":"Too many.","retryAfterSeconds":3600}"""
                )
            )
            {
                BaseAddress = new Uri("http://auth.local/"),
            },
            new StaticToken(),
            new ActorToken(),
            NullLogger<CustomerPortalAccessClient>.Instance
        );

        var result = await client.SendResetLinkAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new PortalCredentialRequest(Guid.NewGuid(), DateTime.UtcNow, "1.2.3.4", "UA")
        );

        Assert.Equal("Auth.CredentialAdmin.TargetRateLimited", result.Error.Code);
        Assert.Equal(3600, result.Error.RetryAfterSeconds);
    }

    // Un motivo inválido vuelve como 400 InvalidReason, no como Customer.PortalAccessUnavailable (503).
    [Fact]
    public async Task An_invalid_reason_from_auth_keeps_its_code_and_status()
    {
        var client = new CustomerPortalAccessClient(
            new HttpClient(
                new StubHandler(
                    HttpStatusCode.BadRequest,
                    """{"code":"Auth.CredentialAdmin.InvalidReason","message":"The reason is not valid."}"""
                )
            )
            {
                BaseAddress = new Uri("http://auth.local/"),
            },
            new StaticToken(),
            new ActorToken(),
            NullLogger<CustomerPortalAccessClient>.Instance
        );
        var request = new PortalCredentialRequest(Guid.NewGuid(), DateTime.UtcNow, "1.2.3.4", "UA", "NotAReason");

        var errors = new[]
        {
            (await client.RequireChangeAsync(Guid.NewGuid(), Guid.NewGuid(), request)).Error,
            (await client.SecureAsync(Guid.NewGuid(), Guid.NewGuid(), request)).Error,
            (await client.ResetMfaAsync(Guid.NewGuid(), Guid.NewGuid(), request)).Error,
        };

        Assert.All(
            errors,
            error =>
            {
                Assert.Equal("Auth.CredentialAdmin.InvalidReason", error.Code);
                Assert.Equal(400, error.ToHttpStatusCode());
            }
        );
    }

    // SR-03: además del token de servicio, cada acción sobre la credencial lleva el token del actor.
    [Fact]
    public async Task Credential_calls_forward_the_actor_token_in_its_own_header()
    {
        var handler = new CapturingHandler();
        var client = new CustomerPortalAccessClient(
            new HttpClient(handler) { BaseAddress = new Uri("http://auth.local/") },
            new StaticToken(),
            new ActorToken(),
            NullLogger<CustomerPortalAccessClient>.Instance
        );
        var request = new PortalCredentialRequest(Guid.NewGuid(), DateTime.UtcNow, "1.2.3.4", "UA", "OfficePolicy");
        var (tenantId, customerId) = (Guid.NewGuid(), Guid.NewGuid());

        await client.GetStatusAsync(tenantId, customerId, request.RequestedByUserId);
        await client.SendResetLinkAsync(tenantId, customerId, request);
        await client.CancelResetLinkAsync(tenantId, customerId, request);
        await client.RequireChangeAsync(tenantId, customerId, request);
        await client.SecureAsync(tenantId, customerId, request);
        await client.ResetMfaAsync(tenantId, customerId, request);

        Assert.Equal(6, handler.Requests.Count);
        Assert.All(
            handler.Requests,
            sent =>
            {
                Assert.Equal("Bearer token", sent.Authorization);
                Assert.Equal("Bearer actor-token", sent.ActorAuthorization);
            }
        );
    }

    // SR-11: sesiones e invitación del portal también llevan el token del actor.
    [Fact]
    public async Task Session_and_invitation_calls_forward_the_actor_token_in_its_own_header()
    {
        var handler = new CapturingHandler();
        var client = new CustomerPortalAccessClient(
            new HttpClient(handler) { BaseAddress = new Uri("http://auth.local/") },
            new StaticToken(),
            new ActorToken(),
            NullLogger<CustomerPortalAccessClient>.Instance
        );
        var (tenantId, customerId, actorId) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        await client.ListSessionsAsync(tenantId, customerId, new PortalSessionsQuery(null, null, null, null));
        await client.RevokeSessionAsync(tenantId, customerId, Guid.NewGuid(), actorId);
        await client.RevokeAllSessionsAsync(tenantId, customerId, actorId, forgetTrustedDevices: false);
        await client.IssueInvitationAsync(tenantId, customerId, "client@example.test", "Ana", "Ruiz", actorId);

        Assert.Equal(4, handler.Requests.Count);
        Assert.All(
            handler.Requests,
            sent =>
            {
                Assert.Equal("Bearer token", sent.Authorization);
                Assert.Equal("Bearer actor-token", sent.ActorAuthorization);
            }
        );
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public List<(string? Authorization, string? ActorAuthorization)> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(
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

    private sealed class ActorToken : IActorAccessTokenReader
    {
        public string? Current => "actor-token";
    }

    private sealed class StaticToken : IServiceTokenAcquirer
    {
        public Task<string?> GetTokenAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<string?>("token");
    }

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(
                new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") }
            );
    }
}
