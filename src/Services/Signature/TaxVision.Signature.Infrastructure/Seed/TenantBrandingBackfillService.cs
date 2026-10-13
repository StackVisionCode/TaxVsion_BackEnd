using BuildingBlocks.Infrastructure.Hosting;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaxVision.Signature.Application.Abstractions;

namespace TaxVision.Signature.Infrastructure.Seed;

/// <summary>
/// Completa la proyección <c>TenantBrandingRefs</c> con las oficinas que ya existían antes que sus
/// consumers. <c>TenantCreatedIntegrationEvent</c> solo cubre las nuevas, y la columna <c>SubDomain</c>
/// llegó después con valor vacío: sin esto, el certificado de finalización de esas oficinas sale sin su
/// nombre y la página pública de firma recibe el subdominio vacío.
///
/// <para><b>Por qué no se republica el evento:</b> <c>TenantCreatedIntegrationEvent</c> lo consumen
/// varios servicios y dispara la invitación del admin, el trial de la suscripción y el aprovisionamiento
/// de almacenamiento. Se lee el directorio de Tenant (GET de solo lectura, el mismo que usa Postmaster).</para>
///
/// <para><b>Idempotente:</b> crea las filas que faltan y rellena nombre/subdominio solo donde están vacíos;
/// el logo nunca se toca. Cuando no falta nada, no escribe.</para>
///
/// <para><b>No bloquea el arranque:</b> si Tenant no contesta, se registra y se deja para el próximo boot.</para>
/// </summary>
internal sealed class TenantBrandingBackfillService(
    IServiceScopeFactory scopeFactory,
    IHostApplicationLifetime lifetime,
    ILogger<TenantBrandingBackfillService> logger
) : DeferredStartupHostedService(lifetime, logger)
{
    private const int PageSize = 100;

    /// <summary>
    /// Esperas entre intentos: en un arranque en frío Signature puede pedir el token M2M antes de que
    /// Auth o Tenant escuchen (mismo caso medido en Postmaster el 2026-10-03).
    /// </summary>
    private static readonly TimeSpan[] RetryDelays = [TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)];

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<ITenantDirectoryClient>();
        var repository = scope.ServiceProvider.GetRequiredService<ITenantBrandingRefRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var outcome = await TenantBrandingBackfillPlan.RunAsync(client, repository, PageSize, cancellationToken);

        foreach (var delay in RetryDelays)
        {
            if (outcome.TenantReachable || cancellationToken.IsCancellationRequested)
                break;

            logger.LogInformation("Tenant not reachable yet; retrying the branding backfill in {Delay}.", delay);
            await Task.Delay(delay, cancellationToken);
            outcome = await TenantBrandingBackfillPlan.RunAsync(client, repository, PageSize, cancellationToken);
        }

        if (outcome.Changed > 0)
            await unitOfWork.SaveChangesAsync(cancellationToken);

        if (!outcome.TenantReachable)
            logger.LogWarning(
                "Tenant branding backfill could NOT reach Tenant; added {Added} and completed {Completed} row(s), will retry next boot.",
                outcome.Added,
                outcome.Completed
            );
        else if (outcome.Changed == 0)
            logger.LogInformation("Tenant branding projection already complete; nothing to backfill.");
        else
            logger.LogInformation(
                "Tenant branding backfill: added {Added} office(s), completed name/subdomain on {Completed}.",
                outcome.Added,
                outcome.Completed
            );
    }
}
