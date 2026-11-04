using TaxVision.CloudStorage.Domain.Providers;

namespace TaxVision.CloudStorage.Tests.Domain;

public sealed class TenantCloudConnectionTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    [Fact]
    public void Connect_keeps_who_authorized_it_and_ignores_an_empty_id()
    {
        var userId = Guid.NewGuid();

        var connection = TenantCloudConnection.Connect(Tenant, "dropbox", "office@firm.com", "x", userId);
        var legacy = TenantCloudConnection.Connect(Tenant, "dropbox", "office@firm.com", "x", Guid.Empty);

        Assert.Equal(userId, connection.ConnectedByUserId);
        Assert.Null(legacy.ConnectedByUserId);
        Assert.False(connection.NeedsReview);
    }

    [Fact]
    public void The_account_of_the_person_wins_over_who_authorized_it_and_ignores_case()
    {
        var userId = Guid.NewGuid();
        var connection = TenantCloudConnection.Connect(Tenant, "google-drive", "Jane@Firm.com", "x", userId);

        Assert.Equal(
            CloudConnectionReviewReasons.AccountOfOffboardedUser,
            connection.LinkedReasonFor(Guid.NewGuid(), " jane@firm.COM ")
        );
        Assert.Equal(
            CloudConnectionReviewReasons.ConnectedByOffboardedUser,
            connection.LinkedReasonFor(userId, "someone@else.com")
        );
        Assert.Null(connection.LinkedReasonFor(Guid.NewGuid(), "someone@else.com"));
        Assert.Null(connection.LinkedReasonFor(Guid.NewGuid(), null));
    }

    [Fact]
    public void Require_review_is_idempotent_and_keeps_the_first_reason()
    {
        var connection = TenantCloudConnection.Connect(Tenant, "onedrive", "office@firm.com", "x");
        var first = new DateTime(2026, 11, 3, 0, 0, 0, DateTimeKind.Utc);

        Assert.True(connection.RequireReview(CloudConnectionReviewReasons.ConnectedByOffboardedUser, first));
        Assert.False(connection.RequireReview(CloudConnectionReviewReasons.AccountOfOffboardedUser, first.AddHours(1)));

        Assert.True(connection.NeedsReview);
        Assert.Equal(first, connection.ReviewRequiredAtUtc);
        Assert.Equal(CloudConnectionReviewReasons.ConnectedByOffboardedUser, connection.ReviewReason);
    }
}
