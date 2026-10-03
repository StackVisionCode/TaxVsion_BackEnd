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

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<ITenantDirectoryClient>();
        var repository = scope.ServiceProvider.GetRequiredService<ITenantDirectoryRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var added = await BackfillPlan.RunAsync(client, repository, PageSize, cancellationToken);

        if (added == 0)
        {
            logger.LogInformation("Tenant directory already complete; nothing to backfill.");
            return;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Backfilled {Count} tenant(s) into the Postmaster directory.", added);
    }
}
