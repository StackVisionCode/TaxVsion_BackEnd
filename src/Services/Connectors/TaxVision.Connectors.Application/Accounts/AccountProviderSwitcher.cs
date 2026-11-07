using BuildingBlocks.Results;
using TaxVision.Connectors.Application.Sync;
using TaxVision.Connectors.Application.Watch;
using TaxVision.Connectors.Domain.Accounts;
using TaxVision.Connectors.Domain.Shared;

namespace TaxVision.Connectors.Application.Accounts;

/// <summary>Ejecuta lo que decide <see cref="ProviderSwitchPolicy"/>. El guardado es de quien llama.</summary>
public sealed class AccountProviderSwitcher(
    ISmtpCredentialsRepository smtpCredentials,
    IImapCredentialsRepository imapCredentials,
    IOAuthConnectionRepository oauthConnections,
    IProviderWatchSubscriptionRepository watchSubscriptions,
    IProviderSyncCursorRepository syncCursors
)
{
    public async Task<Result> SwitchAsync(
        TenantEmailAccount account,
        ProviderCode newProvider,
        DateTime now,
        CancellationToken ct
    )
    {
        if (!ProviderSwitchPolicy.IsSwitch(account, newProvider))
            return Result.Success();

        var smtp = await smtpCredentials.GetByAccountIdAsync(account.Id, ct);
        var imap = await imapCredentials.GetByAccountIdAsync(account.Id, ct);
        var oauth = await oauthConnections.GetByAccountIdAsync(account.Id, ct);
        var watch = await watchSubscriptions.GetByAccountIdAsync(account.Id, ct);
        var cursor = await syncCursors.GetByAccountIdAsync(account.Id, ct);

        var siblings = new HashSet<ProviderArtifact>();
        if (smtp.IsSuccess)
            siblings.Add(ProviderArtifact.SmtpCredentials);
        if (imap.IsSuccess)
            siblings.Add(ProviderArtifact.ImapCredentials);
        if (oauth.IsSuccess)
            siblings.Add(ProviderArtifact.OAuthConnection);
        if (watch.IsSuccess)
            siblings.Add(ProviderArtifact.WatchSubscription);
        if (cursor.IsSuccess)
            siblings.Add(ProviderArtifact.SyncCursor);

        var decision = ProviderSwitchPolicy.Decide(account, siblings, newProvider);
        var changed = account.ChangeProvider(newProvider, now);
        if (changed.IsFailure)
            return changed;

        if (decision.Purge.Contains(ProviderArtifact.SmtpCredentials))
            smtpCredentials.Remove(smtp.Value);
        if (decision.Purge.Contains(ProviderArtifact.ImapCredentials))
            imapCredentials.Remove(imap.Value);
        if (decision.Purge.Contains(ProviderArtifact.OAuthConnection))
            oauthConnections.Remove(oauth.Value);
        if (decision.Purge.Contains(ProviderArtifact.WatchSubscription))
            watchSubscriptions.Remove(watch.Value);
        if (decision.ResetsCursor)
            syncCursors.Remove(cursor.Value);
        return Result.Success();
    }
}
