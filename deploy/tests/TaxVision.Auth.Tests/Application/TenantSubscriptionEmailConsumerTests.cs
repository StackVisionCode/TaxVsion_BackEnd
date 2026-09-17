using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Subscriptions.IntegrationEvents;
using TaxVision.Auth.Application.TenantDomains;
using TaxVision.Auth.Domain.Tenants;
using TaxVision.Auth.Domain.Users;
using Xunit;

namespace TaxVision.Auth.Tests.Application;

/// <summary>
/// Fase 3 del plan de Expiración/Dunning: Auth resuelve el admin/owner y republica el email-request para
/// que Notification lo envíe. Cubre qué estados notifican (GracePeriod/Suspended/Expired y Active solo por
/// recuperación) y que el RenewUrl se arma con el subdominio del tenant.
/// </summary>
public sealed class TenantSubscriptionEmailConsumerTests
{
    [Fact]
    public async Task GracePeriod_publishes_email_request_for_primary_admin_with_subdomain_url()
    {
        var tenantId = Guid.NewGuid();
        var bus = new FakeMessageBus();

        await TenantSubscriptionEmailConsumer.Handle(
            StatusEvent(tenantId, "GracePeriod", nameof(SubscriptionChangeReason.RenewalPaymentFailed)),
            AdminRepo(tenantId),
            TenantRegistry(tenantId, "coretaxpro"),
            bus,
            Domain("taxproffice.com"),
            new NoopCorrelationContext(),
            NullLogger<TenantSubscriptionEmailRequestedIntegrationEvent>.Instance,
            CancellationToken.None
        );

        var request = Assert.IsType<TenantSubscriptionEmailRequestedIntegrationEvent>(Assert.Single(bus.Published));
        Assert.Equal("owner@office.com", request.Email);
        Assert.Equal("Carlos", request.FirstName);
        Assert.Equal("GracePeriod", request.Status);
        Assert.Equal("https://coretaxpro.taxproffice.com", request.RenewUrl);
    }

    [Fact]
    public async Task Active_by_recovery_publishes_reactivated_email()
    {
        var tenantId = Guid.NewGuid();
        var bus = new FakeMessageBus();

        await TenantSubscriptionEmailConsumer.Handle(
            StatusEvent(tenantId, "Active", nameof(SubscriptionChangeReason.PaymentRecovered)),
            AdminRepo(tenantId),
            TenantRegistry(tenantId, "coretaxpro"),
            bus,
            Domain("taxproffice.com"),
            new NoopCorrelationContext(),
            NullLogger<TenantSubscriptionEmailRequestedIntegrationEvent>.Instance,
            CancellationToken.None
        );

        var request = Assert.IsType<TenantSubscriptionEmailRequestedIntegrationEvent>(Assert.Single(bus.Published));
        Assert.Equal("Active", request.Status);
    }

    [Theory]
    [InlineData("Active", nameof(SubscriptionChangeReason.TrialConverted))] // alta normal, no recuperación
    [InlineData("PastDue", nameof(SubscriptionChangeReason.RenewalPaymentFailed))] // transitorio, aún no notifica
    [InlineData("Trialing", nameof(SubscriptionChangeReason.TrialConverted))]
    [InlineData("Cancelled", nameof(SubscriptionChangeReason.CancellationRequested))]
    public async Task Non_notifying_states_publish_nothing(string status, string reason)
    {
        var tenantId = Guid.NewGuid();
        var bus = new FakeMessageBus();

        await TenantSubscriptionEmailConsumer.Handle(
            StatusEvent(tenantId, status, reason),
            AdminRepo(tenantId),
            TenantRegistry(tenantId, "coretaxpro"),
            bus,
            Domain("taxproffice.com"),
            new NoopCorrelationContext(),
            NullLogger<TenantSubscriptionEmailRequestedIntegrationEvent>.Instance,
            CancellationToken.None
        );

        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task No_primary_admin_publishes_nothing()
    {
        var tenantId = Guid.NewGuid();
        var bus = new FakeMessageBus();

        await TenantSubscriptionEmailConsumer.Handle(
            StatusEvent(tenantId, "Suspended", nameof(SubscriptionChangeReason.SuspensionTimeout)),
            new FakeUserRepository(admin: null),
            TenantRegistry(tenantId, "coretaxpro"),
            bus,
            Domain("taxproffice.com"),
            new NoopCorrelationContext(),
            NullLogger<TenantSubscriptionEmailRequestedIntegrationEvent>.Instance,
            CancellationToken.None
        );

        Assert.Empty(bus.Published);
    }

    private static TenantSubscriptionStatusChangedIntegrationEvent StatusEvent(
        Guid tenantId,
        string status,
        string reason
    ) =>
        new()
        {
            TenantId = tenantId,
            TenantSubscriptionId = Guid.NewGuid(),
            Status = status,
            PreviousStatus = "Active",
            Reason = reason,
        };

    private static FakeUserRepository AdminRepo(Guid tenantId) =>
        new(User.Register(tenantId, "Carlos", "Castillo", "owner@office.com", "hash", UserActorType.TenantAdmin).Value);

    private static FakeTenantRegistry TenantRegistry(Guid tenantId, string subDomain) =>
        new(Tenant.Register(tenantId, "Office", subDomain, TenantKind.Customer, "Etc/UTC").Value);

    private static IOptions<TenantDomainOptions> Domain(string baseDomain) =>
        Options.Create(new TenantDomainOptions { BaseDomain = baseDomain });

    private sealed class FakeUserRepository(User? admin) : IUserRepository
    {
        public Task<User?> GetPrimaryAdminAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(admin);

        public Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<User?> GetByEmailAsync(Guid tenantId, string email, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<bool> EmailExistsAsync(Guid tenantId, string email, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<User?> GetByOnboardingIdAsync(Guid onboardingId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Guid>> GetActiveTenantIdsByEmailAsync(string email, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task AddAsync(User user, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<int> CountActiveAsync(Guid tenantId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<(IReadOnlyList<User> Items, int TotalCount)> GetPagedAsync(
            Guid tenantId,
            int page,
            int size,
            string? search,
            bool? isActive,
            Guid? customerId = null,
            CancellationToken ct = default
        ) => throw new NotSupportedException();
    }

    private sealed class FakeTenantRegistry(Tenant tenant) : ITenantRegistry
    {
        public Task<Tenant?> GetByIdAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<Tenant?>(tenant);

        public Task SetBillingBlockedAsync(
            Guid tenantId,
            bool blocked,
            string? reason,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task UpsertCreatedAsync(
            Guid tenantId,
            string name,
            string subDomain,
            TenantKind kind,
            string defaultTimeZoneId,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task SetActiveAsync(Guid tenantId, bool isActive, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class NoopCorrelationContext : ICorrelationContext
    {
        public string CorrelationId => "test-correlation-id";

        public void Set(string correlationId) { }

        public IDisposable Push(string correlationId) => NullDisposable.Instance;

        private sealed class NullDisposable : IDisposable
        {
            public static readonly NullDisposable Instance = new();

            public void Dispose() { }
        }
    }
}
