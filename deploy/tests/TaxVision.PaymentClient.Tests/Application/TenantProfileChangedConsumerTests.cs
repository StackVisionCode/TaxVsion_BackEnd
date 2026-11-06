using BuildingBlocks.Common;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.TenantIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Tenancy;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Application.Tenants.IntegrationEvents;
using TaxVision.PaymentClient.Domain.Tenants;

namespace TaxVision.PaymentClient.Tests.Application;

/// <summary>La oficina guardó su perfil: la proyección local copia el nombre comercial en orden de versión.</summary>
public sealed class TenantProfileChangedConsumerTests
{
    private static readonly DateTime T0 = new(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task A_profile_copies_the_display_name_and_saves_once()
    {
        var tenant = RegisterTenant();
        var unitOfWork = new CountingUnitOfWork();

        await HandleAsync(Event(tenant.Id, 1, "Acme Refunds"), new SingleTenantRegistry(tenant), unitOfWork);

        Assert.Equal("Acme Refunds", tenant.DisplayName);
        Assert.Equal("Acme Refunds", tenant.CustomerFacingName);
        Assert.Equal(1, tenant.ProfileVersion);
        Assert.Equal(1, unitOfWork.SaveCalls);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(2)]
    public async Task A_same_or_older_version_is_ignored(long version)
    {
        var tenant = RegisterTenant();
        tenant.ApplyProfile(3, "Current", T0);
        var unitOfWork = new CountingUnitOfWork();

        await HandleAsync(Event(tenant.Id, version, "Stale"), new SingleTenantRegistry(tenant), unitOfWork);

        Assert.Equal("Current", tenant.DisplayName);
        Assert.Equal(3, tenant.ProfileVersion);
        Assert.Equal(0, unitOfWork.SaveCalls);
    }

    [Fact]
    public async Task An_empty_display_name_falls_back_to_the_office_name()
    {
        var tenant = RegisterTenant();
        tenant.ApplyProfile(1, "Acme Refunds", T0);
        var unitOfWork = new CountingUnitOfWork();

        await HandleAsync(Event(tenant.Id, 2, ""), new SingleTenantRegistry(tenant), unitOfWork);

        Assert.Null(tenant.DisplayName);
        Assert.Equal("Acme Tax Office", tenant.CustomerFacingName);
        Assert.Equal(1, unitOfWork.SaveCalls);
    }

    [Fact]
    public async Task Handle_TenantRowMissing_ThrowsDependencyNotReady()
    {
        var unitOfWork = new CountingUnitOfWork();

        var error = await Assert.ThrowsAsync<DependencyNotReadyException>(() =>
            HandleAsync(Event(Guid.NewGuid(), 1, "Acme"), new SingleTenantRegistry(null), unitOfWork)
        );

        Assert.Equal("Tenant", error.Dependency);
        Assert.Equal(0, unitOfWork.SaveCalls);
    }

    private static Task HandleAsync(
        TenantProfileChangedIntegrationEvent evt,
        ITenantRegistry tenants,
        CountingUnitOfWork unitOfWork
    ) =>
        TenantProfileChangedConsumer.Handle(
            evt,
            tenants,
            unitOfWork,
            new NoOpCorrelationContext(),
            CancellationToken.None
        );

    private static Tenant RegisterTenant() =>
        Tenant
            .Register(Guid.NewGuid(), "Acme Tax Office", "acme-tax", TenantKind.Customer, "Etc/UTC", T0.AddDays(-1))
            .Value;

    private static TenantProfileChangedIntegrationEvent Event(Guid tenantId, long version, string? displayName) =>
        new()
        {
            TenantId = tenantId,
            ProfileTenantId = tenantId,
            Version = version,
            UpdatedAtUtc = T0,
            DisplayName = displayName,
        };

    private sealed class SingleTenantRegistry(Tenant? tenant) : ITenantRegistry
    {
        public Task<Tenant?> GetByIdAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(tenant is not null && tenant.Id == tenantId ? tenant : null);

        public Task UpsertCreatedAsync(
            Guid tenantId,
            string name,
            string subDomain,
            TenantKind kind,
            string defaultTimeZoneId,
            DateTime nowUtc,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task UpdateStatusAsync(
            Guid tenantId,
            string status,
            bool isActive,
            DateTime nowUtc,
            CancellationToken ct = default
        ) => throw new NotSupportedException();
    }

    private sealed class CountingUnitOfWork : IUnitOfWork
    {
        public int SaveCalls { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            SaveCalls++;
            return Task.FromResult(1);
        }
    }

    private sealed class NoOpCorrelationContext : ICorrelationContext
    {
        public string CorrelationId => "test-correlation-id";

        public void Set(string correlationId) { }

        public IDisposable Push(string correlationId) => new NoOpScope();

        private sealed class NoOpScope : IDisposable
        {
            public void Dispose() { }
        }
    }
}
