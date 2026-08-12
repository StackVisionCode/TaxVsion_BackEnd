using Microsoft.Extensions.Options;
using TaxVision.Sms.Application;
using TaxVision.Sms.Application.Providers;
using TaxVision.Sms.Tests.Fakes;

namespace TaxVision.Sms.Tests.Application;

public sealed class SmsProviderRouterTests
{
    private static SmsProviderRouter Build(SmsOptions options, params string[] codes)
    {
        var map = codes.ToDictionary(c => c, c => (ISmsProvider)new FakeSmsProvider { Code = c });
        return new SmsProviderRouter(new MapSmsAdapterFactory(map), Options.Create(options));
    }

    [Fact]
    public void Empty_order_falls_back_to_default_provider_only()
    {
        var router = Build(new SmsOptions { DefaultProvider = "infobip", ProviderOrder = [] }, "infobip", "textmaxx");

        var order = router.ResolveOrder();

        Assert.Single(order);
        Assert.Equal("infobip", order[0].Code);
    }

    [Fact]
    public void Blank_order_entries_fall_back_to_default_provider()
    {
        // Los slots de env vacíos (Sms__ProviderOrder__0/1/2) llegan como ["","",""] — deben ignorarse
        // y caer al DefaultProvider, no dejar la ruta vacía.
        var router = Build(
            new SmsOptions { DefaultProvider = "twilio", ProviderOrder = ["", "", ""] },
            "twilio",
            "infobip"
        );

        var order = router.ResolveOrder();

        Assert.Single(order);
        Assert.Equal("twilio", order[0].Code);
    }

    [Fact]
    public void Provider_order_is_honored_in_sequence()
    {
        var router = Build(
            new SmsOptions { DefaultProvider = "fake", ProviderOrder = ["infobip", "textmaxx"] },
            "infobip",
            "textmaxx"
        );

        var order = router.ResolveOrder();

        Assert.Equal(2, order.Count);
        Assert.Equal("infobip", order[0].Code);
        Assert.Equal("textmaxx", order[1].Code);
    }

    [Fact]
    public void Duplicate_codes_are_removed_preserving_first_position()
    {
        var router = Build(
            new SmsOptions { DefaultProvider = "fake", ProviderOrder = ["infobip", "infobip", "textmaxx"] },
            "infobip",
            "textmaxx"
        );

        var order = router.ResolveOrder();

        Assert.Equal(2, order.Count);
        Assert.Equal("infobip", order[0].Code);
        Assert.Equal("textmaxx", order[1].Code);
    }
}
