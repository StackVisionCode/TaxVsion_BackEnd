using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wolverine;

namespace TaxVision.Auth.Application.Offboarding;

/// <summary>
/// Un tick del barrido: con el lock tomado, despacha <see cref="MarkOffboardingRunIncompleteCommand"/> por cada
/// corrida vencida con pasos sin acuse. Cada comando corre en su propia transacción con outbox (marca + aviso
/// atómicos); el lock evita que dos réplicas avisen dos veces la misma corrida.
/// </summary>
public sealed class OffboardingRunSweeper(
    IOffboardingRunRepository runs,
    IOffboardingSweepLock sweepLock,
    IMessageBus bus,
    IOptions<OffboardingTrackingOptions> options,
    ILogger<OffboardingRunSweeper> logger
)
{
    /// <summary>Corridas marcadas incompletas en este tick (0 si otra réplica tenía el lock).</summary>
    public async Task<int> SweepAsync(DateTime nowUtc, CancellationToken ct)
    {
        await using var held = await sweepLock.TryAcquireAsync(ct);
        if (held is null)
            return 0;

        var opt = options.Value;
        var overdue = await runs.ListOverdueAsync(nowUtc - opt.Timeout, opt.BatchSize, ct);
        var flagged = 0;
        foreach (var run in overdue)
        {
            try
            {
                // Comando local en scope nuevo: sin este stamp el filtro de tenant fail-closed no ve la corrida.
                bus.TenantId = run.TenantId.ToString();
                var result = await bus.InvokeAsync<Result>(
                    new MarkOffboardingRunIncompleteCommand(run.TenantId, run.RunId),
                    ct
                );
                if (result.IsSuccess)
                    flagged++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Una corrida que falla no frena las demás; el tick siguiente la vuelve a listar.
                logger.LogError(ex, "Failed to flag offboarding run {RunId} as incomplete.", run.RunId);
            }
        }

        return flagged;
    }
}
