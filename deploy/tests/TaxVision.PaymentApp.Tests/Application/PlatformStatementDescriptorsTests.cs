using BuildingBlocks.Platform;
using TaxVision.PaymentApp.Application.Common.Branding;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Tests.Application;

public sealed class PlatformStatementDescriptorsTests
{
    private static PlatformSettingsSnapshot Brand(string name) =>
        new(1, name, "Legal", "taxoffix.io", "api.taxoffix.io", "s@taxoffix.io", "b@taxoffix.io");

    [Fact]
    public void The_descriptor_is_the_upper_case_brand_plus_the_concept()
    {
        Assert.Equal("TAXOFFIX SAAS", PlatformStatementDescriptors.For(Brand("TaxOffix"), "SAAS"));
    }

    [Fact]
    public void Characters_card_networks_reject_are_dropped()
    {
        Assert.Equal("TAXOFFIXPRO WALLET", PlatformStatementDescriptors.For(Brand("Tax-Offix Pro!"), "WALLET"));
    }

    [Fact]
    public void A_long_brand_is_trimmed_so_the_descriptor_stays_within_22_characters()
    {
        var descriptor = PlatformStatementDescriptors.For(Brand("AVeryLongPlatformBrandName"), "RENEWAL");

        Assert.Equal(22, descriptor.Length);
        Assert.EndsWith(" RENEWAL", descriptor);
        Assert.True(StatementDescriptor.Create(descriptor).IsSuccess);
    }
}
