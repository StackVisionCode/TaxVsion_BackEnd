using TaxVision.Mobile.Domain.Trips;

namespace TaxVision.Mobile.Tests.Domain;

/// <summary>Plazo que se aplica al recorrido GPS: el de la oficina, o tres años si no configuró ninguno.</summary>
public sealed class MileageRetentionPolicyTests
{
    [Fact]
    public void Default_period_is_three_years() => Assert.Equal(1095, MileageRetentionPolicy.DefaultRetentionDays);

    [Fact]
    public void An_office_without_a_period_gets_the_default()
    {
        Assert.Equal(MileageRetentionPolicy.DefaultRetentionDays, MileageRetentionPolicy.EffectiveDays(null));
    }

    [Theory]
    [InlineData(30)]
    [InlineData(3650)]
    public void A_configured_period_wins_over_the_default(int days)
    {
        Assert.Equal(days, MileageRetentionPolicy.EffectiveDays(days));
    }

    private static readonly DateTime Now = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    [InlineData(3650)]
    public void A_valid_period_creates_the_policy_of_the_office(int? days)
    {
        var tenant = Guid.NewGuid();

        var created = MileageRetentionPolicy.Create(tenant, days, Now);

        Assert.True(created.IsSuccess);
        Assert.Equal(tenant, created.Value.TenantId);
        Assert.Equal(days, created.Value.RetentionDays);
        Assert.Equal(Now, created.Value.UpdatedAtUtc);
    }

    // Regla de negocio: se devuelve el error, no se lanza.
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(3651)]
    public void An_out_of_range_period_is_an_error_not_an_exception(int days)
    {
        var created = MileageRetentionPolicy.Create(Guid.NewGuid(), days, Now);

        Assert.True(created.IsFailure);
        Assert.Equal(TripErrors.InvalidRetention, created.Error);
        Assert.Equal("Mobile.Retention.InvalidDays", created.Error.Code);
    }

    [Fact]
    public void A_policy_needs_an_office() => Assert.True(MileageRetentionPolicy.Create(Guid.Empty, 30, Now).IsFailure);

    [Fact]
    public void An_invalid_update_is_rejected_and_keeps_the_previous_period()
    {
        var policy = MileageRetentionPolicy.Create(Guid.NewGuid(), 30, Now).Value;

        var updated = policy.Update(0, Now.AddDays(1));

        Assert.Equal(TripErrors.InvalidRetention, updated.Error);
        Assert.Equal(30, policy.RetentionDays);
        Assert.Equal(Now, policy.UpdatedAtUtc);
    }

    [Fact]
    public void A_valid_update_changes_the_period_and_can_clear_it()
    {
        var policy = MileageRetentionPolicy.Create(Guid.NewGuid(), 30, Now).Value;

        Assert.True(policy.Update(90, Now.AddDays(1)).IsSuccess);
        Assert.Equal(90, policy.RetentionDays);
        Assert.True(policy.Update(null, Now.AddDays(2)).IsSuccess);
        Assert.Null(policy.RetentionDays);
        Assert.Equal(Now.AddDays(2), policy.UpdatedAtUtc);
    }
}
