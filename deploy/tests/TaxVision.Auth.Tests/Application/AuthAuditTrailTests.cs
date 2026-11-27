using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Audit.Queries;
using TaxVision.Auth.Application.Common;
using TaxVision.Auth.Domain.Audit;
using Xunit;

namespace TaxVision.Auth.Tests.Application;

/// <summary>La bitácora guarda el tipo de actor del claim (H-C07) y la consulta filtra por objetivo.</summary>
public sealed class AuthAuditTrailTests
{
    [Fact]
    public async Task The_trail_records_the_actor_type_of_the_request()
    {
        var writer = new FakeAuthAuditWriter();
        var trail = new AuthAuditTrail(writer, new ActorRequestContext("TenantAdmin"), new FakeCorrelationContext());

        await trail.RecordAsync(Guid.NewGuid(), Guid.NewGuid(), "auth.test", true, CancellationToken.None);

        Assert.Equal("TenantAdmin", Assert.Single(writer.Logs).ActorType);
    }

    [Fact]
    public async Task An_anonymous_request_leaves_the_actor_type_empty()
    {
        var writer = new FakeAuthAuditWriter();
        var trail = new AuthAuditTrail(writer, new FakeRequestContext(), new FakeCorrelationContext());

        await trail.RecordAsync(Guid.NewGuid(), null, "auth.test", true, CancellationToken.None);

        Assert.Null(Assert.Single(writer.Logs).ActorType);
    }

    [Fact]
    public async Task The_audit_query_passes_the_target_filter_to_the_reader()
    {
        var reader = new RecordingReader();
        var tenantId = Guid.NewGuid();
        var targetId = Guid.NewGuid();

        var result = await GetAuditLogsHandler.Handle(
            new GetAuditLogsQuery(tenantId, TargetId: targetId),
            reader,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal((tenantId, targetId), (reader.TenantId, reader.TargetId));
    }

    private sealed class ActorRequestContext(string actorType) : IRequestContext
    {
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "xunit";
        public string? ActorType => actorType;
    }

    private sealed class RecordingReader : IAuthAuditReader
    {
        public Guid TenantId { get; private set; }
        public Guid? TargetId { get; private set; }

        public Task<(IReadOnlyList<AuthAuditLog> Items, int TotalCount)> GetPagedAsync(
            Guid tenantId,
            Guid? userId,
            string? action,
            DateTime? fromUtc,
            DateTime? toUtc,
            int page,
            int size,
            CancellationToken ct = default,
            Guid? targetId = null
        )
        {
            (TenantId, TargetId) = (tenantId, targetId);
            return Task.FromResult<(IReadOnlyList<AuthAuditLog>, int)>(([], 0));
        }
    }
}
