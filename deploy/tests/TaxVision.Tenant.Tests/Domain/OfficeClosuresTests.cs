using TaxVision.Tenant.Application.Profiles;
using TaxVision.Tenant.Domain.Profiles;
using TaxVision.Tenant.Domain.ValueObjects;
using TaxVision.Tenant.Tests.TestSupport;

namespace TaxVision.Tenant.Tests.Domain;

public sealed class OfficeClosuresTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 11, 9, 15, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Closures_are_validated_trimmed_and_sorted_by_date()
    {
        var profile = OfficeProfile.CreateEmpty(TenantId);

        var result = profile.Update(
            OfficeProfileDrafts.Empty() with
            {
                Closures =
                [
                    new OfficeClosureDraft("2026-12-24", "2026-12-26", "  Christmas  ", false),
                    new OfficeClosureDraft("2026-07-04", null, "Independence Day", true),
                ],
            },
            ActorId,
            Now
        );

        Assert.True(result.Value);
        Assert.Equal(["Independence Day", "Christmas"], profile.Closures.Select(c => c.Label));
        Assert.Equal(new DateOnly(2026, 12, 26), profile.Closures[1].EndDate);
        Assert.True(profile.Closures[0].RepeatsYearly);
    }

    [Theory]
    [InlineData("2026-12-26", "2026-12-24", "Holiday")]
    [InlineData("not-a-date", null, "Holiday")]
    [InlineData("2026-12-24", null, " ")]
    [InlineData("2026-01-01", "2026-03-01", "Too long")]
    public void An_invalid_closure_rejects_the_save(string date, string? endDate, string label)
    {
        var profile = OfficeProfile.CreateEmpty(TenantId);

        var result = profile.Update(
            OfficeProfileDrafts.Empty() with
            {
                Closures = [new OfficeClosureDraft(date, endDate, label, false)],
            },
            ActorId,
            Now
        );

        Assert.True(result.IsFailure);
        Assert.Equal(OfficeClosure.Invalid.Code, result.Error.Code);
        Assert.Empty(profile.Closures);
    }

    [Fact]
    public void Without_closures_in_the_request_the_saved_ones_stay()
    {
        var profile = OfficeProfile.CreateEmpty(TenantId);
        profile.Update(
            OfficeProfileDrafts.Empty() with
            {
                Closures = [new OfficeClosureDraft("2026-12-25", null, "Christmas", true)],
            },
            ActorId,
            Now
        );

        // Un cliente que aún no conoce los cierres no los borra; una lista vacía sí.
        profile.Update(OfficeProfileDrafts.Empty() with { DisplayName = "Acme" }, ActorId, Now.AddMinutes(1));
        Assert.Single(profile.Closures);

        var cleared = profile.Update(
            OfficeProfileDrafts.Empty() with
            {
                DisplayName = "Acme",
                Closures = [],
            },
            ActorId,
            Now.AddMinutes(2)
        );
        Assert.True(cleared.Value);
        Assert.Empty(profile.Closures);
    }

    [Fact]
    public void The_profile_event_carries_the_closures()
    {
        var profile = OfficeProfile.CreateEmpty(TenantId);
        profile.Update(
            OfficeProfileDrafts.Empty() with
            {
                Closures = [new OfficeClosureDraft("2026-12-24", "2026-12-26", "Christmas", true)],
            },
            ActorId,
            Now
        );

        var evt = OfficeProfileChangedEvents.From(profile, "corr");

        var closure = Assert.Single(evt.Closures);
        Assert.Equal("2026-12-24", closure.Date);
        Assert.Equal("2026-12-26", closure.EndDate);
        Assert.Equal("Christmas", closure.Label);
        Assert.True(closure.RepeatsYearly);
    }
}
