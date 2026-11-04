using TaxVision.Billing.Application.Settings;
using TaxVision.Billing.Tests.Fakes;
using Xunit;

namespace TaxVision.Billing.Tests.Settings;

/// <summary>PUT de /billing/invoice-settings: estado completo, valida antes de crear la fila.</summary>
public sealed class UpdateInvoiceSettingsHandlerTests
{
    private static readonly DateTime Now = new(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Tenant = Guid.NewGuid();
    private readonly InMemoryTenantBillingSettingsRepository repository = new();
    private readonly CountingUnitOfWork unitOfWork = new();

    private Task<BuildingBlocks.Results.Result<InvoiceSettingsResponse>> UpdateAsync(
        string? terms,
        string? instructions = null,
        string? footer = null
    ) =>
        UpdateInvoiceSettingsHandler.Handle(
            new UpdateInvoiceSettingsCommand(Tenant, terms, instructions, footer),
            repository,
            unitOfWork,
            new FixedClock(Now),
            CancellationToken.None
        );

    [Fact]
    public async Task The_first_save_creates_the_row_and_a_second_one_updates_it()
    {
        await UpdateAsync("net30", "Zelle to pay@office.com", "Thank you!");
        var second = await UpdateAsync("Net45", null, "Thanks");

        Assert.True(second.IsSuccess);
        Assert.Equal(new InvoiceSettingsResponse("Net45", null, "Thanks"), second.Value);
        Assert.Single(repository.Rows);
        Assert.Equal(2, unitOfWork.Saves);

        var read = await GetInvoiceSettingsHandler.Handle(
            new GetInvoiceSettingsQuery(Tenant),
            repository,
            CancellationToken.None
        );
        Assert.Equal(second.Value, read.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Net60")]
    [InlineData("2")]
    public async Task Unknown_payment_terms_are_a_400_without_creating_a_row(string? terms)
    {
        var result = await UpdateAsync(terms);

        Assert.Equal("Billing.InvoiceSettings.PaymentTerms", result.Error.Code);
        Assert.Empty(repository.Rows);
        Assert.Equal(0, unitOfWork.Saves);
    }

    [Fact]
    public async Task Overlong_text_does_not_leave_a_half_created_row()
    {
        var result = await UpdateAsync("Net7", null, new string('a', 501));

        Assert.Equal("Billing.InvoiceSettings.InvoiceFooter", result.Error.Code);
        Assert.Empty(repository.Rows);
    }
}
