using BuildingBlocks.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Sms.Application;
using TaxVision.Sms.Application.Webhooks.Commands;
using Wolverine;

namespace TaxVision.Sms.Infrastructure.Jobs;

/// <summary>
/// Job de fondo que reconcilia el estado de los SMS por PULL (agnóstico de proveedor). Es el backstop de los
/// DLR por webhook: en cada tick pide a cada proveedor el estado real de los mensajes atascados en Accepted y
/// aplica la transición final (Delivered/Failed/Undeliverable). Resuelve el caso en que el proveedor no puede
/// empujar el DLR (p. ej. no alcanza <c>localhost</c> en dev) y el de DLRs perdidos en prod. Se apaga con
/// <c>Sms:Reconciliation:Enabled=false</c>; el endpoint manual sigue disponible aunque el job esté OFF.
/// </summary>
public sealed class SmsStatusReconciliationService(
    IServiceProvider services,
    IOptions<SmsOptions> options,
    ILogger<SmsStatusReconciliationService> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var config = options.Value.Reconciliation;
        if (!config.Enabled)
        {
            logger.LogInformation("SMS status reconciliation job is disabled (Sms:Reconciliation:Enabled=false).");
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(15, config.IntervalSeconds));

        // Arranque diferido para no competir con la migración/warmup del proceso.
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(config, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "SMS reconciliation tick failed; will retry next interval.");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task TickAsync(SmsReconciliationOptions config, CancellationToken ct)
    {
        using var scope = services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        await bus.InvokeAsync<Result<ReconcileSmsStatusesResponse>>(
            new ReconcileSmsStatusesCommand(TenantId: null, config.BatchSize, config.MinAgeSeconds),
            ct
        );
    }
}
