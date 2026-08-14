using TaxVision.Tasks.Domain.Projections;

namespace TaxVision.Tasks.Tests.Projections;

public sealed class CustomerDirectoryEntryTests
{
    private static readonly DateTime T0 = new(2026, 8, 12, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_requires_tenant_and_customer()
    {
        Assert.Throws<ArgumentException>(() =>
            CustomerDirectoryEntry.Create(Guid.Empty, Guid.NewGuid(), "Acme", CustomerDirectoryStatus.Active, T0)
        );
        Assert.Throws<ArgumentException>(() =>
            CustomerDirectoryEntry.Create(Guid.NewGuid(), Guid.Empty, "Acme", CustomerDirectoryStatus.Active, T0)
        );
    }

    [Fact]
    public void ApplyIfNewer_updates_when_observation_is_newer()
    {
        var entry = NewEntry("Acme", CustomerDirectoryStatus.Active);

        entry.ApplyIfNewer("Acme Corp", CustomerDirectoryStatus.Inactive, T0.AddMinutes(5));

        Assert.Equal("Acme Corp", entry.DisplayName);
        Assert.Equal(CustomerDirectoryStatus.Inactive, entry.Status);
        Assert.Equal(T0.AddMinutes(5), entry.UpdatedAtUtc);
    }

    /// <summary>
    /// Redelivery fuera de orden: los eventos de Customer no traen revisión, el orden lo da el
    /// tiempo. Sin este guard un customer reactivado vuelve a Inactive al reentregarse el evento
    /// viejo.
    /// </summary>
    [Fact]
    public void ApplyIfNewer_ignores_older_observation()
    {
        var entry = NewEntry("Acme", CustomerDirectoryStatus.Active);

        entry.ApplyIfNewer("Stale", CustomerDirectoryStatus.Archived, T0.AddMinutes(-1));

        Assert.Equal("Acme", entry.DisplayName);
        Assert.Equal(CustomerDirectoryStatus.Active, entry.Status);
        Assert.Equal(T0, entry.UpdatedAtUtc);
    }

    /// <summary>
    /// Los eventos de cambio de status y el import masivo llegan sin nombre. Si pisaran con null,
    /// cada desactivación borraría el nombre que reconcilió el job.
    /// </summary>
    [Fact]
    public void ApplyIfNewer_keeps_known_name_when_event_carries_none()
    {
        var entry = NewEntry("Acme", CustomerDirectoryStatus.Active);

        entry.ApplyIfNewer(null, CustomerDirectoryStatus.Inactive, T0.AddMinutes(1));

        Assert.Equal("Acme", entry.DisplayName);
        Assert.Equal(CustomerDirectoryStatus.Inactive, entry.Status);
    }

    [Fact]
    public void ApplyDisplayNameIfMissing_fills_only_when_absent()
    {
        var nameless = NewEntry(null, CustomerDirectoryStatus.Active);
        nameless.ApplyDisplayNameIfMissing("Resolved");
        Assert.Equal("Resolved", nameless.DisplayName);

        var named = NewEntry("Acme", CustomerDirectoryStatus.Active);
        named.ApplyDisplayNameIfMissing("Other");
        Assert.Equal("Acme", named.DisplayName);
    }

    private static CustomerDirectoryEntry NewEntry(string? displayName, CustomerDirectoryStatus status) =>
        CustomerDirectoryEntry.Create(Guid.NewGuid(), Guid.NewGuid(), displayName, status, T0);
}
