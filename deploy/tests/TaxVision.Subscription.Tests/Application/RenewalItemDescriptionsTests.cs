using TaxVision.Subscription.Application.Subscriptions.IntegrationEvents;
using Xunit;

namespace TaxVision.Subscription.Tests.Application;

/// <summary>El correo de cobro fallido nombra todo lo que iba en el cobro del aniversario.</summary>
public sealed class RenewalItemDescriptionsTests
{
    [Theory]
    [InlineData(0, 0, "Pro plan")]
    [InlineData(1, 0, "Pro plan and 1 seat")]
    [InlineData(3, 0, "Pro plan and 3 seats")]
    [InlineData(0, 2, "Pro plan and 2 add-ons")]
    [InlineData(3, 1, "Pro plan, 3 seats and 1 add-on")]
    public void The_bundle_names_the_plan_its_seats_and_add_ons(int seats, int addOns, string expected)
    {
        Assert.Equal(expected, RenewalItemDescriptions.Bundle("Pro", seats, addOns));
    }
}
