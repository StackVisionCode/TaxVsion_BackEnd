using TaxVision.Billing.Application.Settings;
using TaxVision.Billing.Domain.Settings;
using TaxVision.Billing.Domain.ValueObjects;
using TaxVision.Billing.Tests.Fakes;
using Xunit;

namespace TaxVision.Billing.Tests.Settings;

public sealed class GetInvoiceSettingsHandlerTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private readonly InMemoryTenantBillingSettingsRepository repository = new();

    [Fact]
    public async Task Without_a_row_the_defaults_are_returned()
    {
        var result = await GetInvoiceSettingsHandler.Handle(
            new GetInvoiceSettingsQuery(Tenant),
            repository,
            CancellationToken.None
        );

        Assert.Equal(new InvoiceSettingsResponse("DueOnReceipt", null, null), result.Value);
    }

    [Fact]
    public async Task A_saved_row_is_returned_with_the_enum_name()
    {
        var settings = TenantBillingSettings.Create(Tenant, DateTime.UtcNow);
        settings.Update(PaymentTerms.Net15, "Check to 1 Main St", "Thanks", DateTime.UtcNow);
        repository.Rows.Add(settings);

        var result = await GetInvoiceSettingsHandler.Handle(
            new GetInvoiceSettingsQuery(Tenant),
            repository,
            CancellationToken.None
        );

        Assert.Equal(new InvoiceSettingsResponse("Net15", "Check to 1 Main St", "Thanks"), result.Value);
    }
}
