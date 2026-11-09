using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.BillingIntegrationEvents;
using BuildingBlocks.Platform;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Application.Invoices.GenerateInvoicePdf;
using TaxVision.Billing.Application.Invoices.IntegrationEvents;
using TaxVision.Billing.Domain.Invoices;
using TaxVision.Billing.Domain.Numbering;
using TaxVision.Billing.Tests.Fakes;
using Xunit;

namespace TaxVision.Billing.Tests.Invoices;

/// <summary>BIL-5: la factura de onboarding termina en el tenant real, llegue antes o después del alta.</summary>
public sealed class OnboardingInvoiceOrderingTests
{
    private static readonly DateTime Now = new(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid OnboardingId = Guid.NewGuid();
    private static readonly Guid CreatedTenantId = Guid.NewGuid();

    private readonly InMemoryInvoiceRepository _invoices = new();
    private readonly InMemoryOnboardingTenantMapRepository _maps = new();
    private readonly FakeMessageBus _bus = new();

    [Fact]
    public async Task TenantCreatedBeforeInvoiceRequested_InvoiceEndsInRealTenant()
    {
        await TenantCreatedAsync();
        await InvoiceRequestedAsync();

        Assert.Equal(CreatedTenantId, Assert.Single(_invoices.Rows).TenantId);
    }

    [Fact]
    public async Task NormalOrder_StillRehomes()
    {
        await InvoiceRequestedAsync();
        await TenantCreatedAsync();

        Assert.Equal(CreatedTenantId, Assert.Single(_invoices.Rows).TenantId);
        Assert.Equal(CreatedTenantId, Assert.Single(_maps.Rows).Value);
    }

    // El PDF busca la factura por su tenant: bajo la plataforma ya no la encontraría.
    [Fact]
    public async Task TenantCreatedFirst_PdfIsRequestedUnderTheRealTenant()
    {
        await TenantCreatedAsync();
        await InvoiceRequestedAsync();

        Assert.Equal(CreatedTenantId, _bus.LastOfType<GenerateInvoicePdfCommand>()!.TenantId);
    }

    private Task TenantCreatedAsync() =>
        OnboardingInvoiceBackfillConsumer.Handle(
            new TenantCreatedForOnboardingIntegrationEvent
            {
                TenantId = PlatformTenant.Id,
                OnboardingId = OnboardingId,
                CreatedTenantId = CreatedTenantId,
            },
            _invoices,
            _maps,
            new CountingUnitOfWork(),
            new FixedClock(Now),
            new NoopCorrelationContext(),
            NullLogger<Invoice>.Instance,
            CancellationToken.None
        );

    private Task InvoiceRequestedAsync() =>
        OnboardingInvoiceRequestedConsumer.Handle(
            new OnboardingInvoiceRequestedIntegrationEvent
            {
                TenantId = PlatformTenant.Id,
                OnboardingId = OnboardingId,
                PlanId = Guid.NewGuid(),
                PlanDescription = "Enterprise (Monthly)",
                PayerName = "Owner",
                PaymentId = Guid.NewGuid(),
                GrossAmountCents = 4900,
                DiscountAmountCents = 0,
                NetAmountCents = 4900,
                Currency = "USD",
                SettlementType = "Paid",
            },
            _invoices,
            new SequenceStub(),
            _maps,
            new CountingUnitOfWork(),
            _bus,
            Options.Create(new PlatformIssuerOptions()),
            new PlatformStub(),
            new FixedClock(Now),
            new NoopCorrelationContext(),
            NullLogger<Invoice>.Instance,
            CancellationToken.None
        );

    private sealed class SequenceStub : IInvoiceNumberSequenceRepository
    {
        public Task<InvoiceNumberSequence> GetOrCreateAsync(
            Guid tenantId,
            string periodKey,
            CancellationToken ct = default
        ) => Task.FromResult(InvoiceNumberSequence.Start(tenantId, periodKey));
    }

    private sealed class PlatformStub : IPlatformSettings
    {
        public PlatformSettingsSnapshot Current { get; } =
            new(1, "Brand", "Brand LLC", "brand.test", "api.brand.test", "help@brand.test", "billing@brand.test");
    }
}
