using BuildingBlocks.Common;
using BuildingBlocks.Messaging.TenantIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Tenancy;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Tenants.IntegrationEvents;
using TaxVision.PaymentApp.Domain.Tenants;

namespace TaxVision.PaymentApp.Tests.Application;

/// <summary>La oficina cambió de nombre o zona horaria: la proyección local los copia en orden.</summary>
public sealed class TenantUpdatedConsumerTests
{
    private static readonly DateTime T0 = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task An_update_copies_name_and_time_zone_and_saves_once()
    {
        var tenant = RegisterTenant();
        var unitOfWork = new CountingUnitOfWork();

        await HandleAsync(
            Event(tenant.Id, "Acme Tax Group", "America/Chicago", T0),
            new SingleTenantRegistry(tenant),
            unitOfWork
        );

        Assert.Equal("Acme Tax Group", tenant.Name);
        Assert.Equal("America/Chicago", tenant.DefaultTimeZoneId);
        Assert.Equal(T0, tenant.SourceUpdatedAtUtc);
        Assert.Equal(1, unitOfWork.SaveCalls);
    }

    [Fact]
    public async Task A_stale_update_is_ignored()
    {
        var tenant = RegisterTenant();
        tenant.ApplyTenantUpdate("Newest", "America/Chicago", T0, T0);
        var unitOfWork = new CountingUnitOfWork();

        await HandleAsync(
            Event(tenant.Id, "Older", "America/Denver", T0.AddSeconds(-1)),
            new SingleTenantRegistry(tenant),
            unitOfWork
        );

        Assert.Equal("Newest", tenant.Name);
        Assert.Equal("America/Chicago", tenant.DefaultTimeZoneId);
        Assert.Equal(0, unitOfWork.SaveCalls);
    }

    [Fact]
    public async Task An_invalid_time_zone_changes_nothing()
    {
        var tenant = RegisterTenant();
        var unitOfWork = new CountingUnitOfWork();

        await HandleAsync(
            Event(tenant.Id, "Acme Tax Group", "Not/AZone", T0),
            new SingleTenantRegistry(tenant),
            unitOfWork
        );

        Assert.Equal("Acme Tax Office", tenant.Name);
        Assert.Equal(0, unitOfWork.SaveCalls);
    }

    [Fact]
    public async Task An_unknown_tenant_is_a_noop()
    {
        var unitOfWork = new CountingUnitOfWork();

        await HandleAsync(Event(Guid.NewGuid(), "Acme", "Etc/UTC", T0), new SingleTenantRegistry(null), unitOfWork);

        Assert.Equal(0, unitOfWork.SaveCalls);
    }

    private static Task HandleAsync(
        TenantUpdatedIntegrationEvent evt,
        ITenantRegistry tenants,
        CountingUnitOfWork unitOfWork
    ) => TenantUpdatedConsumer.Handle(evt, tenants, unitOfWork, new NoOpCorrelationContext(), CancellationToken.None);

    private static Tenant RegisterTenant() =>
        Tenant
            .Register(Guid.NewGuid(), "Acme Tax Office", "acme-tax", TenantKind.Customer, "Etc/UTC", T0.AddDays(-1))
            .Value;

    private static TenantUpdatedIntegrationEvent Event(Guid tenantId, string name, string zone, DateTime at) =>
        new()
        {
            TenantId = tenantId,
            UpdatedTenantId = tenantId,
            Name = name,
            DefaultTimeZoneId = zone,
            UpdatedAtUtc = at,
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
