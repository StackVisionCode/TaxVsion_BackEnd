using TaxVision.Auth.Onboarding.Domain.TenantOnboardings;
using Xunit;

namespace TaxVision.Auth.Tests.Onboarding;

/// <summary>P7: el onboarding guarda la zona del navegador desde el alta, para el recibo que sale antes del registro.</summary>
public sealed class TenantOnboardingTimeZoneTests
{
    private static readonly DateTime Now = new(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void The_browser_zone_is_kept_from_creation()
    {
        var onboarding = TenantOnboarding
            .Create(
                "buyer@example.com",
                Now,
                Guid.NewGuid(),
                "Ada",
                "Lovelace",
                null,
                Now,
                browserTimeZoneId: "America/Chicago"
            )
            .Value;

        Assert.Equal("America/Chicago", onboarding.TimeZoneId);
    }

    [Fact]
    public void An_invalid_browser_zone_is_ignored()
    {
        var onboarding = TenantOnboarding
            .Create(
                "buyer@example.com",
                Now,
                Guid.NewGuid(),
                "Ada",
                "Lovelace",
                null,
                Now,
                browserTimeZoneId: "Mars/Olympus"
            )
            .Value;

        Assert.Null(onboarding.TimeZoneId);
    }
}
