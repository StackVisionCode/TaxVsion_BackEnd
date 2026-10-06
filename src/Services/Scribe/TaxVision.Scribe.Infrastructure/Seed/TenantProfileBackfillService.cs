using BuildingBlocks.Infrastructure.Hosting;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaxVision.Scribe.Application.Abstractions;
using TaxVision.Scribe.Infrastructure.Providers.TenantDirectory;

namespace TaxVision.Scribe.Infrastructure.Seed;

/// <summary>
/// Rellena la proyeccion con las oficinas que YA existian cuando se desplego esta tabla.
/// <c>TenantCreatedIntegrationEvent</c> solo cubre las nuevas; sin esto, toda oficina anterior al
/// despliegue manda sus correos sin nombre en la cabecera ni en el pie — el caso de todos los
/// clientes de hoy.
///
/// <para>No se republica el evento: lo consumen cinco servicios y dispara la invitacion del admin y
/// el trial de la suscripcion. Un GET de solo lectura no puede hacer dano.</para>
///
/// <para>No bloquea el arranque: si Tenant no contesta, se registra y queda para el proximo boot.</para>
/// </summary>
public sealed class TenantProfileBackfillService(
    IServiceScopeFactory scopeFactory,
    IHostApplicationLifetime lifetime,
    ILogger<TenantProfileBackfillService> logger
) : DeferredStartupHostedService(lifetime, logger)
{
    private const int PageSize = 100;

    /// <summary>En un despliegue en frio Auth puede no estar escuchando todavia cuando se pide el token.</summary>
    private static readonly TimeSpan[] RetryDelays = [TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)];

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<ITenantDirectoryClient>();
        var repository = scope.ServiceProvider.GetRequiredService<ITenantProfileRefRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var outcome = await TenantProfileBackfillPlan.RunAsync(client, repository, PageSize, cancellationToken);

        foreach (var delay in RetryDelays)
        {
            if (outcome.TenantReachable || cancellationToken.IsCancellationRequested)
                break;

            logger.LogInformation("Tenant not reachable yet; retrying the profile backfill in {Delay}.", delay);
            await Task.Delay(delay, cancellationToken);
            outcome = await TenantProfileBackfillPlan.RunAsync(client, repository, PageSize, cancellationToken);
        }

        if (outcome.Added > 0)
            await unitOfWork.SaveChangesAsync(cancellationToken);

        // Tres desenlaces, tres mensajes: "no se pudo preguntar" no es "ya estaba completo".
        if (!outcome.TenantReachable)
            logger.LogWarning("Could not reach Tenant; the profile backfill is left for the next boot.");
        else if (outcome.Added > 0)
            logger.LogInformation("Tenant profile backfill added {Added} office name(s).", outcome.Added);
        else
            logger.LogInformation("Tenant profile directory already complete; nothing to backfill.");
    }
}
