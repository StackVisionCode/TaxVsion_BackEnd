using TaxVision.Auth.Domain.Users;
using Xunit;

namespace TaxVision.Auth.Tests.Domain;

// Step-up M2M: Customer reenvía la marca de reautenticación del usuario y Auth exige que tenga ≤ 10 min.
public sealed class ReauthenticationWindowTests
{
    private static readonly DateTime Now = new(2026, 11, 26, 18, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_reauthentication_from_one_minute_ago_is_accepted() =>
        Assert.True(ReauthenticationWindow.Validate(Now.AddMinutes(-1), Now).IsSuccess);

    [Fact]
    public void Exactly_ten_minutes_is_still_accepted() =>
        Assert.True(ReauthenticationWindow.Validate(Now.AddMinutes(-10), Now).IsSuccess);

    [Fact]
    public void One_tick_past_ten_minutes_is_stale()
    {
        var result = ReauthenticationWindow.Validate(Now.AddMinutes(-10).AddTicks(-1), Now);

        Assert.True(result.IsFailure);
        Assert.Equal("Auth.CredentialAdmin.StaleReauthentication", result.Error.Code);
    }

    [Fact]
    public void A_date_in_the_future_is_rejected()
    {
        var result = ReauthenticationWindow.Validate(Now.AddSeconds(1), Now);

        Assert.True(result.IsFailure);
        Assert.Equal("Auth.CredentialAdmin.StaleReauthentication", result.Error.Code);
    }

    [Fact]
    public void A_missing_mark_is_stale() =>
        Assert.Equal(
            "Auth.CredentialAdmin.StaleReauthentication",
            ReauthenticationWindow.Validate(default, Now).Error.Code
        );

    [Fact]
    public void The_same_instant_is_accepted() => Assert.True(ReauthenticationWindow.Validate(Now, Now).IsSuccess);
}
