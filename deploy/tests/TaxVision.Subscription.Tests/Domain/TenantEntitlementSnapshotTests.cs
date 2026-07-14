using TaxVision.Subscription.Domain.Entitlements;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Tests.Domain;

public sealed class TenantEntitlementSnapshotTests
{
    [Fact]
    public void Rebuild_increments_the_revision_number()
    {
        var tenantId = Guid.NewGuid();
        var entries = new List<EntitlementEntry>
        {
            new(
                EntitlementKey.Create("seats.max").Value,
                EntitlementValueType.Int,
                "5",
                EntitlementStatus.Active,
                EntitlementSource.Plan,
                null
            ),
        };

        var result = TenantEntitlementSnapshot.Rebuild(
            tenantId,
            previousRevisionNumber: 3,
            "starter",
            Guid.NewGuid(),
            "Active",
            seatCount: 2,
            availableSeatCount: 1,
            entries,
            DateTime.UtcNow
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(4, result.Value.RevisionNumber);
        Assert.Equal(2, result.Value.SeatCount);
    }

    [Fact]
    public void FindByKey_returns_null_for_a_missing_key()
    {
        var snapshot = TenantEntitlementSnapshot
            .Rebuild(Guid.NewGuid(), 0, "starter", Guid.NewGuid(), "Active", 0, 0, [], DateTime.UtcNow)
            .Value;

        Assert.Null(snapshot.FindByKey("does.not.exist"));
    }

    [Fact]
    public void Rebuild_rejects_negative_seat_counts()
    {
        var result = TenantEntitlementSnapshot.Rebuild(
            Guid.NewGuid(),
            0,
            "starter",
            Guid.NewGuid(),
            "Active",
            seatCount: -1,
            availableSeatCount: 0,
            [],
            DateTime.UtcNow
        );

        Assert.True(result.IsFailure);
        Assert.Equal("EntitlementSnapshot.InvalidSeatCount", result.Error.Code);
    }
}
