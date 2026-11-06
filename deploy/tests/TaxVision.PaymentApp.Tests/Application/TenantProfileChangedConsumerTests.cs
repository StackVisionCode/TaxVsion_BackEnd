using BuildingBlocks.Common;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.TenantIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Tenancy;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Tenants.IntegrationEvents;
using TaxVision.PaymentApp.Domain.Tenants;

namespace TaxVision.PaymentApp.Tests.Application;

/// <summary>La oficina guardó su perfil: la proyección copia el "Billed to" del recibo, ordenado por versión.</summary>
public sealed class TenantProfileChangedConsumerTests
{
    private static readonly DateTime T0 = new(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task A_profile_copies_legal_name_ein_and_postal_address_and_saves_once()
    {
        var tenant = RegisterTenant();
        var unitOfWork = new CountingUnitOfWork();

        await HandleAsync(Event(tenant.Id, version: 1), new SingleTenantRegistry(tenant), unitOfWork);

        Assert.Equal("Acme Tax LLC", tenant.BillingLegalName);
        Assert.Equal("12-3456789", tenant.BillingEin);
        Assert.Equal("1 Main St, Suite 2, Miami, FL 33101", tenant.BillingAddress);
        Assert.Equal(1, tenant.ProfileVersion);
        Assert.Equal(1, unitOfWork.SaveCalls);
    }

    // La postal manda sobre la física; sin postal se usa la física.
    [Fact]
    public async Task The_mailing_address_wins_over_the_physical_one()
    {
        var tenant = RegisterTenant();
        var unitOfWork = new CountingUnitOfWork();
        var evt = Event(tenant.Id, version: 1) with
        {
            MailingAddress = new OfficeAddressData("PO Box 77", null, "Orlando", "FL", "32801", "US"),
        };

        await HandleAsync(evt, new SingleTenantRegistry(tenant), unitOfWork);

        Assert.Equal("PO Box 77, Orlando, FL 32801", tenant.BillingAddress);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task An_older_or_equal_version_is_ignored(long version)
    {
        var tenant = RegisterTenant();
        tenant.ApplyProfile(2, "Newest LLC", "98-7654321", "9 Last St, Tampa, FL 33602", T0);
        var unitOfWork = new CountingUnitOfWork();

        await HandleAsync(Event(tenant.Id, version), new SingleTenantRegistry(tenant), unitOfWork);

        Assert.Equal("Newest LLC", tenant.BillingLegalName);
        Assert.Equal("98-7654321", tenant.BillingEin);
        Assert.Equal(2, tenant.ProfileVersion);
        Assert.Equal(0, unitOfWork.SaveCalls);
    }

    // TEN-10: el perfil llegó antes que TenantCreated; se reintenta, no se descarta.
    [Fact]
    public async Task Handle_TenantRowMissing_ThrowsDependencyNotReady()
    {
        var unitOfWork = new CountingUnitOfWork();

        var error = await Assert.ThrowsAsync<DependencyNotReadyException>(() =>
            HandleAsync(Event(Guid.NewGuid(), version: 1), new SingleTenantRegistry(null), unitOfWork)
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

    private static TenantProfileChangedIntegrationEvent Event(Guid tenantId, long version) =>
        new()
        {
            TenantId = tenantId,
            ProfileTenantId = tenantId,
            Version = version,
            UpdatedAtUtc = T0,
            LegalName = "Acme Tax LLC",
            DisplayName = "Acme Tax",
            Ein = "12-3456789",
            PhysicalAddress = new OfficeAddressData("1 Main St", "Suite 2", "Miami", "FL", "33101", "US"),
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
