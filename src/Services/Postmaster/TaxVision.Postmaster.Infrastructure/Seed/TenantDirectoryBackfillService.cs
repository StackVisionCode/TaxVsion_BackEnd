using BuildingBlocks.Infrastructure.Hosting;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaxVision.Postmaster.Application.Abstractions;
using TaxVision.Postmaster.Infrastructure.Providers.TenantDirectory;

namespace TaxVision.Postmaster.Infrastructure.Seed;

/// <summary>
/// Rellena el directorio local con las oficinas que YA existían cuando se desplegó esta tabla.
/// <c>TenantCreatedIntegrationEvent</c> solo cubre las nuevas; sin este backfill, toda oficina
/// anterior al despliegue seguiría viendo <c>From: TaxVision</c> en sus correos para siempre, que es
/// justo el caso de todos los clientes de hoy.
///
/// <para><b>Por qué no se republica el evento:</b> <c>TenantCreatedIntegrationEvent</c> lo consumen
/// cinco servicios y dispara la invitación del admin, el trial de la suscripción y el
/// aprovisionamiento de almacenamiento. Reemitirlo para llenar una tabla le mandaría a cada cliente
/// existente una invitación nueva y le reiniciaría el trial. Un GET de solo lectura no puede.</para>
///
/// <para><b>Idempotente y barato en los arranques siguientes:</b> primero mira qué ids ya tiene y
/// solo escribe los que faltan; cuando no falta ninguno, no escribe nada. No se corta a la primera
/// página vacía por descuido — ese es justamente el final del listado.</para>
///
/// <para><b>No bloquea el arranque:</b> si Tenant no contesta, se registra y se deja para el próximo
/// boot. Un correo sin el nombre de la oficina es peor que con él, pero muchísimo mejor que un
/// Postmaster que no arranca.</para>
/// </summary>
public sealed class TenantDirectoryBackfillService(
    IServiceScopeFactory scopeFactory,
    IHostApplicationLifetime lifetime,
    ILogger<TenantDirectoryBackfillService> logger
) : DeferredStartupHostedService(lifetime, logger)
{
    private const int PageSize = 100;

    /// <summary>
    /// Esperas entre intentos. <c>postmaster-api</c> NO depende de <c>auth-api</c> en el compose, así
    /// que en un despliegue en frío pide el token M2M antes de que Auth escuche y se queda sin
    /// backfill hasta el siguiente reinicio. Medido en prod el 2026-10-03.
    /// </summary>
    private static readonly TimeSpan[] RetryDelays = [TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)];

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<ITenantDirectoryClient>();
        var repository = scope.ServiceProvider.GetRequiredService<ITenantDirectoryRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var outcome = await BackfillPlan.RunAsync(client, repository, PageSize, cancellationToken);

        foreach (var delay in RetryDelays)
        {
            if (outcome.TenantReachable || cancellationToken.IsCancellationRequested)
                break;

            logger.LogInformation("Tenant not reachable yet; retrying the directory backfill in {Delay}.", delay);
            await Task.Delay(delay, cancellationToken);
            outcome = await BackfillPlan.RunAsync(client, repository, PageSize, cancellationToken);
        }

        if (outcome.Added > 0)
            await unitOfWork.SaveChangesAsync(cancellationToken);

        // Tres desenlaces distintos y tres mensajes distintos. Antes "no se pudo preguntar" se
        // reportaba como "ya está completo", así que el log decía que todo iba bien con la tabla vacía.
        if (!outcome.TenantReachable)
            logger.LogWarning(
                "Tenant directory backfill could NOT reach Tenant; wrote {Count} row(s) and will retry next boot.",
                outcome.Added
            );
        else if (outcome.Added == 0)
            logger.LogInformation("Tenant directory already complete; nothing to backfill.");
        else
            logger.LogInformation("Backfilled {Count} tenant(s) into the Postmaster directory.", outcome.Added);
    }
}
