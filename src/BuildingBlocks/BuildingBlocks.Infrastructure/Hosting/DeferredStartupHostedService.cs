using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Infrastructure.Hosting;

/// <summary>
/// Base para un hosted service de arranque que corre UNA vez (seeder, backfill, bootstrap) y que
/// toca el bus de mensajería o un DbContext que auto-publica domain events en SaveChangesAsync. El
/// host genérico arranca los <see cref="IHostedService"/> en el orden de registro en DI — si uno de
/// estos corre su lógica dentro de <c>StartAsync</c> antes de que Wolverine (u otro hosted service
/// registrado después) termine de inicializar, revienta con WolverineHasNotStartedException. Esta
/// base elimina el problema de raíz, sin importar el orden de registro: <c>StartAsync</c> nunca
/// bloquea el arranque del host, y la lógica real (<see cref="ExecuteAsync"/>) solo corre una vez
/// que TODO el host — Wolverine incluido — terminó de arrancar.
/// <para>
/// Patrón adoptado de <c>ScribeNotificationTemplateSeeder</c> (el único lugar del monorepo que ya
/// lo resolvía bien antes de esta clase), generalizado para no reimplementarlo a mano en cada
/// servicio nuevo. Un fallo en <see cref="ExecuteAsync"/> se loguea y se traga — nunca debe tumbar
/// el host, porque para cuando corre la aplicación ya está atendiendo tráfico.
/// </para>
/// </summary>
public abstract class DeferredStartupHostedService(IHostApplicationLifetime lifetime, ILogger logger) : IHostedService
{
    private Task? _executionTask;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _executionTask = RunAfterStartupAsync();
        return Task.CompletedTask;
    }

    /// <summary>Lógica real de arranque diferido — corre una sola vez, después de ApplicationStarted.</summary>
    protected abstract Task ExecuteAsync(CancellationToken cancellationToken);

    public virtual async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_executionTask is null)
            return;

        try
        {
            await _executionTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // El host está forzando el shutdown; la tarea diferida puede quedar incompleta —
            // es aceptable (es idempotente por diseño en todos los casos actuales) y preferible a
            // bloquear el shutdown indefinidamente.
        }
    }

    private async Task RunAfterStartupAsync()
    {
        try
        {
            await lifetime.WaitForApplicationStartedAsync().ConfigureAwait(false);
            await ExecuteAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "{ServiceType} failed during deferred startup execution.", GetType().Name);
        }
    }
}
