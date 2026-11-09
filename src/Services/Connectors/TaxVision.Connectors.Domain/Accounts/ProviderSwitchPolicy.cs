using TaxVision.Connectors.Domain.Shared;

namespace TaxVision.Connectors.Domain.Accounts;

/// <summary>Lo que cuelga de una cuenta y solo vale para un proveedor.</summary>
public enum ProviderArtifact
{
    SmtpCredentials,
    ImapCredentials,
    OAuthConnection,
    WatchSubscription,
    SyncCursor,
}

/// <param name="ProviderChanges">La cuenta pasa a otro proveedor.</param>
/// <param name="Purge">Lo que hay que borrar antes de seguir.</param>
public sealed record ProviderSwitchDecision(bool ProviderChanges, IReadOnlySet<ProviderArtifact> Purge)
{
    public static ProviderSwitchDecision None { get; } = new(false, new HashSet<ProviderArtifact>());

    public bool ResetsCursor => Purge.Contains(ProviderArtifact.SyncCursor);
}

/// <summary>
/// Qué se purga al cambiar de proveedor sobre la misma fila. Hacia OAuth se van las credenciales manuales: la conexión
/// se reutiliza. Hacia IMAP se va lo de OAuth, y las credenciales las crea quien conecta. El cursor siempre se va: el
/// formato de uno revienta en el otro y la reconciliación lo vuelve a sembrar.
/// </summary>
public static class ProviderSwitchPolicy
{
    private static readonly ProviderArtifact[] StaleForOAuth =
    [
        ProviderArtifact.SmtpCredentials,
        ProviderArtifact.ImapCredentials,
        ProviderArtifact.SyncCursor,
    ];

    private static readonly ProviderArtifact[] StaleForManual =
    [
        ProviderArtifact.OAuthConnection,
        ProviderArtifact.WatchSubscription,
        ProviderArtifact.SyncCursor,
    ];

    public static bool IsSwitch(TenantEmailAccount account, ProviderCode newProvider) =>
        account.ProviderCode != newProvider;

    /// <param name="siblings">Lo que la cuenta tiene hoy.</param>
    public static ProviderSwitchDecision Decide(
        TenantEmailAccount account,
        IReadOnlySet<ProviderArtifact> siblings,
        ProviderCode newProvider
    )
    {
        if (!IsSwitch(account, newProvider))
            return ProviderSwitchDecision.None;

        var stale = newProvider == ProviderCode.Imap ? StaleForManual : StaleForOAuth;
        return new ProviderSwitchDecision(true, stale.Where(siblings.Contains).ToHashSet());
    }
}
