using TaxVision.Postmaster.Domain.Projections;

namespace TaxVision.Postmaster.Tests.Domain;

public sealed class ConnectedMailboxTests
{
    [Fact]
    public void ForNewConnection_creates_active_account()
    {
        var tenantId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var connectedAt = DateTime.UtcNow;

        var account = ConnectedMailbox.ForNewConnection(
            tenantId,
            accountId,
            "gmail",
            "sales@tenant.example",
            connectedAt
        );

        Assert.Equal(tenantId, account.TenantId);
        Assert.Equal(accountId, account.AccountId);
        Assert.True(account.IsActive);
        Assert.Equal(connectedAt, account.ConnectedAtUtc);
        Assert.Null(account.DisconnectedAtUtc);
    }

    [Fact]
    public void ForNewConnection_throws_for_empty_tenant_id()
    {
        Assert.Throws<ArgumentException>(() =>
            ConnectedMailbox.ForNewConnection(
                Guid.Empty,
                Guid.NewGuid(),
                "gmail",
                "sales@tenant.example",
                DateTime.UtcNow
            )
        );
    }

    [Fact]
    public void Older_disconnect_after_newer_reconnect_is_ignored()
    {
        var connectedAt = new DateTime(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);
        var account = ConnectedMailbox.ForNewConnection(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "gmail",
            "sales@tenant.example",
            connectedAt
        );
        account.ReconnectAt("gmail", "sales@tenant.example", connectedAt.AddMinutes(2));

        var applied = account.MarkDisconnected(connectedAt.AddMinutes(1));

        Assert.False(applied);
        Assert.True(account.IsActive);
        Assert.Null(account.DisconnectedAtUtc);
    }

    [Fact]
    public void Older_reconnect_after_newer_disconnect_is_ignored()
    {
        var connectedAt = new DateTime(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);
        var account = ConnectedMailbox.ForNewConnection(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "gmail",
            "sales@tenant.example",
            connectedAt
        );
        account.MarkDisconnected(connectedAt.AddMinutes(2));

        var applied = account.ReconnectAt("graph", "other@tenant.example", connectedAt.AddMinutes(1));

        Assert.False(applied);
        Assert.False(account.IsActive);
        Assert.Equal("gmail", account.ProviderCode);
        Assert.Equal(connectedAt.AddMinutes(2), account.DisconnectedAtUtc);
    }

    [Fact]
    public void MarkDisconnected_deactivates_and_stamps_disconnected_date()
    {
        var account = ConnectedMailbox.ForNewConnection(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "gmail",
            "sales@tenant.example",
            DateTime.UtcNow
        );

        var disconnectedAt = DateTime.UtcNow.AddMinutes(5);
        account.MarkDisconnected(disconnectedAt);

        Assert.False(account.IsActive);
        Assert.Equal(disconnectedAt, account.DisconnectedAtUtc);
    }

    [Fact]
    public void ReconnectAt_reactivates_and_clears_disconnected_date()
    {
        var account = ConnectedMailbox.ForNewConnection(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "gmail",
            "sales@tenant.example",
            DateTime.UtcNow
        );
        account.MarkDisconnected(DateTime.UtcNow.AddMinutes(5));

        var reconnectedAt = DateTime.UtcNow.AddMinutes(10);
        account.ReconnectAt("gmail", "sales@tenant.example", reconnectedAt);

        Assert.True(account.IsActive);
        Assert.Null(account.DisconnectedAtUtc);
        Assert.Equal(reconnectedAt, account.ConnectedAtUtc);
    }
}
