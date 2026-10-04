using BuildingBlocks.Infrastructure.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Requests.Commands.Send;
using Wolverine;

namespace TaxVision.Signature.Infrastructure.Scheduling;

/// <summary>
/// F3 — Scheduled Send. Cada minuto barre las solicitudes <c>Scheduled</c> cuyo
/// <c>ScheduledSendAtUtc</c> ya llegó e invoca el mismo <see cref="SendSignatureRequestCommand"/>
/// que usa el envío manual: así se centraliza la emisión del evento de envío, la rotación de
/// tokens, el guardado y la invalidación de caché. Idempotente: si el envío ya ocurrió
/// (concurrencia con un envío manual), <c>Send</c> del dominio devuelve <c>NotEditable</c> y el
/// scheduler lo registra sin levantar error. Scan cross-tenant.
/// </summary>
public sealed class ScheduledSendScheduler(IServiceProvider serviceProvider, ILogger<ScheduledSendScheduler> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);
    private const int BatchSize = 50;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var lifetime = serviceProvider.GetRequiredService<IHostApplicationLifetime>();
        await lifetime.WaitForApplicationStartedAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunOnceSafeAsync(stoppingToken);
            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    private async Task RunOnceSafeAsync(CancellationToken ct)
    {
        try
        {
            await RunOnceAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ScheduledSendScheduler iteration failed.");
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = serviceProvider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ISignatureRequestRepository>();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        var now = DateTime.UtcNow;
        var candidates = await repository.ListScheduledReadyToSendAsync(now, BatchSize, ct);
        if (candidates.Count == 0)
            return;

        var sent = 0;
        foreach (var request in candidates)
        {
            // El handler re-lee el aggregate dentro de su propio scope: necesitamos invocar por bus
            // para que la auditoría, los eventos y la cadena de persistencia funcionen igual que un envío
            // manual (guardrail: no duplicar pipelines).
            var cmd = new SendSignatureRequestCommand(request.TenantId, request.Id);
            try
            {
                await bus.InvokeAsync(cmd, ct);
                sent++;
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "ScheduledSendScheduler could not send scheduled request {RequestId}; will retry on next tick.",
                    request.Id
                );
            }
        }

        if (sent > 0)
            logger.LogInformation("ScheduledSendScheduler dispatched {Count} scheduled requests.", sent);
    }
}
