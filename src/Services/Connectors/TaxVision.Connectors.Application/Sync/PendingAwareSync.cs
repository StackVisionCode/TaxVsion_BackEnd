using BuildingBlocks.Results;
using TaxVision.Connectors.Application.Abstractions;

namespace TaxVision.Connectors.Application.Sync;

/// <summary>
/// Corre el sync con el lock tomado y, si mientras tanto llegó otro aviso (marca "pendiente"), hace
/// otra pasada. Tope de pasadas para que una cuenta muy activa no retenga el lock para siempre.
/// </summary>
internal static class PendingAwareSync
{
    public const int MaxPasses = 3;

    /// <returns>Suma de mensajes publicados en todas las pasadas.</returns>
    public static async Task<Result<int>> RunAsync(
        Guid accountId,
        ILockHandle lockHandle,
        TimeSpan lockTtl,
        ISyncPendingFlag pendingFlag,
        Func<Task<Result<int>>> syncPass,
        CancellationToken ct
    )
    {
        var published = 0;
        for (var pass = 1; pass <= MaxPasses; pass++)
        {
            var result = await syncPass();
            if (result.IsFailure)
                return result;

            published += result.Value;

            // Si quedó marca al llegar al tope, la consume el próximo sync o la reconciliación.
            if (pass == MaxPasses || !await pendingFlag.TryConsumeAsync(accountId, ct))
                break;

            // Perdimos el lock: otro dueño ya sincroniza y cubre lo pendiente.
            if (!await lockHandle.ExtendAsync(lockTtl, ct))
                break;
        }

        return Result.Success(published);
    }
}
