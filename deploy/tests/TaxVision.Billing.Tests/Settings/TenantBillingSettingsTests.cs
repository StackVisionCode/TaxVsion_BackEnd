using TaxVision.Billing.Domain.Settings;
using TaxVision.Billing.Domain.ValueObjects;
using Xunit;

namespace TaxVision.Billing.Tests.Settings;

public sealed class TenantBillingSettingsTests
{
    private static readonly DateTime Now = new(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_new_row_is_due_on_receipt_without_texts()
    {
        var settings = TenantBillingSettings.Create(Guid.NewGuid(), Now);

        Assert.Equal(InvoiceTermsSnapshot.Default, settings.ToSnapshot());
    }

    [Fact]
    public void Texts_are_trimmed_and_blank_becomes_null()
    {
        var settings = TenantBillingSettings.Create(Guid.NewGuid(), Now);

        var result = settings.Update(PaymentTerms.Net30, "  Zelle to pay@office.com  ", "   ", Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            new InvoiceTermsSnapshot(PaymentTerms.Net30, "Zelle to pay@office.com", null),
            settings.ToSnapshot()
        );
    }

    [Fact]
    public void Exactly_500_characters_is_accepted()
    {
        var settings = TenantBillingSettings.Create(Guid.NewGuid(), Now);
        var text = new string('a', TenantBillingSettings.TextMaxLength);

        Assert.True(settings.Update(PaymentTerms.Net7, text, text, Now).IsSuccess);
    }

    [Fact]
    public void Overlong_instructions_reject_the_whole_save()
    {
        var settings = TenantBillingSettings.Create(Guid.NewGuid(), Now);
        settings.Update(PaymentTerms.Net15, "old", "old footer", Now);

        var result = settings.Update(PaymentTerms.Net45, new string('a', 501), "new footer", Now);

        Assert.Equal("Billing.InvoiceSettings.PaymentInstructions", result.Error.Code);
        Assert.Equal(new InvoiceTermsSnapshot(PaymentTerms.Net15, "old", "old footer"), settings.ToSnapshot());
    }

    [Fact]
    public void An_overlong_footer_is_rejected()
    {
        var settings = TenantBillingSettings.Create(Guid.NewGuid(), Now);

        var result = settings.Update(PaymentTerms.Net15, null, new string('a', 501), Now);

        Assert.Equal("Billing.InvoiceSettings.InvoiceFooter", result.Error.Code);
    }

    [Fact]
    public void A_value_outside_the_enum_is_rejected()
    {
        var settings = TenantBillingSettings.Create(Guid.NewGuid(), Now);

        var result = settings.Update((PaymentTerms)99, null, null, Now);

        Assert.Equal("Billing.InvoiceSettings.PaymentTerms", result.Error.Code);
    }
}
