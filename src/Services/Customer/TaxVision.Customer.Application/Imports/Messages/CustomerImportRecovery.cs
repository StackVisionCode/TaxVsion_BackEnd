using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Imports.Configuration;

namespace TaxVision.Customer.Application.Imports.Messages;

/// <summary>
/// Un import sin avance (mensaje perdido, proceso caído) se vuelve a encolar para que se retome donde quedó. Tras
/// <see cref="CustomerImportRecoveryOptions.MaxRecoveries"/> se da por fallido con su aviso, para no dejar a la
/// oficina sin poder importar. Una unidad por import, con outbox.
/// </summary>
public static class CustomerImportRecovery
{
    public static async Task<int> RecoverAsync(
        IServiceScopeFactory sf,
        DateTime nowUtc,
        CustomerImportRecoveryOptions options,
        ILogger logger,
        CancellationToken ct
    )
    {
        var idle = TimeSpan.FromMinutes(options.IdleMinutes);
        IReadOnlyList<Guid> ids;
        await using (var scope = sf.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<ICustomerImportRecoveryStore>();
            ids = await store.ListStuckIdsAsync(nowUtc - idle, options.BatchSize, ct);
        }

        var recovered = 0;
        foreach (var id in ids)
            if (await RecoverOneAsync(sf, id, nowUtc, idle, options.MaxRecoveries, logger, ct))
                recovered++;
        return recovered;
    }

    private static async Task<bool> RecoverOneAsync(
        IServiceScopeFactory sf,
        Guid attemptId,
        DateTime nowUtc,
        TimeSpan idle,
        int maxRecoveries,
        ILogger logger,
        CancellationToken ct
    )
    {
        await using var scope = sf.CreateAsyncScope();
        var runner = scope.ServiceProvider.GetRequiredService<ITransactionalUnitRunner>();
        var acted = false;
        await runner.RunAsync(
            async (unit, token) =>
            {
                var repo = unit.Services.GetRequiredService<ICustomerImportRepository>();
                var attempt = await repo.GetByIdAsync(attemptId, token);
                // Se vuelve a mirar dentro de la unidad: pudo avanzar desde la búsqueda.
                if (attempt is null || !attempt.IsStuck(nowUtc, idle))
                    return false;

                if (attempt.RecoveryCount >= maxRecoveries)
                {
                    attempt.Fail(
                        $"The import stopped making progress after {attempt.RecoveryCount} recovery attempts."
                    );
                    var correlation = unit.Services.GetRequiredService<ICorrelationContext>();
                    await unit.PublishAsync(ImportAttemptLifecycle.FailedEvent(attempt, correlation.CorrelationId));
                    logger.LogWarning("Import {AttemptId} stuck too many times; failed", attemptId);
                }
                else
                {
                    attempt.MarkRecoveryRequested(nowUtc);
                    await unit.PublishAsync(new RunCustomerImportMessage(attempt.Id));
                    logger.LogWarning(
                        "Import {AttemptId} stuck; queued again (recovery {Count})",
                        attemptId,
                        attempt.RecoveryCount
                    );
                }
                acted = true;
                return true;
            },
            ct
        );
        return acted;
    }
}
