using TaxVision.Notification.Domain.Emailing.Accounts;

namespace TaxVision.Notification.Tests.Domain;

public sealed class EmailAccountTests
{
    [Fact]
    public void Imap_account_requires_host()
    {
        var result = EmailAccountConnection.CreateImap(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "user@example.com",
            null,
            imapHost: " ",
            imapPort: 993,
            imapUsername: "user",
            imapPasswordCipher: "cipher",
            imapUseSsl: true
        );

        Assert.True(result.IsFailure);
        Assert.Equal("EmailAccount.Imap", result.Error.Code);
    }

    [Fact]
    public void Oauth_factory_rejects_imap_provider()
    {
        var result = EmailAccountConnection.CreateOAuth(
            Guid.NewGuid(),
            Guid.NewGuid(),
            EmailExternalProvider.Imap,
            "user@example.com",
            null,
            null,
            "at",
            "rt",
            null
        );

        Assert.True(result.IsFailure);
        Assert.Equal("EmailAccount.Provider", result.Error.Code);
    }

    [Fact]
    public void Connected_account_starts_active_and_idle()
    {
        var account = CreateImap();

        Assert.True(account.IsActive);
        Assert.Equal(AccountSyncStatus.Idle, account.SyncStatus);
    }

    [Fact]
    public void Disconnect_clears_secrets_and_deactivates()
    {
        var account = CreateImap();

        account.Disconnect();

        Assert.False(account.IsActive);
        Assert.Equal(AccountSyncStatus.Disconnected, account.SyncStatus);
        Assert.Null(account.ImapPasswordCipher);
    }

    [Fact]
    public void Full_sync_completion_sets_full_sync_timestamp()
    {
        var account = CreateImap();
        account.MarkSyncStarted();

        account.MarkSyncCompleted(wasFullSync: true);

        Assert.Equal(AccountSyncStatus.Idle, account.SyncStatus);
        Assert.NotNull(account.LastSyncAtUtc);
        Assert.NotNull(account.LastFullSyncAtUtc);
    }

    private static EmailAccountConnection CreateImap() =>
        EmailAccountConnection
            .CreateImap(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "user@example.com",
                "User",
                imapHost: "imap.example.com",
                imapPort: 993,
                imapUsername: "user",
                imapPasswordCipher: "cipher",
                imapUseSsl: true
            )
            .Value;
}
