using TaxVision.Connectors.Domain.Accounts;
using TaxVision.Connectors.Domain.Shared;

namespace TaxVision.Connectors.Tests.Domain;

public sealed class ProviderSwitchPolicyTests
{
    private static readonly DateTime Now = new(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);

    private static readonly HashSet<ProviderArtifact> AllSiblings =
    [
        ProviderArtifact.SmtpCredentials,
        ProviderArtifact.ImapCredentials,
        ProviderArtifact.OAuthConnection,
        ProviderArtifact.WatchSubscription,
        ProviderArtifact.SyncCursor,
    ];

    private static TenantEmailAccount Account(ProviderCode provider) =>
        TenantEmailAccount.Create(Guid.NewGuid(), "office@example.com", provider, Guid.NewGuid(), Now).Value;

    [Fact]
    public void SwitchGmailToGraph_PurgesSiblingImapAndResetsCursor()
    {
        var decision = ProviderSwitchPolicy.Decide(
            Account(ProviderCode.Gmail),
            new HashSet<ProviderArtifact>
            {
                ProviderArtifact.SmtpCredentials,
                ProviderArtifact.ImapCredentials,
                ProviderArtifact.SyncCursor,
            },
            ProviderCode.Graph
        );

        Assert.True(decision.ProviderChanges);
        Assert.True(decision.ResetsCursor);
        Assert.Equal(
            [ProviderArtifact.SmtpCredentials, ProviderArtifact.ImapCredentials, ProviderArtifact.SyncCursor],
            decision.Purge.Order()
        );
    }

    [Fact]
    public void SwitchToOAuth_KeepsTheConnectionAndTheWatch()
    {
        var decision = ProviderSwitchPolicy.Decide(Account(ProviderCode.Imap), AllSiblings, ProviderCode.Gmail);

        Assert.DoesNotContain(ProviderArtifact.OAuthConnection, decision.Purge);
        Assert.DoesNotContain(ProviderArtifact.WatchSubscription, decision.Purge);
    }

    [Fact]
    public void SwitchToManual_PurgesOAuthAndKeepsCredentials()
    {
        var decision = ProviderSwitchPolicy.Decide(Account(ProviderCode.Gmail), AllSiblings, ProviderCode.Imap);

        Assert.Equal(
            [ProviderArtifact.OAuthConnection, ProviderArtifact.WatchSubscription, ProviderArtifact.SyncCursor],
            decision.Purge.Order()
        );
    }

    [Fact]
    public void Only_what_exists_is_purged()
    {
        var decision = ProviderSwitchPolicy.Decide(
            Account(ProviderCode.Imap),
            new HashSet<ProviderArtifact> { ProviderArtifact.ImapCredentials },
            ProviderCode.Gmail
        );

        Assert.Equal([ProviderArtifact.ImapCredentials], decision.Purge);
        Assert.False(decision.ResetsCursor);
    }

    [Fact]
    public void Same_provider_changes_nothing()
    {
        var decision = ProviderSwitchPolicy.Decide(Account(ProviderCode.Gmail), AllSiblings, ProviderCode.Gmail);

        Assert.False(decision.ProviderChanges);
        Assert.Empty(decision.Purge);
    }
}
