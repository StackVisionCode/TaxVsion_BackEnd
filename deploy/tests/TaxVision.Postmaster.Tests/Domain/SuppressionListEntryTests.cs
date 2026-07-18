using TaxVision.Postmaster.Domain.Suppression;

namespace TaxVision.Postmaster.Tests.Domain;

public sealed class SuppressionListEntryTests
{
    [Fact]
    public void Create_normalizes_address_to_lowercase_trimmed()
    {
        var result = SuppressionListEntry.Create(
            Guid.NewGuid(),
            "  Bounced@Example.com  ",
            SuppressionReason.HardBounce,
            addedByUserId: null,
            notes: null,
            DateTime.UtcNow
        );

        Assert.True(result.IsSuccess);
        Assert.Equal("bounced@example.com", result.Value.EmailAddress);
    }

    [Fact]
    public void Create_fails_for_invalid_address()
    {
        var result = SuppressionListEntry.Create(
            Guid.NewGuid(),
            "not-an-email",
            SuppressionReason.Manual,
            addedByUserId: null,
            notes: null,
            DateTime.UtcNow
        );

        Assert.True(result.IsFailure);
        Assert.Equal("SuppressionListEntry.EmailAddress", result.Error.Code);
    }

    [Fact]
    public void Create_fails_when_notes_exceed_1000_chars()
    {
        var result = SuppressionListEntry.Create(
            Guid.NewGuid(),
            "user@example.com",
            SuppressionReason.Manual,
            addedByUserId: null,
            notes: new string('x', 1001),
            DateTime.UtcNow
        );

        Assert.True(result.IsFailure);
        Assert.Equal("SuppressionListEntry.Notes", result.Error.Code);
    }

    [Fact]
    public void Reactivate_refreshes_reason_and_date()
    {
        var entry = SuppressionListEntry
            .Create(
                Guid.NewGuid(),
                "user@example.com",
                SuppressionReason.Manual,
                null,
                "manual note",
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            )
            .Value;

        var newDate = new DateTime(2026, 7, 17, 0, 0, 0, DateTimeKind.Utc);
        var actingUserId = Guid.NewGuid();
        entry.Reactivate(SuppressionReason.HardBounce, actingUserId, "second bounce", newDate);

        Assert.Equal(SuppressionReason.HardBounce, entry.Reason);
        Assert.Equal(actingUserId, entry.AddedByUserId);
        Assert.Equal("second bounce", entry.Notes);
        Assert.Equal(newDate, entry.AddedAtUtc);
    }
}
