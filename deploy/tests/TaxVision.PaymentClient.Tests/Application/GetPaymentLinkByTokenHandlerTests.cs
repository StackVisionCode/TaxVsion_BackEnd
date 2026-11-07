using BuildingBlocks.Tenancy;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Application.Abstractions.Payments;
using TaxVision.PaymentClient.Application.PaymentLinks.Queries;
using TaxVision.PaymentClient.Domain.Payables;
using TaxVision.PaymentClient.Domain.PaymentLinks;
using TaxVision.PaymentClient.Domain.TenantPaymentConfigs;
using TaxVision.PaymentClient.Domain.Tenants;
using TaxVision.PaymentClient.Domain.ValueObjects;

namespace TaxVision.PaymentClient.Tests.Application;

/// <summary>El checkout público muestra el nombre que ve el cliente: el comercial si la oficina lo tiene.</summary>
public sealed class GetPaymentLinkByTokenHandlerTests
{
    private static readonly DateTime NowUtc = DateTime.UtcNow;

    [Fact]
    public async Task The_checkout_shows_the_display_name_when_the_office_has_one()
    {
        var tenant = RegisterTenant();
        tenant.ApplyProfile(1, "Acme Refunds", NowUtc);

        var response = await HandleAsync(tenant);

        Assert.Equal("Acme Refunds", response.TenantName);
    }

    [Fact]
    public async Task Without_a_display_name_the_checkout_shows_the_office_name()
    {
        var tenant = RegisterTenant();

        var response = await HandleAsync(tenant);

        Assert.Equal("Acme Tax Office", response.TenantName);
    }

    [Fact]
    public async Task The_checkout_carries_the_office_subdomain_so_the_page_can_show_its_logo()
    {
        var response = await HandleAsync(RegisterTenant());

        Assert.Equal("acme-tax", response.TenantBrandingKey);
    }

    private static async Task<PaymentLinkCheckoutResponse> HandleAsync(Tenant tenant)
    {
        var link = PaymentLink
            .Create(
                tenant.Id,
                null,
                Money.Create(1999, "USD").Value,
                PaymentPurpose.Create(PaymentPurposeKind.Other, null).Value,
                PaymentLinkToken.Generate(),
                TimeSpan.FromDays(7),
                Guid.Empty,
                NowUtc
            )
            .Value;

        var result = await GetPaymentLinkByTokenHandler.Handle(
            new GetPaymentLinkByTokenQuery("token"),
            new SingleLinkRepository(link),
            new NoPayables(),
            new NoConfigs(),
            new NoAdapters(),
            new SingleTenantRegistry(tenant),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private static Tenant RegisterTenant() =>
        Tenant.Register(Guid.NewGuid(), "Acme Tax Office", "acme-tax", TenantKind.Customer, "Etc/UTC", NowUtc).Value;

    private sealed class SingleLinkRepository(PaymentLink link) : IPaymentLinkRepository
    {
        public Task<PaymentLink?> GetByTokenAsync(string token, CancellationToken ct = default) =>
            Task.FromResult<PaymentLink?>(link);

        public Task<PaymentLink?> GetByIdAsync(Guid paymentLinkId, Guid tenantId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<PaymentLink?> GetByRelatedTenantPaymentIdAsync(
            Guid tenantPaymentId,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<PaymentLink?> GetActiveByExternalReferenceAsync(
            Guid tenantId,
            string externalReferenceId,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<bool> AnyUsedForExternalReferenceAsync(
            Guid tenantId,
            string externalReferenceId,
            DateTime? usedSinceUtc = null,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<IReadOnlyList<PaymentLink>> SearchByTenantAsync(
            Guid tenantId,
            PaymentLinkStatus? status,
            int page,
            int pageSize,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<IReadOnlyList<PaymentLink>> GetActiveExpiredBeforeAsync(
            DateTime cutoffUtc,
            int batchSize,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task AddAsync(PaymentLink link, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class NoPayables : IPayableReferenceRepository
    {
        public Task<PayableReference?> GetByReferenceAsync(string reference, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<PayableReference?> GetByExternalReferenceAsync(
            Guid tenantId,
            PaymentPurposeKind kind,
            string externalReferenceId,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task AddAsync(PayableReference payable, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class NoConfigs : ITenantPaymentConfigRepository
    {
        public Task<IReadOnlyList<TenantPaymentConfig>> GetActiveByTenantAsync(
            Guid tenantId,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<TenantPaymentConfig>>([]);

        public Task<TenantPaymentConfig?> GetByTenantAndProviderAsync(
            Guid tenantId,
            PaymentProviderCode code,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<TenantPaymentConfig?> GetByIdAsync(
            Guid tenantPaymentConfigId,
            Guid tenantId,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<IReadOnlyList<TenantPaymentConfig>> GetAllByTenantAsync(
            Guid tenantId,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task AddAsync(TenantPaymentConfig config, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<TenantPaymentConfig>> GetStalePendingWebhookSetupsAsync(
            DateTime requestedBeforeUtc,
            int batchSize,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public void Remove(TenantPaymentConfig config) => throw new NotSupportedException();
    }

    private sealed class NoAdapters : IPaymentAdapterFactory
    {
        public IPaymentProvider Resolve(PaymentProviderCode code) => throw new NotSupportedException();

        public bool IsRegistered(PaymentProviderCode code) => false;

        public IReadOnlyList<PaymentProviderDescriptor> GetAvailableProviders() => [];

        public PaymentProviderDescriptor? FindDescriptor(PaymentProviderCode code) => null;
    }

    private sealed class SingleTenantRegistry(Tenant tenant) : ITenantRegistry
    {
        public Task<Tenant?> GetByIdAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<Tenant?>(tenant.Id == tenantId ? tenant : null);

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
            long? tenantVersion,
            CancellationToken ct = default
        ) => throw new NotSupportedException();
    }
}
