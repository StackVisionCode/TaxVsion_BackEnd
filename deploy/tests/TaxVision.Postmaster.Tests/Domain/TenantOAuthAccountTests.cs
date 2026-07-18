using TaxVision.Postmaster.Domain.Projections;

namespace TaxVision.Postmaster.Tests.Domain;

public sealed class TenantOAuthAccountTests
{
    [Fact]
    public void ForNewConnection_creates_active_account()
    {
        var tenantId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var connectedAt = DateTime.UtcNow;

        var account = TenantOAuthAccount.ForNewConnection(
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
            TenantOAuthAccount.ForNewConnection(
                Guid.Empty,
                Guid.NewGuid(),
                "gmail",
                "sales@tenant.example",
                DateTime.UtcNow
            )
        );
    }

    [Fact]
    public void MarkDisconnected_deactivates_and_stamps_disconnected_date()
    {
        var account = TenantOAuthAccount.ForNewConnection(
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
        var account = TenantOAuthAccount.ForNewConnection(
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
