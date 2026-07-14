using TaxVision.Subscription.Domain.Audit;

namespace TaxVision.Subscription.Tests.Domain;

public sealed class SubscriptionAuditLogTests
{
    [Fact]
    public void Create_succeeds_with_valid_fields()
    {
        var result = SubscriptionAuditLog.Create(
            Guid.NewGuid(),
            "SubscriptionSeat",
            Guid.NewGuid(),
            "Seat.Assigned",
            Guid.NewGuid(),
            "User",
            DateTime.UtcNow,
            "corr-1",
            null,
            "{\"before\":true}",
            "{\"after\":true}",
            "manual test"
        );

        Assert.True(result.IsSuccess);
        Assert.Equal("Seat.Assigned", result.Value.Action);
    }

    [Fact]
    public void Create_rejects_an_empty_tenant_id()
    {
        var result = SubscriptionAuditLog.Create(
            Guid.Empty,
            "SubscriptionSeat",
            Guid.NewGuid(),
            "Seat.Assigned",
            Guid.NewGuid(),
            "User",
            DateTime.UtcNow,
            null,
            null,
            null,
            null,
            null
        );

        Assert.True(result.IsFailure);
        Assert.Equal("AuditLog.InvalidTenant", result.Error.Code);
    }

    [Fact]
    public void Create_truncates_a_reason_longer_than_500_characters()
    {
        var longReason = new string('x', 600);

        var result = SubscriptionAuditLog.Create(
            Guid.NewGuid(),
            "SubscriptionSeat",
            Guid.NewGuid(),
            "Seat.Assigned",
            Guid.NewGuid(),
            "User",
            DateTime.UtcNow,
            null,
            null,
            null,
            null,
            longReason
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(500, result.Value.Reason!.Length);
    }
}
