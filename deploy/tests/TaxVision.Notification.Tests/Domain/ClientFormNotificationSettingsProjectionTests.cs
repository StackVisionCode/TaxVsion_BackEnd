using TaxVision.Notification.Domain.Directory;

namespace TaxVision.Notification.Tests.Domain;

public sealed class ClientFormNotificationSettingsProjectionTests
{
    private static readonly DateTime Earlier = new(2026, 10, 25, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Later = Earlier.AddSeconds(5);

    [Fact]
    public void An_older_sync_arriving_late_does_not_overwrite_a_newer_one()
    {
        var row = ClientFormNotificationSettingsProjection.Create(Guid.NewGuid(), pushEnabled: false, Later);

        row.Reconcile(pushEnabled: true, Earlier);

        Assert.False(row.PushNotificationsEnabled);
        Assert.Equal(Later, row.UpdatedAtUtc);
    }

    // El backfill de Tasks viaja con la fecha mínima: crea la fila que falta y nunca pisa una real.
    [Fact]
    public void A_backfill_sync_never_overwrites_a_real_change()
    {
        var row = ClientFormNotificationSettingsProjection.Create(Guid.NewGuid(), pushEnabled: false, Earlier);

        row.Reconcile(pushEnabled: true, DateTime.UnixEpoch);

        Assert.False(row.PushNotificationsEnabled);
    }

    [Fact]
    public void A_backfill_sync_creates_the_missing_row_and_a_later_change_still_wins()
    {
        var row = ClientFormNotificationSettingsProjection.Create(
            Guid.NewGuid(),
            pushEnabled: true,
            DateTime.UnixEpoch
        );

        row.Reconcile(pushEnabled: false, Earlier);

        Assert.False(row.PushNotificationsEnabled);
    }

    [Fact]
    public void A_newer_sync_replaces_the_value()
    {
        var row = ClientFormNotificationSettingsProjection.Create(Guid.NewGuid(), pushEnabled: false, Earlier);

        row.Reconcile(pushEnabled: true, Later);

        Assert.True(row.PushNotificationsEnabled);
        Assert.Equal(Later, row.UpdatedAtUtc);
    }
}
