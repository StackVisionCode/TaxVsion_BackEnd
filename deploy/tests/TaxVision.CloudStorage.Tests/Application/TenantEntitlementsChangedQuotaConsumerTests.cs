using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Application.Configuration;
using TaxVision.CloudStorage.Application.Quotas;
using TaxVision.CloudStorage.Domain.Quotas;

namespace TaxVision.CloudStorage.Tests.Application;

public sealed class TenantEntitlementsChangedQuotaConsumerTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly FakeStorageLimitRepository _limits = new();
    private readonly FakeUnitOfWork _unitOfWork = new();

    [Fact]
    public async Task Handle_OlderRevisionAfterNewer_IsIgnored()
    {
        await HandleAsync(Snapshot(5, "pro", "Active", 2_000));
        await HandleAsync(Snapshot(4, "starter", "Suspended", 1_000));

        var limit = await _limits.GetAsync(_tenantId, CancellationToken.None);
        Assert.False(limit!.IsSuspended);
        Assert.Equal("pro", limit.PlanCode);
        Assert.Equal(2_000, limit.MaxBytes);
    }

    [Fact]
    public async Task Handle_NewerRevision_IsApplied()
    {
        await HandleAsync(Snapshot(4, "pro", "Active", 2_000));
        await HandleAsync(Snapshot(5, "starter", "Suspended", 1_000));

        var limit = await _limits.GetAsync(_tenantId, CancellationToken.None);
        Assert.True(limit!.IsSuspended);
        Assert.Equal("starter", limit.PlanCode);
        Assert.Equal(5, limit.LastEntitlementsRevision);
    }

    [Fact]
    public async Task Handle_FirstSnapshot_CreatesTheRowWithItsRevision()
    {
        await HandleAsync(Snapshot(3, "starter", "Suspended", 1_000));

        var limit = await _limits.GetAsync(_tenantId, CancellationToken.None);
        Assert.Equal(3, limit!.LastEntitlementsRevision);
        Assert.True(limit!.IsSuspended);
    }

    private Task HandleAsync(TenantEntitlementsChangedIntegrationEvent message) =>
        TenantEntitlementsChangedQuotaConsumer.Handle(
            message,
            _limits,
            Options.Create(new CloudStorageOptions()),
            _unitOfWork,
            new FakeCorrelation(),
            CancellationToken.None
        );

    private TenantEntitlementsChangedIntegrationEvent Snapshot(long revision, string plan, string status, long bytes) =>
        new()
        {
            TenantId = _tenantId,
            CorrelationId = "corr-entitlements",
            RevisionNumber = revision,
            ChangedKeys = [],
            PlanCode = plan,
            SubscriptionStatus = status,
            SeatCount = 1,
            AvailableSeatCount = 1,
            EntitlementValues = new Dictionary<string, string> { ["storage.max_bytes"] = bytes.ToString() },
        };

    private sealed class FakeCorrelation : ICorrelationContext
    {
        public string CorrelationId => "corr-entitlements";

        public void Set(string correlationId) { }

        public IDisposable Push(string correlationId) => new Noop();

        private sealed class Noop : IDisposable
        {
            public void Dispose() { }
        }
    }
}
