using TaxVision.Connectors.Application.Watch;
using TaxVision.Connectors.Domain.Accounts;
using TaxVision.Connectors.Domain.Shared;
using TaxVision.Connectors.Domain.Sync;

namespace TaxVision.Connectors.Application.Sync;

/// <summary>
/// Seed de <see cref="ProviderSyncCursor"/> compartido entre los dos webhook handlers
/// (ProcessGmailPushNotificationHandler/ProcessGraphNotificationHandler) y ReconcileAccountHandler
/// (reconciliación) — los tres necesitan la MISMA regla de "qué cursor usar si todavía no existe
/// uno persistido", que difiere por proveedor:
///   - Gmail: la History API exige startHistoryId (falla 400 sin él) — se siembra desde el
///     historyId inicial de ProviderWatchSubscription (el que Gmail devolvió en users.watch), con
///     fallback a <paramref name="fallbackCursorValue"/> únicamente si por algún motivo no hay
///     watch persistida (no debería ocurrir en el flujo normal — el watch se configura antes de
///     que la cuenta pueda pasar a Active).
///   - Graph e IMAP: null es un cursor de arranque válido (Graph arranca desde la URL base del
///     delta query; IMAP hace SearchQuery.All) — nunca hace falta sembrar nada, ni consultar
///     ProviderWatchSubscription (IMAP ni siquiera tiene una — ver SetupWatchHandler).
/// </summary>
internal static class ProviderSyncCursorSeeder
{
    public static async Task<(ProviderSyncCursor Cursor, bool WasSeeded)> GetOrSeedAsync(
        TenantEmailAccount account,
        string? fallbackCursorValue,
        IProviderWatchSubscriptionRepository subscriptionRepository,
        IProviderSyncCursorRepository cursorRepository,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        var existing = await cursorRepository.GetByAccountIdAsync(account.Id, ct);
        if (existing.IsSuccess)
            return (existing.Value, false);

        string? seed = null;
        if (account.ProviderCode == ProviderCode.Gmail)
        {
            var subscription = await subscriptionRepository.GetByAccountIdAsync(account.Id, ct);
            seed = subscription.IsSuccess ? subscription.Value.SubscriptionRef : fallbackCursorValue;
        }

        var cursor = ProviderSyncCursor.Create(account.Id, seed, nowUtc).Value;
        await cursorRepository.AddAsync(cursor, ct);
        return (cursor, true);
    }
}
