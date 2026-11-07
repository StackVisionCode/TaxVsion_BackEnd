using BuildingBlocks.Messaging;
using BuildingBlocks.Persistence;
using BuildingBlocks.Tenancy;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Tenants.IntegrationEvents;
using TaxVision.Auth.Domain.Tenants;

namespace TaxVision.Auth.Tests.Application;

/// <summary>TEN-4: los avisos de estado se ordenan por la versión de la oficina.</summary>
public sealed class TenantStatusChangedConsumerTests
{
    private readonly Tenant _tenant = Tenant
        .Register(Guid.NewGuid(), "Acme Tax", "acme-tax", TenantKind.Customer, "Etc/UTC")
        .Value;
    private readonly InMemoryTenantRegistry _registry = new();
    private readonly CountingSessions _sessions = new();

    public TenantStatusChangedConsumerTests() => _registry.Add(_tenant);

    [Fact]
    public async Task Handle_OlderVersionAfterNewer_IsIgnored()
    {
        await HandleAsync(Status(isActive: true, version: 6));
        await HandleAsync(Status(isActive: false, version: 5));

        Assert.True(_tenant.IsActive);
        Assert.Equal(0, _sessions.TenantRevocations);
    }

    [Fact]
    public async Task Handle_NewerVersion_IsApplied()
    {
        await HandleAsync(Status(isActive: true, version: 5));
        await HandleAsync(Status(isActive: false, version: 6));

        Assert.False(_tenant.IsActive);
        Assert.Equal(1, _sessions.TenantRevocations);
    }

    [Fact]
    public async Task Handle_SameVersionRepeated_CutsSessionsOnce()
    {
        await HandleAsync(Status(isActive: false, version: 5));
        await HandleAsync(Status(isActive: false, version: 5));

        Assert.Equal(1, _sessions.TenantRevocations);
    }

    [Fact]
    public async Task Handle_MessageWithoutVersion_KeepsApplyingAsBefore()
    {
        await HandleAsync(Status(isActive: true, version: 6));
        await HandleAsync(Status(isActive: false, version: null));

        Assert.False(_tenant.IsActive);
    }

    [Fact]
    public async Task Handle_StatusOlderThanARename_IsStillApplied()
    {
        // La versión es una sola por oficina: el renombre v6 no dice nada del estado v5.
        _tenant.ApplyTenantUpdate("Renamed", "Etc/UTC", DateTime.UtcNow, tenantVersion: 6);

        await HandleAsync(Status(isActive: false, version: 5));

        Assert.False(_tenant.IsActive);
    }

    private TenantStatusChangedIntegrationEvent Status(bool isActive, long? version) =>
        new()
        {
            TenantId = _tenant.Id,
            ChangedTenantId = _tenant.Id,
            Status = isActive ? "Active" : "Suspended",
            IsActive = isActive,
            TenantVersion = version,
        };

    private Task HandleAsync(TenantStatusChangedIntegrationEvent evt) =>
        TenantStatusChangedConsumer.Handle(
            evt,
            _registry,
            _sessions,
            new RecordingAccessTokenDenylist(),
            new QuietRevocations(),
            new NoSaves(),
            new FakeCorrelationContext(),
            CancellationToken.None
        );

    private sealed class CountingSessions : EmptyUserSessionRepository
    {
        public int TenantRevocations { get; private set; }

        public override Task<int> RevokeAllForTenantAsync(Guid tenantId, string reason, CancellationToken ct = default)
        {
            TenantRevocations++;
            return Task.FromResult(0);
        }
    }

    private sealed class NoSaves : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class QuietRevocations : ISessionRevocationPublisher
    {
        public Task PublishRevokedAsync(
            Guid tenantId,
            Guid userId,
            Guid sessionId,
            string reason,
            CancellationToken ct = default
        ) => Task.CompletedTask;
    }
}
