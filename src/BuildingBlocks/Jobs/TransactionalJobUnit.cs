using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wolverine.EntityFrameworkCore;

namespace BuildingBlocks.Jobs;

/// <summary>
/// BB-3. Una unidad de trabajo de un job: scope propio, transacción propia y outbox de EF. Los cambios y
/// los avisos se confirman juntos con <c>SaveChangesAndFlushMessagesAsync</c>, o no queda ninguno.
/// Una unidad por entidad: un conflicto o un error en una no arrastra a las demás.
///
/// <para>Reemplaza "guardar y después publicar con el <c>IMessageBus</c> del scope", que pierde el aviso
/// si el proceso cae entre los dos pasos. Modelo: <c>OverdueTaskSweepJob</c> de Tasks.</para>
/// </summary>
public static class TransactionalJobUnit
{
    /// <summary>
    /// Ejecuta una unidad. La acción devuelve <c>true</c> para confirmar o <c>false</c> si no había
    /// nada que hacer (no se guarda). Solo relanza la cancelación del job.
    /// </summary>
    public static async Task<JobUnitResult> RunAsync<TDbContext>(
        IServiceScopeFactory scopeFactory,
        Func<JobUnitContext<TDbContext>, CancellationToken, Task<bool>> work,
        ILogger logger,
        CancellationToken ct
    )
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(logger);

        await using var scope = scopeFactory.CreateAsyncScope();
        var outbox = scope.ServiceProvider.GetRequiredService<IDbContextOutbox<TDbContext>>();

        try
        {
            // Transacción desde el inicio: cubre también un SaveChanges intermedio de la acción.
            // Si algo falla, se descarta al cerrar el scope.
            await outbox.DbContext.Database.BeginTransactionAsync(ct);

            if (!await work(new JobUnitContext<TDbContext>(scope.ServiceProvider, outbox), ct))
                return new JobUnitResult(JobUnitOutcome.Skipped);

            await outbox.SaveChangesAndFlushMessagesAsync(ct);
            return new JobUnitResult(JobUnitOutcome.Committed);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (IsConflict(ex))
        {
            logger.LogDebug(ex, "Job unit skipped: the row was modified concurrently; the next sweep retries it.");
            return new JobUnitResult(JobUnitOutcome.Conflict, ex);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Job unit failed; nothing was committed and the remaining units continue.");
            return new JobUnitResult(JobUnitOutcome.Failed, ex);
        }
    }

    /// <summary>Una unidad por ítem, en serie. Devuelve el conteo por resultado.</summary>
    public static async Task<JobBatchSummary> RunEachAsync<TItem, TDbContext>(
        IServiceScopeFactory scopeFactory,
        IEnumerable<TItem> items,
        Func<TItem, JobUnitContext<TDbContext>, CancellationToken, Task<bool>> work,
        ILogger logger,
        CancellationToken ct
    )
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(work);

        int committed = 0,
            skipped = 0,
            conflicts = 0,
            failed = 0;
        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested();
            var result = await RunAsync<TDbContext>(scopeFactory, (unit, token) => work(item, unit, token), logger, ct);
            switch (result.Outcome)
            {
                case JobUnitOutcome.Committed:
                    committed++;
                    break;
                case JobUnitOutcome.Skipped:
                    skipped++;
                    break;
                case JobUnitOutcome.Conflict:
                    conflicts++;
                    break;
                default:
                    failed++;
                    break;
            }
        }

        return new JobBatchSummary(committed, skipped, conflicts, failed);
    }

    /// <summary>Código con el que los DbContext de los servicios traducen la concurrencia de EF.</summary>
    public const string ConcurrencyConflictCode = "Persistence.ConcurrencyConflict";

    /// <summary>
    /// Solo concurrencia (la excepción cruda de EF o su traducción). Un índice único violado no es
    /// conflicto: se repetiría en cada barrido, así que cuenta como fallo y se registra como error.
    /// </summary>
    private static bool IsConflict(Exception ex) =>
        ex is DbUpdateConcurrencyException || ex is ConflictException { Code: ConcurrencyConflictCode };
}
