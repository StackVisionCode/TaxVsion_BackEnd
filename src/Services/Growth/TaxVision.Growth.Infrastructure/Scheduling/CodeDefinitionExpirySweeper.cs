using BuildingBlocks.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaxVision.Codes.Application.Abstractions;
using TaxVision.Codes.Application.Definitions.ExpireCode;
using Wolverine;

namespace TaxVision.Growth.Infrastructure.Scheduling;

/// <summary>
/// Barrido diario: pasa a Expired los códigos con fecha de fin vencida. Cada código va en su propia invocación
/// (una transacción por código, con el tenant del código); si uno falla, los demás siguen. Dos barridos a la vez
/// son inocuos: el dominio solo expira lo que sigue abierto y la RowVersion frena la segunda escritura.
/// </summary>
public sealed class CodeDefinitionExpirySweeper(
    IServiceScopeFactory scopeFactory,
    ILogger<CodeDefinitionExpirySweeper> logger
) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromDays(1);
    private const int BatchSize = 200;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Code expiry sweep failed; will retry next tick.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var due = await scope
            .ServiceProvider.GetRequiredService<ICodeDefinitionRepository>()
            .GetExpiredOpenAsync(DateTime.UtcNow, BatchSize, ct);
        if (due.Count == 0)
            return;

        var expired = 0;
        foreach (var code in due)
        {
            // Scope propio por código: cada invocación es su propia unidad de trabajo.
            await using var unit = scopeFactory.CreateAsyncScope();
            var bus = unit.ServiceProvider.GetRequiredService<IMessageBus>();
            bus.TenantId = code.TenantId.ToString();
            var result = await bus.InvokeAsync<Result>(new ExpireCodeCommand(code.TenantId, code.CodeDefinitionId), ct);
            if (result.IsSuccess)
                expired++;
            else
                logger.LogWarning(
                    "Failed to expire code {CodeDefinitionId} (tenant {TenantId}): {Error}",
                    code.CodeDefinitionId,
                    code.TenantId,
                    result.Error.Code
                );
        }

        logger.LogInformation("Code expiry sweep expired {Expired}/{Total} code(s).", expired, due.Count);
    }
}
