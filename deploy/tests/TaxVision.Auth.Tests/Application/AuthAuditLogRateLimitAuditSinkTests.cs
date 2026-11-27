using BuildingBlocks.Web.RateLimiting;
using TaxVision.Auth.Api.RateLimiting;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.Audit;
using Xunit;

namespace TaxVision.Auth.Tests.Application;

/// <summary>El 429 auditado lleva el tipo de actor del claim, como el resto de la bitácora (H-C07).</summary>
public sealed class AuthAuditLogRateLimitAuditSinkTests
{
    [Fact]
    public async Task The_blocked_row_records_the_actor_type_of_the_request()
    {
        var writer = new FakeAuthAuditWriter();
        var sink = new AuthAuditLogRateLimitAuditSink(writer, new FakeUnitOfWork(), new ActorRequestContext());

        await sink.OnBlockedAsync(
            new RateLimitAuditContext(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "auth.n.credential_admin",
                "203.0.113.10",
                "xunit",
                "corr"
            )
        );

        var log = Assert.Single(writer.Logs);
        Assert.Equal(AuthAuditAction.RateLimitBlocked, log.Action);
        Assert.Equal("TenantAdmin", log.ActorType);
    }

    private sealed class ActorRequestContext : IRequestContext
    {
        public string? IpAddress => "203.0.113.10";
        public string? UserAgent => "xunit";
        public string? ActorType => "TenantAdmin";
    }
}
