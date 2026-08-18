using BuildingBlocks.Infrastructure.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Tasks.Application.Attachments.Abstractions;

namespace TaxVision.Tasks.Infrastructure.Jobs;

/// <summary>
/// Cierra los adjuntos que quedaron esperando un veredicto ya emitido. Diez minutos de gracia: por
/// debajo de eso el escaneo puede seguir en curso y preguntar sería ruido.
/// </summary>
internal sealed class StaleAttachmentJob(
    IServiceScopeFactory scopeFactory,
    IOptions<StaleAttachmentOptions> options,
    ILogger<StaleAttachmentJob> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // La pasada de arranque publica eventos, y Wolverine todavía no está listo cuando el host
        // levanta los hosted services: sin esta espera lanza WolverineHasNotStartedException y la
        // primera corrida se pierde entera.
        await scopeFactory
            .CreateScope()
            .ServiceProvider.GetRequiredService<IHostApplicationLifetime>()
            .WaitForApplicationStartedAsync(stoppingToken);

        var settings = options.Value;
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(settings.IntervalMinutes));

        // Una pasada al arrancar: si el servicio estuvo caído mientras CloudStorage publicaba, los
        // veredictos de esa ventana no vuelven y los adjuntos ya están esperando.
        do
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var resolver = scope.ServiceProvider.GetRequiredService<IStaleAttachmentResolver>();
                await resolver.ResolveAsync(
                    TimeSpan.FromMinutes(settings.GraceMinutes),
                    settings.BatchSize,
                    stoppingToken
                );
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "StaleAttachmentJob failed; retrying on the next tick.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
