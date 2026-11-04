using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Application.Invoices.CreateInvoiceDraft;
using TaxVision.Billing.Domain.Invoices;
using TaxVision.Billing.Domain.Settings;
using TaxVision.Billing.Domain.ValueObjects;
using TaxVision.Billing.Tests.Fakes;
using Xunit;
using DomainIssuerProfile = TaxVision.Billing.Domain.Invoices.IssuerProfile;

namespace TaxVision.Billing.Tests.Invoices;

/// <summary>El borrador congela el emisor (proyección del perfil) y las condiciones de cobro del tenant.</summary>
public sealed class CreateInvoiceDraftHandlerTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);
    private readonly RecordingInvoices invoices = new();
    private readonly InMemoryIssuerProfileRepository profiles = new();
    private readonly InMemoryTenantBillingSettingsRepository settings = new();

    private async Task<Invoice> CreateAsync()
    {
        var result = await CreateInvoiceDraftHandler.Handle(
            new CreateInvoiceDraftCommand(
                Tenant,
                Guid.NewGuid(),
                new InvoiceCustomerInput(Guid.NewGuid(), "Client", null, null, null, null),
                "USD",
                [new InvoiceLineInput("Tax prep", 1, 10_000, 0)],
                null,
                null
            ),
            invoices,
            profiles,
            settings,
            new CountingUnitOfWork(),
            new FixedClock(Now),
            CancellationToken.None
        );
        Assert.True(result.IsSuccess);
        return Assert.Single(invoices.Added, i => i.Id == result.Value.InvoiceId);
    }

    [Fact]
    public async Task Without_settings_the_draft_is_due_on_receipt()
    {
        var invoice = await CreateAsync();

        Assert.Equal(InvoiceTermsSnapshot.Default, invoice.GetTermsSnapshot());
    }

    [Fact]
    public async Task The_tenant_settings_are_frozen_and_later_changes_do_not_touch_the_draft()
    {
        var row = TenantBillingSettings.Create(Tenant, Now);
        row.Update(PaymentTerms.Net30, "Zelle to pay@office.com", "Thank you!", Now);
        settings.Rows.Add(row);

        var invoice = await CreateAsync();
        row.Update(PaymentTerms.Net7, "Cash only", null, Now);

        Assert.Equal(
            new InvoiceTermsSnapshot(PaymentTerms.Net30, "Zelle to pay@office.com", "Thank you!"),
            invoice.GetTermsSnapshot()
        );
    }

    [Fact]
    public async Task The_projected_issuer_profile_is_frozen_as_the_issuer()
    {
        var profile = DomainIssuerProfile.Create(Tenant, Now);
        profile.ApplyTenantProfile(
            1,
            new TenantOfficeProfile("Gutierrez Tax LLC", null, "12-3456789", null, null, null, null),
            Now
        );
        profiles.Rows.Add(profile);

        var invoice = await CreateAsync();

        Assert.Equal("Gutierrez Tax LLC", invoice.Issuer!.Name);
        Assert.Equal("12-3456789", invoice.Issuer.TaxId);
    }

    private sealed class RecordingInvoices : IInvoiceRepository
    {
        public List<Invoice> Added { get; } = [];

        public Task<Invoice?> GetByIdAsync(
            Guid tenantId,
            Guid invoiceId,
            CancellationToken ct = default,
            Guid? assignedToUserId = null
        ) => Task.FromResult(Added.FirstOrDefault(i => i.Id == invoiceId));

        public Task<Invoice?> GetByOnboardingIdAsync(Guid onboardingId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Invoice>> ListByTenantAsync(
            Guid tenantId,
            int take,
            CancellationToken ct = default,
            Guid? assignedToUserId = null,
            Guid? customerId = null
        ) => throw new NotSupportedException();

        public Task AddAsync(Invoice invoice, CancellationToken ct = default)
        {
            Added.Add(invoice);
            return Task.CompletedTask;
        }
    }
}
