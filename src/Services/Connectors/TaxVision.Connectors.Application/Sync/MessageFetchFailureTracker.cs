using Microsoft.Extensions.Logging;
using TaxVision.Connectors.Application.Abstractions;
using TaxVision.Connectors.Application.Providers;
using TaxVision.Connectors.Domain.Accounts;
using TaxVision.Connectors.Domain.Sync;

namespace TaxVision.Connectors.Application.Sync;

/// <summary>
/// Lleva la cuenta de los mensajes que fallan de forma transitoria. Un fallo Pending frena el cursor;
/// pasado el tope (intentos y antigüedad) se abandona para no trabar la cuenta. Un fetch bueno lo resuelve.
/// </summary>
public sealed class MessageFetchFailureTracker(
    IProviderMessageFetchFailureRepository repository,
    MessageFetchFailureLimits limits,
    ISyncMetrics metrics,
    ILogger<MessageFetchFailureTracker> logger
)
{
    /// <returns>Cuántos fallos siguen frenando el cursor.</returns>
    public async Task<int> RecordAsync(
        TenantEmailAccount account,
        IReadOnlyList<ProviderMessageFetch> fetched,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        var pending = (await repository.ListPendingAsync(account.Id, ct)).ToDictionary(f => f.ProviderMessageId);
        var blocking = 0;

        foreach (var item in fetched)
        {
            if (item.Message is not null)
            {
                if (pending.TryGetValue(item.ProviderMessageId, out var recovered))
                    recovered.Resolve();
                continue;
            }

            if (item.IsNotFound)
                continue;

            if (await RegisterAsync(account, item, pending, nowUtc, ct))
                blocking++;
        }

        return blocking;
    }

    private async Task<bool> RegisterAsync(
        TenantEmailAccount account,
        ProviderMessageFetch item,
        Dictionary<string, ProviderMessageFetchFailure> pending,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        var failure =
            pending.GetValueOrDefault(item.ProviderMessageId)
            ?? await repository.GetAsync(account.Id, item.ProviderMessageId, ct);

        if (failure is null)
        {
            var created = ProviderMessageFetchFailure.Create(
                account.Id,
                item.ProviderMessageId,
                item.Error?.Message,
                nowUtc
            );
            if (created.IsFailure)
            {
                // Id inválido para registrar: no se puede llevar la cuenta, así que tampoco frena.
                logger.LogError(
                    "Could not track fetch failure for message {MessageId} on account {AccountId}: {Error}",
                    item.ProviderMessageId,
                    account.Id,
                    created.Error.Code
                );
                return false;
            }

            await repository.AddAsync(created.Value, ct);
            return true;
        }

        failure.RegisterFailure(item.Error?.Message, nowUtc);
        if (failure.Status == ProviderMessageFetchFailureStatus.Abandoned)
            return false;

        if (!failure.ShouldAbandon(limits.MaxAttempts, limits.MinAge, nowUtc))
            return true;

        failure.Abandon();
        metrics.MessageAbandoned(account.ProviderCode);
        logger.LogError(
            "Message {MessageId} on account {AccountId} abandoned after {Attempts} failed fetches since {FirstFailedAtUtc}; it no longer blocks the cursor.",
            item.ProviderMessageId,
            account.Id,
            failure.Attempts,
            failure.FirstFailedAtUtc
        );
        return false;
    }
}
