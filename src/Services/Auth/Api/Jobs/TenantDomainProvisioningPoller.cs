using BuildingBlocks.Infrastructure.Hosting;
using BuildingBlocks.Jobs;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.TenantDomains;
using TaxVision.Auth.Infrastructure.Persistence;

namespace TaxVision.Auth.Api.Jobs;

/// <summary>
/// Fase A5/A6/A7 — re-consulta cada 5 minutos los custom hostnames en Provisioning y
/// los pasa a Active en cuanto Cloudflare confirma status=active &amp; ssl.status=active
/// (o a Failed si Cloudflare los bloqueó), sin que el tenant admin tenga que hacer
/// clic en "verificar". PUT .../activate sigue disponible para que lo confirme
/// manualmente sin esperar el siguiente ciclo. domain.MarkActive/MarkFailed encolan el
/// domain event correspondiente; la unidad del job (BB-3) lo guarda con el cambio y su handler
/// audita y publica — el poller no sabe nada de auditoría ni de integration events.
/// </summary>
public sealed class TenantDomainProvisioningPoller(
    IServiceScopeFactory scopeFactory,
    IHostApplicationLifetime lifetime,
    ILogger<TenantDomainProvisioningPoller> logger
) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Primer poll inmediato — sin esperar acá, el outbox de Wolverine revienta con
        // WolverineHasNotStartedException si el poll corre antes de que termine de arrancar.
        await lifetime.WaitForApplicationStartedAsync(stoppingToken);

        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await PollOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Tenant domain provisioning poll failed.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task PollOnceAsync(CancellationToken ct)
    {
        List<Guid> pending;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var domains = scope.ServiceProvider.GetRequiredService<ITenantDomainRepository>();
            pending = (await domains.GetProvisioningCustomHostnamesAsync(ct)).Select(domain => domain.Id).ToList();
        }
        if (pending.Count == 0)
            return;

        // Una unidad por dominio: el cambio de estado y su evento de dominio se confirman juntos.
        var summary = await TransactionalJobUnit.RunEachAsync<Guid, AuthDbContext>(
            scopeFactory,
            pending,
            async (domainId, unit, token) =>
            {
                var domains = unit.Services.GetRequiredService<ITenantDomainRepository>();
                var cloudflare = unit.Services.GetRequiredService<ICloudflareProvisioningClient>();
                return await domains.GetByIdAsync(domainId, token) is { Status: TenantDomainStatus.Provisioning } domain
                    && await TryAdvanceAsync(domain, cloudflare, token);
            },
            logger,
            ct
        );

        if (summary.Committed > 0)
        {
            logger.LogInformation(
                "Tenant domain provisioning poll: {Transitioned}/{Checked} hostname(s) changed state.",
                summary.Committed,
                pending.Count
            );
        }
    }

    private async Task<bool> TryAdvanceAsync(
        TenantDomain domain,
        ICloudflareProvisioningClient cloudflare,
        CancellationToken ct
    )
    {
        if (domain.CloudflareCustomHostnameId is not { } cloudflareId)
            return false;

        var statusResult = await cloudflare.GetCustomHostnameAsync(cloudflareId, ct);
        if (statusResult.IsFailure)
        {
            logger.LogWarning(
                "Could not poll Cloudflare status for TenantDomain {DomainId}: {Error}",
                domain.Id,
                statusResult.Error.Message
            );
            return false;
        }

        var status = statusResult.Value;
        if (status.IsFullyActive)
            return domain.MarkActive(DateTime.UtcNow).IsSuccess;

        if (status.IsBlocked)
            return domain.MarkFailed("cloudflare_blocked").IsSuccess;

        return false;
    }
}
