using TaxVision.Subscription.Domain.Entitlements;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Tests.Domain;

public sealed class TenantEntitlementOverrideTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 11, 22, 12, 0, 0, DateTimeKind.Utc);

    private static EntitlementKey Key(string value) => EntitlementKey.Create(value).Value;

    [Fact]
    public void Grant_keeps_the_value_reason_and_expiry()
    {
        var granted = TenantEntitlementOverride.Grant(
            TenantId,
            Key("storage.max_bytes"),
            EntitlementValueType.Long,
            "53687091200",
            "  Acuerdo comercial  ",
            Now.AddDays(30),
            Actor,
            Now
        );

        Assert.True(granted.IsSuccess);
        var item = granted.Value;
        Assert.Equal(TenantId, item.TenantId);
        Assert.Equal("storage.max_bytes", item.Key);
        Assert.Equal("53687091200", item.Value);
        Assert.Equal("Acuerdo comercial", item.Reason);
        Assert.Equal(Now.AddDays(30), item.ExpiresAtUtc);
        Assert.Equal(Actor, item.CreatedBy);
    }

    [Theory]
    [InlineData(EntitlementValueType.Bool, "TRUE", "true")]
    [InlineData(EntitlementValueType.Bool, "false", "false")]
    [InlineData(EntitlementValueType.Int, "120", "120")]
    [InlineData(EntitlementValueType.Decimal, "1.50", "1.50")]
    public void Grant_writes_the_value_the_way_the_snapshot_does(EntitlementValueType type, string input, string stored)
    {
        var granted = TenantEntitlementOverride.Grant(TenantId, Key("x.limit"), type, input, "r", null, Actor, Now);

        Assert.Equal(stored, granted.Value.Value);
    }

    [Theory]
    [InlineData(EntitlementValueType.Int, "abc")]
    [InlineData(EntitlementValueType.Int, "-1")]
    [InlineData(EntitlementValueType.Long, "1.5")]
    [InlineData(EntitlementValueType.Bool, "maybe")]
    [InlineData(EntitlementValueType.String, "   ")]
    public void Grant_rejects_a_value_that_does_not_fit_the_type(EntitlementValueType type, string value)
    {
        var granted = TenantEntitlementOverride.Grant(TenantId, Key("x.limit"), type, value, "r", null, Actor, Now);

        Assert.Equal("EntitlementOverride.InvalidValue", granted.Error.Code);
    }

    [Fact]
    public void Grant_requires_a_reason()
    {
        var granted = TenantEntitlementOverride.Grant(
            TenantId,
            Key("seats.max"),
            EntitlementValueType.Int,
            "10",
            " ",
            null,
            Actor,
            Now
        );

        Assert.Equal("EntitlementOverride.ReasonRequired", granted.Error.Code);
    }

    [Fact]
    public void Grant_rejects_an_expiry_that_already_passed()
    {
        var granted = TenantEntitlementOverride.Grant(
            TenantId,
            Key("seats.max"),
            EntitlementValueType.Int,
            "10",
            "r",
            Now,
            Actor,
            Now
        );

        Assert.Equal("EntitlementOverride.ExpiryInPast", granted.Error.Code);
    }

    [Fact]
    public void Is_in_force_until_it_expires()
    {
        var item = Grant(expiresAtUtc: Now.AddHours(1));

        Assert.True(item.IsInForceAt(Now));
        Assert.False(item.IsInForceAt(Now.AddHours(1)));
        Assert.True(Grant(expiresAtUtc: null).IsInForceAt(Now.AddYears(10)));
    }

    [Fact]
    public void Entry_is_marked_as_an_override()
    {
        var entry = Grant(expiresAtUtc: Now.AddDays(1)).ToEntry();

        Assert.Equal("seats.max", entry.Key.Value);
        Assert.Equal(EntitlementValueType.Int, entry.ValueType);
        Assert.Equal("10", entry.Value);
        Assert.Equal(EntitlementSource.Override, entry.Source);
        Assert.Equal(EntitlementStatus.Active, entry.Status);
        Assert.Equal(Now.AddDays(1), entry.ExpiresAtUtc);
    }

    [Fact]
    public void Change_replaces_the_terms_and_opens_the_expiry_again()
    {
        var item = Grant(expiresAtUtc: Now.AddHours(1));
        Assert.True(item.MarkExpiryApplied(Now.AddHours(2)).IsSuccess);
        var other = Guid.NewGuid();

        var changed = item.Change("12", "Ampliación", Now.AddDays(10), other, Now.AddHours(3));

        Assert.True(changed.IsSuccess);
        Assert.Equal("12", item.Value);
        Assert.Equal("Ampliación", item.Reason);
        Assert.Equal(Now.AddDays(10), item.ExpiresAtUtc);
        Assert.Null(item.ExpiryAppliedAtUtc);
        Assert.Equal(other, item.UpdatedBy);
    }

    [Fact]
    public void Change_validates_like_grant()
    {
        var item = Grant(expiresAtUtc: null);

        Assert.Equal("EntitlementOverride.InvalidValue", item.Change("x", "r", null, Actor, Now).Error.Code);
        Assert.Equal("10", item.Value);
    }

    [Fact]
    public void Expiry_is_applied_once_and_only_after_it_passed()
    {
        var item = Grant(expiresAtUtc: Now.AddHours(1));

        Assert.Equal("EntitlementOverride.NotExpired", item.MarkExpiryApplied(Now).Error.Code);
        Assert.True(item.MarkExpiryApplied(Now.AddHours(1)).IsSuccess);
        Assert.Equal(Now.AddHours(1), item.ExpiryAppliedAtUtc);
        Assert.True(item.MarkExpiryApplied(Now.AddHours(5)).IsSuccess);
        Assert.Equal(Now.AddHours(1), item.ExpiryAppliedAtUtc);
    }

    private static TenantEntitlementOverride Grant(DateTime? expiresAtUtc) =>
        TenantEntitlementOverride
            .Grant(TenantId, Key("seats.max"), EntitlementValueType.Int, "10", "r", expiresAtUtc, Actor, Now)
            .Value;
}
