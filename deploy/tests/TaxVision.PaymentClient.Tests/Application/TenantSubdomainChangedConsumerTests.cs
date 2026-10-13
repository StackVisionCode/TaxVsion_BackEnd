using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Tenancy;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Application.Tenants.IntegrationEvents;
using TaxVision.PaymentClient.Domain.Tenants;

namespace TaxVision.PaymentClient.Tests.Application;

/// <summary>
/// La oficina renombró su subdominio primario: la proyección local guarda el slug nuevo, que es con el
/// que se arman los hosts públicos de la oficina. Un subdominio secundario no toca el slug.
/// </summary>
public sealed class TenantSubdomainChangedConsumerTests
{
    [Fact]
    public async Task A_primary_rename_updates_the_slug_and_saves_once()
    {
        var tenant = RegisterTenant("acme-tax");
        var unitOfWork = new CountingUnitOfWork();

        await HandleAsync(Event(tenant.Id, isPrimary: true), new SingleTenantRegistry(tenant), unitOfWork);

        Assert.Equal("acme-new", tenant.SubDomain);
        Assert.Equal(1, unitOfWork.SaveCalls);
    }

    [Fact]
    public async Task A_non_primary_rename_changes_nothing()
    {
        var tenant = RegisterTenant("acme-tax");
        var unitOfWork = new CountingUnitOfWork();

        await HandleAsync(Event(tenant.Id, isPrimary: false), new SingleTenantRegistry(tenant), unitOfWork);

        Assert.Equal("acme-tax", tenant.SubDomain);
        Assert.Equal(0, unitOfWork.SaveCalls);
    }

    [Fact]
    public async Task An_unknown_tenant_is_a_noop()
    {
        var unitOfWork = new CountingUnitOfWork();

        await HandleAsync(Event(Guid.NewGuid(), isPrimary: true), new SingleTenantRegistry(null), unitOfWork);

        Assert.Equal(0, unitOfWork.SaveCalls);
    }

    private static Task HandleAsync(
        TenantSubdomainChangedIntegrationEvent evt,
        ITenantRegistry tenants,
        CountingUnitOfWork unitOfWork
    ) =>
        TenantSubdomainChangedConsumer.Handle(
            evt,
            tenants,
            unitOfWork,
            new NoOpCorrelationContext(),
            CancellationToken.None
        );

    private static Tenant RegisterTenant(string subDomain) =>
        Tenant
            .Register(Guid.NewGuid(), "Acme Tax Office", subDomain, TenantKind.Customer, "UTC", DateTime.UtcNow)
            .Value;

    private static TenantSubdomainChangedIntegrationEvent Event(Guid tenantId, bool isPrimary) =>
        new()
        {
            TenantId = tenantId,
            DomainId = Guid.NewGuid(),
            OldHost = "acme-tax.taxproffice.com",
            NewHost = "acme-new.taxproffice.com",
            OldSlug = "acme-tax",
            NewSlug = "acme-new",
            IsPrimary = isPrimary,
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
