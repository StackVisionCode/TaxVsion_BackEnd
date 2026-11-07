using BuildingBlocks.Common;
using BuildingBlocks.Messaging.TenantIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Auth.Application.Tenants.IntegrationEvents;
using TaxVision.Auth.Domain.Tenants;

namespace TaxVision.Auth.Tests.Application;

public sealed class TenantUpdatedConsumerTests
{
    private static readonly DateTime T0 = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Applies_the_rename_and_time_zone_and_saves()
    {
        var tenant = Tenant.Register(Guid.NewGuid(), "Acme Tax", "acme-tax", TenantKind.Customer, "Etc/UTC").Value;
        var registry = new InMemoryTenantRegistry();
        registry.Add(tenant);
        var unitOfWork = new CountingUnitOfWork();

        await HandleAsync(Event(tenant.Id, "Acme Tax Group", "America/Chicago", T0), registry, unitOfWork);

        Assert.Equal("Acme Tax Group", tenant.Name);
        Assert.Equal("America/Chicago", tenant.DefaultTimeZoneId);
        Assert.Equal(1, unitOfWork.Saves);
    }

    [Fact]
    public async Task A_stale_event_is_ignored_without_saving()
    {
        var tenant = Tenant.Register(Guid.NewGuid(), "Acme Tax", "acme-tax", TenantKind.Customer, "Etc/UTC").Value;
        tenant.ApplyTenantUpdate("Newest", "America/Chicago", T0);
        var registry = new InMemoryTenantRegistry();
        registry.Add(tenant);
        var unitOfWork = new CountingUnitOfWork();

        await HandleAsync(Event(tenant.Id, "Older", "America/Denver", T0.AddSeconds(-1)), registry, unitOfWork);

        Assert.Equal("Newest", tenant.Name);
        Assert.Equal(0, unitOfWork.Saves);
    }

    [Fact]
    public async Task An_older_version_is_ignored_even_with_a_newer_clock()
    {
        var tenant = Tenant.Register(Guid.NewGuid(), "Acme Tax", "acme-tax", TenantKind.Customer, "Etc/UTC").Value;
        tenant.ApplyTenantUpdate("Newest", "America/Chicago", T0, tenantVersion: 6);
        var registry = new InMemoryTenantRegistry();
        registry.Add(tenant);
        var unitOfWork = new CountingUnitOfWork();

        await HandleAsync(
            Event(tenant.Id, "Older", "America/Denver", T0.AddSeconds(1)) with { TenantVersion = 5 },
            registry,
            unitOfWork
        );

        Assert.Equal("Newest", tenant.Name);
        Assert.Equal(0, unitOfWork.Saves);
    }

    [Fact]
    public async Task Unknown_tenant_is_a_noop()
    {
        var unitOfWork = new CountingUnitOfWork();

        await HandleAsync(Event(Guid.NewGuid(), "Acme", "Etc/UTC", T0), new InMemoryTenantRegistry(), unitOfWork);

        Assert.Equal(0, unitOfWork.Saves);
    }

    private static TenantUpdatedIntegrationEvent Event(Guid tenantId, string name, string zone, DateTime at) =>
        new()
        {
            TenantId = tenantId,
            UpdatedTenantId = tenantId,
            Name = name,
            DefaultTimeZoneId = zone,
            UpdatedAtUtc = at,
        };

    private static Task HandleAsync(
        TenantUpdatedIntegrationEvent evt,
        InMemoryTenantRegistry registry,
        CountingUnitOfWork unitOfWork
    ) =>
        TenantUpdatedConsumer.Handle(
            evt,
            registry,
            unitOfWork,
            new NoopCorrelation(),
            NullLogger<TenantUpdatedIntegrationEvent>.Instance,
            CancellationToken.None
        );

    private sealed class CountingUnitOfWork : IUnitOfWork
    {
        public int Saves { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            Saves++;
            return Task.FromResult(1);
        }
    }

    private sealed class NoopCorrelation : ICorrelationContext
    {
        public string CorrelationId => "test";

        public void Set(string correlationId) { }

        public IDisposable Push(string correlationId) => new Scope();

        private sealed class Scope : IDisposable
        {
            public void Dispose() { }
        }
    }
}
