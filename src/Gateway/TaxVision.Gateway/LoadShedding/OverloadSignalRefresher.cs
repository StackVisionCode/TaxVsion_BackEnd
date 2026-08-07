using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TaxVision.Gateway.LoadShedding;

/// <summary>Mueve el cálculo de la señal de sobrecarga fuera del camino de la petición (GW-05).</summary>
public sealed class OverloadSignalRefresher(OverloadSignal signal, ILogger<OverloadSignalRefresher> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(OverloadSignal.RefreshInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                signal.Refresh();
            }
            catch (Exception ex)
            {
                // Si el refresco muere, la señal se congela en su último valor y el shedder deja de
                // reaccionar en silencio. Se registra y se sigue: un fallo puntual no debe matar el
                // bucle.
                logger.LogError(ex, "Failed to refresh the load-shedding overload signal.");
            }
        }
    }
}
